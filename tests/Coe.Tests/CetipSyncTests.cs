using System.Text;
using Coe.Core.Text;
using Coe.Ingestion.Cetip;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Coe.Tests;

/// <summary>
/// The sync against a local mirror of CETIP's directory. Everything but the socket is exercised
/// here: picking the newest dated file per export, transcoding it, writing it once, and knowing
/// not to write it again.
/// </summary>
public sealed class CetipSyncTests : IDisposable
{
    private readonly string _mirror = NewDirectory();
    private readonly string _target = NewDirectory();

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "coe-cetip-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Writes a dated file the way CETIP publishes one: single-byte, CRLF.</summary>
    private void Publish(string name, string content) =>
        File.WriteAllBytes(Path.Combine(_mirror, name), Windows1252.Encode(content.ReplaceLineEndings("\r\n")));

    private CetipReferenceSync Sync(Action<CetipFtpOptions>? configure = null)
    {
        var options = new CetipFtpOptions { LocalMirrorDirectory = _mirror, MinimumInterval = TimeSpan.Zero };
        configure?.Invoke(options);
        return new CetipReferenceSync(options, _target, NullLogger<CetipReferenceSync>.Instance);
    }

    [Fact]
    public async Task Takes_the_newest_dated_file_for_each_export()
    {
        Publish("20260826_DTpFiguras.txt", "20260826\nCODIGO FIGURA;NOME FIGURA;CALCULADA\n01;COE001001 - Call;S\n");
        Publish("20260828_DTpFiguras.txt", "20260828\nCODIGO FIGURA;NOME FIGURA;CALCULADA\n01;COE001001 - Call;S\n02;COE001002 - Put;S\n");
        Publish("20260827_DTpFiguras.txt", "20260827\nCODIGO FIGURA;NOME FIGURA;CALCULADA\n01;COE001001 - Call;S\n");

        var report = await Sync().SyncAsync();

        var figures = report.Entries.Single(e => e.Export == "figuras");
        Assert.Equal("downloaded", figures.Status);
        Assert.Equal("20260828", figures.AsOf);
        Assert.Equal("20260828_DTpFiguras.txt", figures.RemoteFile);
        Assert.Contains("COE001002", File.ReadAllText(Path.Combine(_target, "figuras.csv")));
    }

