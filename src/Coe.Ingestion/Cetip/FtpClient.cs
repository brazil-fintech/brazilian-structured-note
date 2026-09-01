using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Security.Authentication;
using System.Text;

namespace Coe.Ingestion.Cetip;

/// <summary>One reply from the control channel: the three-digit code and the text that came with it.</summary>
public sealed record FtpReply(int Code, string Text)
{
    /// <summary>2xx and 3xx — the command was accepted, or accepted pending more input.</summary>
    public bool IsPositive => Code is >= 200 and < 400;

    public override string ToString() => $"{Code} {Text}";
}

public sealed class FtpException : Exception
{
    public FtpException(string message) : base(message) { }
    public FtpException(string message, Exception inner) : base(message, inner) { }

    /// <summary>The reply that caused this, when the failure came from the server rather than the socket.</summary>
    public FtpReply? Reply { get; init; }
}

/// <summary>
/// A small RFC 959 client, enough for what CETIP publishes: log in, list a directory, pull a
/// file. Written by hand rather than taken from <c>FtpWebRequest</c>, which .NET has obsoleted
/// (SYSLIB0014) and which cannot report a listing's timestamps without a second round trip.
///
/// Passive mode only. CETIP's public area sits behind a firewall that will not open a port back
/// to the client, and an active-mode fallback would be code no deployment can exercise.
/// Explicit TLS (<c>AUTH TLS</c>) is supported and off by default: the manual's own links are
/// <c>ftp://</c>, and the directory is world-readable, so there is nothing to protect on the
/// wire and nothing to gain from a handshake the server may not offer.
/// </summary>
public sealed partial class FtpClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly bool _useSsl;
    private readonly TimeSpan _timeout;

    private TcpClient? _control;
    private Stream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    /// <summary>Control-channel text is Latin-1 in practice; paths here are ASCII either way.</summary>
    private static readonly Encoding ControlEncoding = Encoding.Latin1;

    public FtpClient(string host, int port = 21, bool useSsl = false, TimeSpan? timeout = null)
    {
        _host = host;
        _port = port;
        _useSsl = useSsl;
        _timeout = timeout ?? TimeSpan.FromSeconds(60);
    }

    /// <summary>Every reply the session exchanged, in order. Logged when a sync fails.</summary>
    public List<string> Transcript { get; } = [];

    public async Task ConnectAsync(string user, string password, CancellationToken ct = default)
    {
        _control = new TcpClient { SendTimeout = (int)_timeout.TotalMilliseconds, ReceiveTimeout = (int)_timeout.TotalMilliseconds };

        // Sockets do not honour SendTimeout while connecting, and a firewall that drops the
        // SYN rather than refusing it leaves the OS retrying for minutes. The worker starts
        // behind this call, so the wait is bounded here instead.
        using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connectTimeout.CancelAfter(_timeout);
            try
            {
                await _control.ConnectAsync(_host, _port, connectTimeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new FtpException($"Timed out connecting to {_host}:{_port} after {_timeout}.");
            }
        }

        _stream = _control.GetStream();
        Rebind();

        var greeting = await ReadReplyAsync(ct);
        if (greeting.Code != 220) throw Failed("The server did not greet the connection", greeting);

        if (_useSsl)
        {
            var auth = await SendAsync("AUTH TLS", ct);
            if (!auth.IsPositive) throw Failed("The server refused AUTH TLS", auth);
            _stream = await UpgradeAsync(_control.GetStream(), ct);
            Rebind();
        }

        var userReply = await SendAsync($"USER {user}", ct);
        if (userReply.Code == 331)
        {
            var passReply = await SendAsync($"PASS {password}", ct);
            if (!passReply.IsPositive) throw Failed("Login was rejected", passReply);
        }
        else if (!userReply.IsPositive)
        {
            throw Failed("Login was rejected", userReply);
        }

        if (_useSsl)
        {
            // Protect the data channel too; without PROT P the server hands back cleartext.
            await SendAsync("PBSZ 0", ct);
            var prot = await SendAsync("PROT P", ct);
            if (!prot.IsPositive) throw Failed("The server refused PROT P", prot);
        }

        var type = await SendAsync("TYPE I", ct);
        if (!type.IsPositive) throw Failed("The server refused binary mode", type);
    }

    /// <summary>Changes directory, e.g. to <c>/Public</c>.</summary>
    public async Task ChangeDirectoryAsync(string directory, CancellationToken ct = default)
    {
        var reply = await SendAsync($"CWD {directory}", ct);
        if (!reply.IsPositive) throw Failed($"Cannot open directory '{directory}'", reply);
    }

    /// <summary>Bare names in the current directory (<c>NLST</c>).</summary>
    public async Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct = default)
    {
        var bytes = await TransferAsync("NLST", ct);
        return ControlEncoding.GetString(bytes)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            // A server may answer NLST with full paths; only the leaf is ever useful here.
            .Select(line => line[(line.LastIndexOf('/') + 1)..])
            .Where(name => name.Length > 0)
            .ToList();
    }

    /// <summary>Downloads one file whole. The largest CETIP publishes is ~50 MB.</summary>
    public Task<byte[]> DownloadAsync(string fileName, CancellationToken ct = default) =>
        TransferAsync($"RETR {fileName}", ct);

    /// <summary>Size in bytes, or null when the server does not answer <c>SIZE</c> for this file.</summary>
    public async Task<long?> SizeAsync(string fileName, CancellationToken ct = default)
    {
        var reply = await SendAsync($"SIZE {fileName}", ct);
        return reply.Code == 213 && long.TryParse(reply.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var size)
            ? size
            : null;
    }

    // ----- transfer plumbing -----------------------------------------------------------

    /// <summary>
    /// Opens a passive data connection, issues <paramref name="command"/> on the control channel
    /// and reads the data connection to the end. The order matters: the data socket has to be
    /// connected before the command, or the server has nowhere to write.
    /// </summary>
    private async Task<byte[]> TransferAsync(string command, CancellationToken ct)
    {
        var endpoint = await EnterPassiveModeAsync(ct);

        using var data = new TcpClient { ReceiveTimeout = (int)_timeout.TotalMilliseconds };
        await data.ConnectAsync(endpoint.Address, endpoint.Port, ct);

        var opened = await SendAsync(command, ct);
        // 125/150 mean "transfer starting"; anything else is a refusal, and the data socket
        // will simply close empty, which would otherwise read as an empty file.
        if (opened.Code is not (125 or 150)) throw Failed($"'{command}' was refused", opened);

        using var buffer = new MemoryStream();
        var dataStream = _useSsl ? await UpgradeAsync(data.GetStream(), ct) : data.GetStream();
        try
        {
            await dataStream.CopyToAsync(buffer, ct);
        }
        finally
        {
            // Closing the data connection before reading the completion reply is what tells
            // the server the transfer is over; it will not answer 226 until we do.
            await dataStream.DisposeAsync();
            data.Close();
        }

        var done = await ReadReplyAsync(ct);
        if (!done.IsPositive) throw Failed($"'{command}' did not complete", done);

        return buffer.ToArray();
    }

    private async Task<IPEndPoint> EnterPassiveModeAsync(CancellationToken ct)
    {
        // EPSV first: it is the only form that works unchanged over IPv6, and a server that
        // does not know it answers 500 rather than dropping the session.
        var epsv = await SendAsync("EPSV", ct);
        if (epsv.Code == 229 && TryParseEpsv(epsv.Text, out var epsvPort))
            return new IPEndPoint(ResolveControlAddress(), epsvPort);

        var pasv = await SendAsync("PASV", ct);
        if (pasv.Code == 227 && TryParsePasv(pasv.Text, out var address, out var port))
        {
            // The address a server advertises in PASV is often its private one, behind NAT.
            // The control connection already reached it, so prefer that peer.
            return new IPEndPoint(
                IPAddress.IsLoopback(address) || IsPrivate(address) ? ResolveControlAddress() : address, port);
        }

        throw Failed("The server would not enter passive mode", pasv);
    }

    private IPAddress ResolveControlAddress() =>
        (_control?.Client.RemoteEndPoint as IPEndPoint)?.Address
        ?? throw new FtpException("The control connection has no remote address.");

    private static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168);
    }

    /// <summary>
    /// 227 Entering Passive Mode (h1,h2,h3,h4,p1,p2). Servers punctuate this reply every way
    /// there is — parentheses, an equals sign, nothing at all — so the six numbers are matched
    /// as a group wherever they appear rather than found by splitting the line.
    /// </summary>
    public static bool TryParsePasv(string text, out IPAddress address, out int port)
    {
        address = IPAddress.None;
        port = 0;

        var match = PasvPattern().Match(text);
        if (!match.Success) return false;

        var numbers = new int[6];
        for (var i = 0; i < 6; i++)
        {
            var value = int.Parse(match.Groups[i + 1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture);
            if (value > 255) return false;
            numbers[i] = value;
        }

        address = new IPAddress(new[] { (byte)numbers[0], (byte)numbers[1], (byte)numbers[2], (byte)numbers[3] });
        port = (numbers[4] << 8) + numbers[5];
        return port > 0;
    }

    [GeneratedRegex(@"(\d{1,3}),\s*(\d{1,3}),\s*(\d{1,3}),\s*(\d{1,3}),\s*(\d{1,3}),\s*(\d{1,3})",
        RegexOptions.CultureInvariant)]
    private static partial Regex PasvPattern();

    /// <summary>229 Entering Extended Passive Mode (|||port|).</summary>
    public static bool TryParseEpsv(string text, out int port)
    {
        port = 0;
        var open = text.IndexOf('(');
        var close = text.IndexOf(')', open + 1);
        if (open < 0 || close <= open) return false;

        // (|||port|) splits to ["", "", "", "port", ""].
        var fields = text[(open + 1)..close].Split('|');
        return fields.Length >= 4
            && int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out port)
            && port > 0;
    }

    // ----- control channel -------------------------------------------------------------

    private async Task<FtpReply> SendAsync(string command, CancellationToken ct)
    {
        if (_writer is null) throw new FtpException("The client is not connected.");

        // Never log the argument of PASS.
        Transcript.Add(command.StartsWith("PASS ", StringComparison.Ordinal) ? "PASS ****" : command);

        await _writer.WriteAsync(command.AsMemory(), ct);
        await _writer.WriteAsync("\r\n".AsMemory(), ct);
        await _writer.FlushAsync(ct);
        return await ReadReplyAsync(ct);
    }

    /// <summary>
    /// Reads one reply, joining the continuation lines of a multi-line one. A multi-line reply
    /// opens with <c>NNN-</c> and ends with the same code followed by a space, which is the only
    /// way to tell its last line from a line of its body.
    /// </summary>
    private async Task<FtpReply> ReadReplyAsync(CancellationToken ct)
    {
        if (_reader is null) throw new FtpException("The client is not connected.");

        var first = await _reader.ReadLineAsync(ct)
                    ?? throw new FtpException("The server closed the connection.");

        var code = ParseCode(first);
        var text = new StringBuilder(first.Length > 4 ? first[4..] : string.Empty);

        if (first.Length > 3 && first[3] == '-')
        {
            var terminator = first[..3] + " ";
            while (true)
            {
                var line = await _reader.ReadLineAsync(ct)
                           ?? throw new FtpException("The server closed the connection mid-reply.");
                if (line.StartsWith(terminator, StringComparison.Ordinal))
                {
                    text.Append(' ').Append(line[4..]);
                    break;
                }
                text.Append(' ').Append(line);
            }
        }

        var reply = new FtpReply(code, text.ToString());
        Transcript.Add(reply.ToString());
        return reply;
    }

    private static int ParseCode(string line) =>
        line.Length >= 3 && int.TryParse(line.AsSpan(0, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var code)
            ? code
            : throw new FtpException($"Malformed reply from the server: '{line}'.");

    private async Task<SslStream> UpgradeAsync(Stream inner, CancellationToken ct)
    {
        var ssl = new SslStream(inner, leaveInnerStreamOpen: false);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = _host,
            EnabledSslProtocols = SslProtocols.None
        }, ct);
        return ssl;
    }

    private void Rebind()
    {
        _reader = new StreamReader(_stream!, ControlEncoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        _writer = new StreamWriter(_stream!, ControlEncoding, leaveOpen: true) { AutoFlush = false, NewLine = "\r\n" };
    }

    private FtpException Failed(string what, FtpReply reply) =>
        new($"{what}: {reply}") { Reply = reply };

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_writer is not null && _control?.Connected == true)
            {
                await _writer.WriteAsync("QUIT\r\n".AsMemory());
                await _writer.FlushAsync();
            }
        }
        catch
        {
            // A polite goodbye that fails changes nothing; the socket is going away regardless.
        }

        _reader?.Dispose();
        _writer?.Dispose();
        if (_stream is not null) await _stream.DisposeAsync();
        _control?.Dispose();
    }
}
