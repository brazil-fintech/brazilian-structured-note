using System.Diagnostics;
using Coe.Core.Diagnostics;
using Coe.Core.Figures;
using Coe.Core.Templates;
using Microsoft.Extensions.Logging;

namespace Coe.Ingestion;

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>Directory holding <c>figures/</c> and <c>common/</c>.</summary>
    public string DomainDirectory { get; set; } = "domain";

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Re-scan as soon as a domain file changes, instead of waiting for the interval.</summary>
    public bool WatchFileSystem { get; set; } = true;

    /// <summary>
    /// Release a newly discovered figure for booking as soon as its template compiles.
    /// Turn off where a desk wants to review a B3 figure before it appears in the picker.
    /// </summary>
    public bool AutoEnableNewFigures { get; set; } = true;

    /// <summary>
    /// Directory holding B3's published exports (figure catalogue, domains, strategy fields,
    /// underlying master). Domain files are checked against them at compile time.
    /// </summary>
    public string ReferenceDirectory { get; set; } = "reference/b3";

    /// <summary>Compile and report, but write nothing. Used by the CLI check in CI.</summary>
    public bool DryRun { get; set; }
}

public sealed record IngestionReport(
    int FilesScanned,
    int FiguresCreated,
    int TemplatesCreated,
    int Quarantined,
    IReadOnlyList<string> Messages);

