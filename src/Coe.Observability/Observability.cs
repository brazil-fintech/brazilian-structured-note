using System.Data;
using System.Diagnostics;
using Coe.Core.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Coe.Observability;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>Reported as <c>service.name</c> on every span, metric and log line.</summary>
    public string ServiceName { get; set; } = "coe";

    public string? ServiceVersion { get; set; }

    /// <summary>OTLP endpoint, e.g. <c>http://localhost:4317</c>. Unset disables the exporter.</summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>Writes spans and metrics to stdout. For local work only — it is noisy.</summary>
    public bool ConsoleExporter { get; set; }

    /// <summary>
    /// Attaches SQL text to database spans. Statements here are parameterised, so the text
    /// carries no booked values — the parameters are never attached — but it stays off by default.
    /// </summary>
    public bool RecordSqlStatements { get; set; }

    /// <summary>Head sampling ratio. 1.0 keeps every trace; lower it once traffic justifies it.</summary>
    public double TraceSampleRatio { get; set; } = 1.0;
}

public static class Observability
{
    /// <summary>
    /// A logger that exists before configuration is read, so a failure while building the host
    /// is still reported as a log line rather than an unhandled exception on stderr.
    /// </summary>
    public static void UseBootstrapLogger()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateBootstrapLogger();
    }

    /// <summary>
    /// Structured logging through Serilog. Sinks and levels come from the <c>Serilog</c>
    /// configuration section; the enrichers are fixed here because they are what makes a log
    /// line joinable to a trace.
    /// </summary>
    public static IServiceCollection AddCoeLogging(
        this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        services.AddSerilog((provider, logger) => logger
            .ReadFrom.Configuration(configuration)
            .ReadFrom.Services(provider)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentName()
            .Enrich.With<TraceContextEnricher>()
            .Enrich.WithProperty("service.name", serviceName));

        return services;
    }

    /// <summary>
    /// Traces and metrics. Returns the builder so a host can add its own instrumentation —
    /// the API adds ASP.NET Core, which the worker has no use for.
    /// </summary>
    public static OpenTelemetryBuilder AddCoeTelemetry(
        this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        var options = configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()
                      ?? new ObservabilityOptions();
        options.ServiceName = serviceName;
        options.ServiceVersion ??= CoeDiagnostics.Version;
        services.AddSingleton(options);

        return services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: options.ServiceName,
                serviceVersion: options.ServiceVersion,
                serviceInstanceId: Environment.MachineName))
            .WithTracing(tracing =>
            {
                // The domain sources: a span per validation pass, ingestion pass and save.
                tracing
                    .AddSource(CoeDiagnostics.ValidationSourceName)
                    .AddSource(CoeDiagnostics.IngestionSourceName)
                    .AddSource(CoeDiagnostics.BookingSourceName)
                    .SetSampler(options.TraceSampleRatio >= 1.0
                        ? new AlwaysOnSampler()
                        : new TraceIdRatioBasedSampler(options.TraceSampleRatio))
                    .AddSqlClientInstrumentation(sql =>
                    {
                        sql.RecordException = true;
                        // The instrumentation no longer exposes a statement-capture switch, so
                        // the text is attached here instead: explicit, and not tied to an option
                        // name that has already moved once.
                        // The hook is weakly typed so the instrumentation need not reference
                        // SqlClient; IDbCommand is enough for both of these.
                        sql.EnrichWithSqlCommand = (activity, command) =>
                        {
                            if (command is not IDbCommand dbCommand) return;
                            if (options.RecordSqlStatements) activity.SetTag("db.statement", dbCommand.CommandText);
                            activity.SetTag("db.parameter.count", dbCommand.Parameters.Count);
                        };
                    });

                ApplyExporters(options, tracing);
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(CoeDiagnostics.MeterName)
                    .AddRuntimeInstrumentation()
                    // Explicit buckets: the defaults are tuned for seconds, and a validation
                    // pass that takes 200ms is already a bad experience on a per-keystroke call.
                    .AddView("coe.validation.duration", new ExplicitBucketHistogramConfiguration
                    {
                        Boundaries = [0.5, 1, 2, 5, 10, 25, 50, 100, 250, 500, 1000]
                    })
                    .AddView("coe.sql.command.duration", new ExplicitBucketHistogramConfiguration
                    {
                        Boundaries = [1, 2, 5, 10, 25, 50, 100, 250, 500, 1000, 2500]
                    });

                ApplyExporters(options, metrics);
            });
    }

    private static void ApplyExporters(ObservabilityOptions options, TracerProviderBuilder tracing)
    {
        if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
            tracing.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(options.OtlpEndpoint));
        if (options.ConsoleExporter)
            tracing.AddConsoleExporter();
    }

    private static void ApplyExporters(ObservabilityOptions options, MeterProviderBuilder metrics)
    {
        if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
            metrics.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(options.OtlpEndpoint));
        if (options.ConsoleExporter)
            metrics.AddConsoleExporter();
    }
}

/// <summary>
/// Stamps every log line with the trace and span it happened inside.
///
/// This is the join between the three signals: a slow request shows up as a span, the span id
/// appears on its log lines, and the metrics say whether it is one request or a trend. Without
/// it the logs and the traces are two unrelated piles.
/// </summary>
public sealed class TraceContextEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
    {
        var activity = Activity.Current;
        if (activity is null) return;

        logEvent.AddPropertyIfAbsent(factory.CreateProperty("TraceId", activity.TraceId.ToString()));
        logEvent.AddPropertyIfAbsent(factory.CreateProperty("SpanId", activity.SpanId.ToString()));
    }
}
