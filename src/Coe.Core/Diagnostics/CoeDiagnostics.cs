using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Coe.Core.Diagnostics;

/// <summary>
/// The instrumentation surface of the platform: one <see cref="ActivitySource"/> per subsystem
/// and one <see cref="Meter"/> carrying the instruments.
///
/// These are plain BCL primitives on purpose. The engine and the data layer emit through them
/// with no dependency on OpenTelemetry; only the host (<c>Coe.Observability</c>) decides what
/// listens and where it is exported. That keeps <c>Coe.Core</c> free of vendor packages and
/// means a unit test never has to configure a pipeline to exercise instrumented code.
/// </summary>
public static class CoeDiagnostics
{
    public const string ValidationSourceName = "Coe.Validation";
    public const string IngestionSourceName = "Coe.Ingestion";
    public const string BookingSourceName = "Coe.Booking";
    public const string MeterName = "Coe";

    public static readonly string Version =
        typeof(CoeDiagnostics).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    public static readonly ActivitySource Validation = new(ValidationSourceName, Version);
    public static readonly ActivitySource Ingestion = new(IngestionSourceName, Version);
    public static readonly ActivitySource Booking = new(BookingSourceName, Version);

    private static readonly Meter Meter = new(MeterName, Version);

    /// <summary>Wall-clock cost of one validation pass — the call the booking screen makes per keystroke.</summary>
    public static readonly Histogram<double> ValidationDuration = Meter.CreateHistogram<double>(
        "coe.validation.duration", unit: "ms",
        description: "Duration of a validation pass, tagged by figure and scope.");

    /// <summary>Findings produced, split by severity, so a spike in errors is visible without log mining.</summary>
    public static readonly Counter<long> ValidationMessages = Meter.CreateCounter<long>(
        "coe.validation.messages", unit: "{message}",
        description: "Validation findings produced, tagged by severity and origin.");

    /// <summary>Rules actually evaluated — the number the field-scope narrowing is meant to keep small.</summary>
    public static readonly Histogram<int> ValidationRulesEvaluated = Meter.CreateHistogram<int>(
        "coe.validation.rules_evaluated", unit: "{rule}",
        description: "Rules evaluated in a pass, tagged by scope.");

    public static readonly Counter<long> AssetSaves = Meter.CreateCounter<long>(
        "coe.asset.saves", unit: "{save}",
        description: "Save attempts, tagged by outcome: saved, rejected or conflict.");

    /// <summary>Template cache hit rate: a miss costs a query plus a JSON parse on a per-keystroke path.</summary>
    public static readonly Counter<long> TemplateCacheLookups = Meter.CreateCounter<long>(
        "coe.template.cache_lookups", unit: "{lookup}",
        description: "Template cache lookups, tagged by result: hit or miss.");

    public static readonly Counter<long> IngestionRuns = Meter.CreateCounter<long>(
        "coe.ingestion.runs", unit: "{run}",
        description: "Ingestion passes, tagged by status.");

    public static readonly Counter<long> TemplatesPublished = Meter.CreateCounter<long>(
        "coe.ingestion.templates_published", unit: "{template}",
        description: "New template versions published by the ingestion worker.");

    public static readonly Histogram<double> IngestionDuration = Meter.CreateHistogram<double>(
        "coe.ingestion.duration", unit: "ms",
        description: "Duration of an ingestion pass.");

    /// <summary>Retries burned on transient SQL faults — a rising count means the database is unhealthy.</summary>
    public static readonly Counter<long> SqlRetries = Meter.CreateCounter<long>(
        "coe.sql.retries", unit: "{retry}",
        description: "Transient SQL faults retried, tagged by operation.");

    public static readonly Histogram<double> SqlCommandDuration = Meter.CreateHistogram<double>(
        "coe.sql.command.duration", unit: "ms",
        description: "Duration of a database command, tagged by operation.");
}