/// <summary>
/// One pass over the domain files: read, compile, and store a new template version for every
/// figure whose content changed. This is the "enable the new figure" step — a figure that B3
/// publishes and someone drops into <c>domain/figures/</c> becomes bookable without a deploy.
/// </summary>
public sealed class FigureIngestionService(
    IFigureCatalog catalog,
    IngestionOptions options,
    B3ReferenceProvider references,
    ILogger<FigureIngestionService> logger)
{
    public async Task<IngestionReport> RunAsync(CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = CoeDiagnostics.Ingestion.StartActivity("coe.ingest", ActivityKind.Internal);

        var run = new IngestionRun { StartedUtc = DateTimeOffset.UtcNow };
        var messages = new List<string>();

        // Taken once, for the whole pass: the exports can be replaced by a CETIP sync while
        // this runs, and every figure in one pass must be checked against the same catalogue.
        var reference = references.Current;
        var compiler = new TemplateCompiler(reference);

        // A missing or partial reference export is a warning, not a failure: the platform still
        // compiles and serves, it simply cannot cross-check against B3's catalogue.
        foreach (var error in reference.Errors)
            logger.LogWarning("B3 reference: {Message}", error);

        var loader = new DomainFileLoader(options.DomainDirectory);
        var set = loader.Load();
        messages.AddRange(set.Errors);
        run.FilesScanned = set.Figures.Count;

        foreach (var loaded in set.Figures)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var fileActivity = CoeDiagnostics.Ingestion.StartActivity("coe.ingest.figure", ActivityKind.Internal);
                fileActivity?.SetTag("coe.figure.code", loaded.File.FigureCode);
                fileActivity?.SetTag("coe.source.file", loaded.RelativePath);
                await IngestOneAsync(loaded, set.Fragments, compiler, run, messages, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Ingestion of {File} failed", loaded.RelativePath);
                messages.Add($"{loaded.RelativePath}: {ex.Message}");
                run.FiguresQuarantined++;
            }
        }

        run.CompletedUtc = DateTimeOffset.UtcNow;
        run.Status = set.Errors.Count == 0 && run.FiguresQuarantined == 0 ? "Succeeded" : "CompletedWithErrors";
        run.Details = messages.Count == 0 ? null : string.Join(Environment.NewLine, messages);

        if (!options.DryRun) await catalog.RecordRunAsync(run, ct);

        CoeDiagnostics.IngestionDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        CoeDiagnostics.IngestionRuns.Add(1, new KeyValuePair<string, object?>("coe.ingestion.status", run.Status));
        CoeDiagnostics.TemplatesPublished.Add(run.TemplatesCreated);
        activity?.SetTag("coe.ingestion.files_scanned", run.FilesScanned);
        activity?.SetTag("coe.ingestion.templates_created", run.TemplatesCreated);
        activity?.SetTag("coe.ingestion.quarantined", run.FiguresQuarantined);
        if (run.FiguresQuarantined > 0) activity?.SetStatus(ActivityStatusCode.Error, "figures quarantined");

        logger.LogInformation(
            "Ingestion finished: {Scanned} file(s), {Created} new figure(s), {Templates} new template version(s), {Quarantined} quarantined",
            run.FilesScanned, run.FiguresCreated, run.TemplatesCreated, run.FiguresQuarantined);

        return new IngestionReport(run.FilesScanned, run.FiguresCreated, run.TemplatesCreated, run.FiguresQuarantined, messages);
    }

    private async Task IngestOneAsync(
        LoadedDomainFile loaded,
        IReadOnlyDictionary<string, DomainFile> fragments,
        TemplateCompiler compiler,
        IngestionRun run,
        List<string> messages,
        CancellationToken ct)
    {
        var file = loaded.File;
        var code = file.FigureCode;
        if (string.IsNullOrWhiteSpace(code))
        {
            messages.Add($"{loaded.RelativePath}: figureCode is required.");
            run.FiguresQuarantined++;
            return;
        }

        var existing = await catalog.GetAsync(code, ct);
        var isNew = existing is null;

        // Unchanged content is the common case; skip it before doing any work.
        if (existing is not null &&
            string.Equals(existing.SourceHash, loaded.Hash, StringComparison.Ordinal) &&
            existing.Status != FigureStatus.Quarantined)
        {
            return;
        }

        var nextVersion = await catalog.LatestTemplateVersionAsync(code, ct) + 1;
        var result = compiler.Compile(file, fragments, nextVersion);

        foreach (var warning in result.Warnings)
            messages.Add($"{loaded.RelativePath}: warning — {warning}");

        if (!result.Succeeded)
        {
            var detail = string.Join("; ", result.Errors);
            messages.Add($"{loaded.RelativePath}: {detail}");
            run.FiguresQuarantined++;

            if (!options.DryRun)
            {
                var quarantined = existing ?? NewFigure(code, file, loaded);
                quarantined.Status = FigureStatus.Quarantined;
                quarantined.LastError = detail;
                quarantined.SourceHash = loaded.Hash;
                quarantined.SourceFile = loaded.RelativePath;
                quarantined.UpdatedUtc = DateTimeOffset.UtcNow;
                await catalog.UpsertAsync(quarantined, ct);
            }
            return;
        }

        var template = result.Template!;
        if (options.DryRun)
        {
            if (isNew) run.FiguresCreated++;
            run.TemplatesCreated++;
            return;
        }

        var figure = existing ?? NewFigure(code, file, loaded);
        figure.Name = file.FigureName!;
        figure.CommercialName = file.CommercialName;
        figure.DescriptionPt = file.Description?.Pt;
        figure.DescriptionEn = file.Description?.En;
        figure.Modalities = string.Join(',', file.Modalities);
        figure.SourceFile = loaded.RelativePath;
        figure.SourceHash = loaded.Hash;
        figure.LastError = null;
        figure.ActiveTemplateVersion = nextVersion;
        figure.UpdatedUtc = DateTimeOffset.UtcNow;

        // A figure already released stays released; a new or previously quarantined one is
        // released only when the platform is configured to do so on its own.
        if (figure.Status is FigureStatus.Pending or FigureStatus.Quarantined)
        {
            figure.Status = options.AutoEnableNewFigures ? FigureStatus.Enabled : FigureStatus.Pending;
            if (figure.Status == FigureStatus.Enabled) figure.EnabledUtc ??= DateTimeOffset.UtcNow;
        }

        await catalog.UpsertAsync(figure, ct);
        await catalog.AddTemplateVersionAsync(new FigureTemplateRecord
        {
            FigureCode = code,
            Version = nextVersion,
            SchemaVersion = template.SchemaVersion,
            TemplateJson = TemplateJson.Serialize(template),
            SourceHash = loaded.Hash,
            SourceFile = loaded.RelativePath,
            IsActive = true,
            CreatedUtc = DateTimeOffset.UtcNow,
            CreatedBy = "ingestion-worker"
        }, ct);

        if (isNew) run.FiguresCreated++;
        run.TemplatesCreated++;

        logger.LogInformation("Figure {Code} compiled to template v{Version} ({Status})", code, nextVersion, figure.Status);
    }

    private static Figure NewFigure(string code, DomainFile file, LoadedDomainFile loaded) => new()
    {
        Code = code,
        Name = file.FigureName ?? code,
        CommercialName = file.CommercialName,
        Modalities = string.Join(',', file.Modalities),
        Status = FigureStatus.Pending,
        SourceFile = loaded.RelativePath,
        SourceHash = loaded.Hash,
        FirstSeenUtc = DateTimeOffset.UtcNow,
        UpdatedUtc = DateTimeOffset.UtcNow
    };
}
