namespace Coe.Ingestion.Cetip;

/// <summary>
/// Where the dated exports are read from. Two implementations: B3's FTP directory, and a local
/// folder holding the same files. The sync is written against this so a desk that mirrors the
/// directory once — and the test suite, which has no network — exercise the same code path as
/// a live pull.
/// </summary>
public interface ICetipFileSource : IAsyncDisposable
{
    /// <summary>Where the files came from, for the log line and the sync report.</summary>
    string Origin { get; }

    /// <summary>Every file name in the directory. Empty when the source cannot list.</summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct);

    /// <summary>The bytes of one file, exactly as published.</summary>
    Task<byte[]> DownloadAsync(string fileName, CancellationToken ct);
}

/// <summary>Reads B3's public FTP directory. The connection is opened on first use.</summary>
public sealed class FtpFileSource(CetipFtpOptions options) : ICetipFileSource
{
    private FtpClient? _client;

    public string Origin => $"ftp://{options.Host}{options.Directory}";

    private async Task<FtpClient> ClientAsync(CancellationToken ct)
    {
        if (_client is not null) return _client;

        var client = new FtpClient(options.Host, options.Port, options.UseSsl, TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            await client.ConnectAsync(options.User, options.Password, ct);
            if (!string.IsNullOrWhiteSpace(options.Directory))
                await client.ChangeDirectoryAsync(options.Directory, ct);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }

        return _client = client;
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken ct) =>
        await (await ClientAsync(ct)).ListNamesAsync(ct);

    public async Task<byte[]> DownloadAsync(string fileName, CancellationToken ct) =>
        await (await ClientAsync(ct)).DownloadAsync(fileName, ct);

    public async ValueTask DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
        _client = null;
    }
}

/// <summary>Reads a folder holding the same dated files, for a mirror or a test.</summary>
public sealed class DirectoryFileSource(string directory) : ICetipFileSource
{
    public string Origin => directory;

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(
            Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory).Select(Path.GetFileName).OfType<string>().ToList()
                : []);

    public Task<byte[]> DownloadAsync(string fileName, CancellationToken ct)
    {
        // The names come from ListAsync, but a caller could pass anything; keep the read inside
        // the folder rather than letting a crafted name walk out of it.
        var safe = Path.GetFileName(fileName);
        if (string.IsNullOrEmpty(safe) || !string.Equals(safe, fileName, StringComparison.Ordinal))
            throw new IOException($"'{fileName}' is not a file name.");

        return File.ReadAllBytesAsync(Path.Combine(directory, safe), ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
