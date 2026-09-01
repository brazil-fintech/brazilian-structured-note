using Coe.Ingestion;
using Coe.Ingestion.Cetip;
using Microsoft.Extensions.Logging;

namespace Coe.Infrastructure;

public sealed record ReferenceRefreshReport(
    CetipSyncReport Sync,
    bool Reloaded,
    ReferenceImportReport Import);

/// <summary>
/// One refresh of the reference data, end to end: pull what CETIP has published, re-read the
/// directory if anything changed, and publish the result to the database.
///
/// The three steps are separable on purpose. A directory that cannot be reached still reloads
/// and imports whatever is on disk, so a network outage costs freshness and nothing else; and
/// the import runs on every pass whether or not a file changed, because the database may be
/// empty even when the files are current.
/// </summary>
public sealed class ReferenceDataRefresher(
    CetipReferenceSync sync,
    B3ReferenceProvider references,
    B3ReferenceImporter importer,
    ILogger<ReferenceDataRefresher> logger)
{
    /// <param name="force">
    /// Run the download even when the minimum interval has not elapsed. Set by the admin
    /// endpoint, so an operator who knows B3 has just republished need not wait for it.
    /// </param>
    public async Task<ReferenceRefreshReport> RefreshAsync(bool force = false, CancellationToken ct = default)
    {
        var due = force || sync.IsDue(DateTimeOffset.UtcNow);
        var syncReport = due
            ? await sync.SyncAsync(ct)
            : new CetipSyncReport(false, "not due", DateTimeOffset.UtcNow, TimeSpan.Zero, [],
                [$"The last sync was at {sync.LastSyncUtc:u}; skipped."]);

        var reloaded = syncReport.Downloaded > 0;
        if (reloaded)
        {
            logger.LogInformation("{Count} export(s) changed; re-reading the reference directory", syncReport.Downloaded);
            references.Reload();
        }

        var import = await importer.ImportAsync(ct);
        return new ReferenceRefreshReport(syncReport, reloaded, import);
    }
}
