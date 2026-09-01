using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Coe.Core.Text;
using Microsoft.Extensions.Logging;

namespace Coe.Ingestion.Cetip;

/// <summary>What happened to one export in a sync pass.</summary>
/// <param name="Export">Logical export name.</param>
/// <param name="RemoteFile">The dated file that was chosen, when one was found.</param>
/// <param name="AsOf">Its <c>AAAAMMDD</c> stamp.</param>
/// <param name="Bytes">Size written, after transcoding.</param>
/// <param name="Status">
/// <c>downloaded</c>, <c>current</c> (the copy on disk is already that date),
/// <c>missing</c> (the directory has no file for it), <c>stale</c> (the newest published is
/// older than the copy on disk) or <c>failed</c>.
/// </param>
public sealed record CetipSyncEntry(string Export, string? RemoteFile, string? AsOf, long Bytes, string Status, string? Detail = null);

public sealed record CetipSyncReport(
    bool Ran,
    string Origin,
    DateTimeOffset StartedUtc,
    TimeSpan Duration,
    IReadOnlyList<CetipSyncEntry> Entries,
    IReadOnlyList<string> Messages)
{
    public int Downloaded => Entries.Count(e => e.Status == "downloaded");
    public int Failed => Entries.Count(e => e.Status == "failed");
}