    [Fact]
    public async Task Transcodes_to_utf8_with_unix_line_endings()
    {
        // Byte 0x96 is an en dash in the encoding CETIP writes, and B3 uses it in figure names.
        Publish("20260828_DTpFiguras.txt", "20260828\nheader\n85;COE de Crédito – CDS;N\n");

        await Sync().SyncAsync();

        var path = Path.Combine(_target, "figuras.csv");
        var bytes = File.ReadAllBytes(path);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.Contains("COE de Crédito – CDS", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
        Assert.NotEqual(0xEF, bytes[0]); // no byte-order mark
    }

    [Fact]
    public async Task Leaves_a_file_alone_when_the_copy_on_disk_is_already_that_day()
    {
        Publish("20260828_DTpFiguras.txt", "20260828\nheader\n01;COE001001 - Call;S\n");

        var sync = Sync();
        Assert.Equal("downloaded", (await sync.SyncAsync()).Entries.Single(e => e.Export == "figuras").Status);
        Assert.Equal("current", (await sync.SyncAsync()).Entries.Single(e => e.Export == "figuras").Status);
    }

    [Fact]
    public async Task Refuses_to_roll_an_export_backwards()
    {
        Publish("20260828_DTpFiguras.txt", "20260828\nheader\n01;COE001001 - Call;S\n");
        var sync = Sync();
        await sync.SyncAsync();

        // A listing that comes back short — a partial mirror, a directory mid-publish — must not
        // replace today's export with an older one.
        File.Delete(Path.Combine(_mirror, "20260828_DTpFiguras.txt"));
        Publish("20260820_DTpFiguras.txt", "20260820\nheader\nstale\n");

        var entry = (await sync.SyncAsync()).Entries.Single(e => e.Export == "figuras");
        Assert.Equal("stale", entry.Status);
        Assert.DoesNotContain("stale", File.ReadAllText(Path.Combine(_target, "figuras.csv")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_an_export_the_directory_does_not_carry()
    {
        Publish("20260828_DTpFiguras.txt", "20260828\nheader\n01;COE001001 - Call;S\n");

        var report = await Sync().SyncAsync();

        Assert.Equal("missing", report.Entries.Single(e => e.Export == "ativos-subjacentes").Status);
        // and the one that is there still lands.
        Assert.Equal("downloaded", report.Entries.Single(e => e.Export == "figuras").Status);
    }

    [Fact]
    public async Task Matches_a_name_however_CETIP_punctuates_it()
    {
        // The manual links Dominios_COE.txt for the file the directory carries as DominiosCOE.txt.
        Publish("20260828_Dominios_COE.txt", "Tipo do Dominio;Nome;Descricao;Codigo;\nTIPO CESTA;BEST OF;;1;\n");

        var entry = (await Sync().SyncAsync()).Entries.Single(e => e.Export == "dominios-coe");

        Assert.Equal("downloaded", entry.Status);
        Assert.True(File.Exists(Path.Combine(_target, "dominios-coe.csv")));
    }

    [Fact]
    public async Task Does_nothing_when_it_is_switched_off()
    {
        Publish("20260828_DTpFiguras.txt", "20260828\nheader\n01;COE001001 - Call;S\n");

        var report = await Sync(o => o.Enabled = false).SyncAsync();

        Assert.False(report.Ran);
        Assert.Empty(report.Entries);
        Assert.False(File.Exists(Path.Combine(_target, "figuras.csv")));
    }

    [Fact]
    public async Task Honours_the_interval_between_passes()
    {
        Publish("20260828_DTpFiguras.txt", "20260828\nheader\n01;COE001001 - Call;S\n");

        var sync = Sync(o => o.MinimumInterval = TimeSpan.FromHours(6));
        Assert.True(sync.IsDue(DateTimeOffset.UtcNow));

        await sync.SyncAsync();

        Assert.False(sync.IsDue(DateTimeOffset.UtcNow));
        Assert.True(sync.IsDue(DateTimeOffset.UtcNow.AddHours(7)));
    }

    public void Dispose()
    {
        foreach (var directory in new[] { _mirror, _target })
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

public sealed class FtpReplyParsingTests
{
    [Theory]
    [InlineData("Entering Passive Mode (200,146,29,10,195,80)", "200.146.29.10", 50000)]
    [InlineData("=200,146,29,10,4,1", "200.146.29.10", 1025)]
    public void Reads_a_passive_mode_reply(string text, string address, int port)
    {
        Assert.True(FtpClient.TryParsePasv(text, out var parsed, out var parsedPort));
        Assert.Equal(address, parsed.ToString());
        Assert.Equal(port, parsedPort);
    }

    [Fact]
    public void Rejects_a_passive_reply_it_cannot_read()
    {
        Assert.False(FtpClient.TryParsePasv("Entering Passive Mode (nonsense)", out _, out _));
    }

    [Fact]
    public void Reads_an_extended_passive_mode_reply()
    {
        Assert.True(FtpClient.TryParseEpsv("Entering Extended Passive Mode (|||50123|)", out var port));
        Assert.Equal(50123, port);
    }
}

public sealed class Windows1252Tests
{
    [Theory]
    [InlineData(0x96, '–')]  // en dash, the byte B3's figure names carry
    [InlineData(0xE9, 'é')]  // e-acute
    [InlineData(0xE7, 'ç')]  // c-cedilla
    [InlineData(0x41, 'A')]
    public void Decodes_a_byte_to_the_character_the_code_page_gives_it(int b, char expected) =>
        Assert.Equal(expected.ToString(), Windows1252.Decode([(byte)b]));

    [Fact]
    public void Round_trips_the_text_B3_publishes()
    {
        const string original = "COE de Crédito – CDS com Amortização";
        Assert.Equal(original, Windows1252.Decode(Windows1252.Encode(original)));
    }

    [Fact]
    public void Encodes_one_byte_per_character_so_positions_hold()
    {
        const string text = "Opção – Ações";
        Assert.Equal(text.Length, Windows1252.Encode(text).Length);
    }

    [Fact]
    public void Keeps_the_letter_when_it_cannot_keep_the_mark()
    {
        // U+0101 (a with macron) has no position in the code page; its base letter does.
        Assert.Equal("a", Windows1252.Decode(Windows1252.Encode("ā")));
    }
}