/// <summary>
/// Pulls B3's public exports straight from <c>ftp://ftp.cetip.com.br/Public</c> into
/// <c>reference/b3/</c>, so the platform's reference data is whatever B3 published this morning
/// rather than whatever someone last committed.
///
/// The directory carries one dated copy per day — <c>20260828_DTpFiguras.txt</c> — and never
/// deletes the old ones, so the sync lists the directory and takes the newest stamp for each
/// export. Files come out as CETIP writes them, Latin-1 with CRLF line endings; they are
/// transcoded to UTF-8 with LF on the way in, which is the only change made to them, so they
/// diff and grep cleanly and every downstream reader sees one encoding.
///
/// Nothing here is required for the platform to run. A directory that cannot be reached, an
/// export that has not been published yet, a listing that comes back short — each leaves the
/// copy already on disk in place and is reported, because stale reference data is a great deal
/// better than none.
/// </summary>
public sealed class CetipReferenceSync(
    CetipFtpOptions options,
    string targetDirectory,
    ILogger<CetipReferenceSync> logger)
{
    /// <summary>The dated form every file in the directory takes: <c>20260828_DTpFiguras.txt</c>.</summary>
    private static readonly Regex DatedName = new(@"^(?<date>\d{8})[_-](?<name>.+?)(?<ext>\.[A-Za-z0-9]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string ManifestPath => Path.Combine(targetDirectory, CetipManifest.FileName);

    /// <summary>When the last successful pass ran, from the manifest. Null when there has been none.</summary>
    public DateTimeOffset? LastSyncUtc => CetipManifest.Load(ManifestPath).LastSyncUtc;

    /// <summary>True when <see cref="CetipFtpOptions.MinimumInterval"/> has elapsed since the last pass.</summary>
    public bool IsDue(DateTimeOffset now) =>
        LastSyncUtc is not { } last || now - last >= options.MinimumInterval;

    public async Task<CetipSyncReport> SyncAsync(CancellationToken ct = default)
    {
        var started = DateTimeOffset.UtcNow;
        var entries = new List<CetipSyncEntry>();
        var messages = new List<string>();

        if (!options.Enabled)
        {
            return new CetipSyncReport(false, "disabled", started, TimeSpan.Zero, entries,
                ["Cetip:Enabled is false; the committed reference exports are used as they are."]);
        }

        var exports = SelectedExports(messages).ToList();
        if (exports.Count == 0)
        {
            return new CetipSyncReport(false, "none", started, TimeSpan.Zero, entries,
                [.. messages, "No export is selected; nothing to fetch."]);
        }

        Directory.CreateDirectory(targetDirectory);
        var manifest = CetipManifest.Load(ManifestPath);

        await using var source = CreateSource();

        IReadOnlyList<string> listing;
        try
        {
            listing = await source.ListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not list {Origin}; keeping the reference exports already on disk", source.Origin);
            return new CetipSyncReport(false, source.Origin, started, DateTimeOffset.UtcNow - started, entries,
                [.. messages, $"Could not list {source.Origin}: {ex.Message}"]);
        }

        var published = IndexByExport(listing);
        var wrote = false;

        foreach (var export in exports)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var entry = await SyncOneAsync(source, export, published, manifest, ct);
                entries.Add(entry);
                if (entry.Status == "downloaded") wrote = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not refresh the {Export} export", export.Name);
                entries.Add(new CetipSyncEntry(export.Name, null, null, 0, "failed", ex.Message));
                messages.Add($"{export.Name}: {ex.Message}");
            }
        }

        // A pass that reached the directory counts, even when every file was already current:
        // that is exactly the case the interval floor exists to stop repeating.
        manifest.LastSyncUtc = DateTimeOffset.UtcNow;
        manifest.Origin = source.Origin;
        manifest.Save(ManifestPath);

        var duration = DateTimeOffset.UtcNow - started;
        logger.LogInformation(
            "CETIP sync from {Origin} finished in {Duration}: {Downloaded} downloaded, {Current} already current, {Failed} failed",
            source.Origin, duration, entries.Count(e => e.Status == "downloaded"),
            entries.Count(e => e.Status == "current"), entries.Count(e => e.Status == "failed"));

        if (wrote)
            logger.LogInformation("Reference exports refreshed in {Directory}", targetDirectory);

        return new CetipSyncReport(true, source.Origin, started, duration, entries, messages);
    }

    // ----- one export -------------------------------------------------------------------

    private async Task<CetipSyncEntry> SyncOneAsync(
        ICetipFileSource source,
        CetipExport export,
        IReadOnlyDictionary<string, List<PublishedFile>> published,
        CetipManifest manifest,
        CancellationToken ct)
    {
        var newest = Newest(export, published) ?? await ProbeAsync(source, export, ct);
        var target = Path.Combine(targetDirectory, export.LocalFile);

        if (newest is null)
        {
            var detail = $"No dated file for '{export.RemoteNames[0]}' in the directory.";
            logger.LogWarning("{Export}: {Detail}", export.Name, detail);
            return new CetipSyncEntry(export.Name, null, null, 0, "missing", detail);
        }

        var known = manifest.Entries.GetValueOrDefault(export.Name);

        // Nothing to do when the copy on disk is already that day's file — the common case,
        // since the worker syncs far more often than B3 publishes.
        if (known is not null && known.AsOf == newest.Date && File.Exists(target))
            return new CetipSyncEntry(export.Name, newest.FileName, newest.Date, known.Bytes, "current");

        if (known is not null && !options.AllowOlder &&
            string.CompareOrdinal(newest.Date, known.AsOf) < 0 && File.Exists(target))
        {
            var detail = $"The newest published file is {newest.Date}, older than the {known.AsOf} copy on disk.";
            logger.LogWarning("{Export}: {Detail}", export.Name, detail);
            return new CetipSyncEntry(export.Name, newest.FileName, newest.Date, known.Bytes, "stale", detail);
        }

        var bytes = await source.DownloadAsync(newest.FileName, ct);
        if (bytes.Length == 0)
            throw new InvalidDataException($"{newest.FileName} came back empty.");

        var text = Transcode(bytes);
        await WriteAtomicAsync(target, text, ct);

        var written = new FileInfo(target).Length;
        manifest.Entries[export.Name] = new CetipManifestEntry
        {
            RemoteFile = newest.FileName,
            AsOf = newest.Date,
            Bytes = written,
            Sha256 = Sha256(text),
            FetchedUtc = DateTimeOffset.UtcNow
        };

        logger.LogInformation("{Export}: {RemoteFile} ({Bytes:N0} bytes) -> {Local}",
            export.Name, newest.FileName, written, export.LocalFile);

        return new CetipSyncEntry(export.Name, newest.FileName, newest.Date, written, "downloaded");
    }

    /// <summary>
    /// Falls back to asking for dated names one day at a time, for a source that will not list
    /// its directory. Walks back from today because the newest file is the one wanted and a
    /// holiday can put several days between publications.
    /// </summary>
    private async Task<PublishedFile?> ProbeAsync(ICetipFileSource source, CetipExport export, CancellationToken ct)
    {
        for (var back = 0; back <= options.MaxLookbackDays; back++)
        {
            var date = DateTime.UtcNow.Date.AddDays(-back).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            foreach (var remote in export.RemoteNames)
            {
                ct.ThrowIfCancellationRequested();
                var candidate = $"{date}_{remote}.txt";
                try
                {
                    var bytes = await source.DownloadAsync(candidate, ct);
                    if (bytes.Length > 0) return new PublishedFile(candidate, date);
                }
                catch (Exception ex) when (ex is FtpException or IOException)
                {
                    // Not published under that name on that day; try the next one.
                }
            }
        }
        return null;
    }

    // ----- listing ------------------------------------------------------------------------

    private sealed record PublishedFile(string FileName, string Date);

    /// <summary>Groups a directory listing by the export each dated file belongs to.</summary>
    private static Dictionary<string, List<PublishedFile>> IndexByExport(IReadOnlyList<string> listing)
    {
        var index = new Dictionary<string, List<PublishedFile>>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in listing)
        {
            var match = DatedName.Match(name);
            if (!match.Success) continue;

            var key = Normalize(match.Groups["name"].Value);
            if (!index.TryGetValue(key, out var files)) index[key] = files = [];
            files.Add(new PublishedFile(name, match.Groups["date"].Value));
        }
        return index;
    }

    private static PublishedFile? Newest(CetipExport export, IReadOnlyDictionary<string, List<PublishedFile>> published)
    {
        foreach (var remote in export.RemoteNames)
        {
            if (published.TryGetValue(Normalize(remote), out var files) && files.Count > 0)
                return files.OrderByDescending(f => f.Date, StringComparer.Ordinal).First();
        }
        return null;
    }

    /// <summary>
    /// The directory is not consistent about spaces, underscores and case —
    /// <c>Ativos_Subjacentes</c> against <c>Ativos Subjacentes</c>, <c>DominiosCOE</c> against
    /// <c>Dominios_COE</c> — so names are matched on their letters and digits alone.
    /// </summary>
    private static string Normalize(string name)
    {
        var buffer = new StringBuilder(name.Length);
        foreach (var c in name)
            if (char.IsLetterOrDigit(c)) buffer.Append(char.ToUpperInvariant(c));
        return buffer.ToString();
    }

    // ----- writing --------------------------------------------------------------------------

    /// <summary>
    /// This is the one change made to a downloaded file: re-encode it from the single-byte
    /// encoding CETIP publishes to UTF-8, and normalise the CRLF line endings to LF. No row is
    /// reordered, renamed or dropped, so the file on disk is still B3's export.
    /// </summary>
    internal static string Transcode(byte[] bytes)
    {
        var text = Windows1252.Decode(bytes);
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    /// <summary>
    /// Writes through a temporary file. The compiler and the importer read this directory on
    /// their own schedule, and half a 50 MB export is a worse input than yesterday's whole one.
    /// </summary>
    private static async Task WriteAtomicAsync(string path, string content, CancellationToken ct)
    {
        var temporary = path + ".downloading";
        await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
        File.Move(temporary, path, overwrite: true);
    }

    private static string Sha256(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private IEnumerable<CetipExport> SelectedExports(List<string> messages)
    {
        if (options.Exports.Count == 0) return CetipPublicFiles.Default;

        var selected = new List<CetipExport>();
        foreach (var name in options.Exports)
        {
            var export = CetipPublicFiles.ByName(name);
            if (export is null) messages.Add($"Cetip:Exports names '{name}', which is not a known export.");
            else selected.Add(export);
        }
        return selected;
    }

    private ICetipFileSource CreateSource() =>
        string.IsNullOrWhiteSpace(options.LocalMirrorDirectory)
            ? new FtpFileSource(options)
            : new DirectoryFileSource(options.LocalMirrorDirectory);
}

/// <summary>What the last sync fetched, per export. Written beside the exports themselves.</summary>
public sealed class CetipManifest
{
    public const string FileName = "cetip-manifest.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public DateTimeOffset? LastSyncUtc { get; set; }
    public string? Origin { get; set; }
    public Dictionary<string, CetipManifestEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static CetipManifest Load(string path)
    {
        if (!File.Exists(path)) return new CetipManifest();
        try
        {
            return JsonSerializer.Deserialize<CetipManifest>(File.ReadAllText(path), Json) ?? new CetipManifest();
        }
        catch (JsonException)
        {
            // A corrupt manifest costs one redundant download, not a failed start-up.
            return new CetipManifest();
        }
    }

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json) + Environment.NewLine);
}

public sealed class CetipManifestEntry
{
    public string RemoteFile { get; set; } = string.Empty;

    /// <summary>The <c>AAAAMMDD</c> stamp the file was published under.</summary>
    public string AsOf { get; set; } = string.Empty;

    public long Bytes { get; set; }
    public string? Sha256 { get; set; }
    public DateTimeOffset FetchedUtc { get; set; }
}
