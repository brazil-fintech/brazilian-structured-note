using System.Text.Json;
using System.Text.Json.Serialization;
using Coe.Api.Endpoints;
using Coe.Infrastructure;
using Coe.Infrastructure.Data;
using Coe.Ingestion;
using Coe.Ingestion.Cetip;
using Coe.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;

Observability.UseBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    const string serviceName = "coe-api";

    builder.Services.AddCoeLogging(builder.Configuration, serviceName);
    builder.Services.AddCoeTelemetry(builder.Configuration, serviceName)
        .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation(aspNet =>
        {
            aspNet.RecordException = true;
            // Health probes run on a timer and would otherwise dominate the traces.
            aspNet.Filter = context => !context.Request.Path.StartsWithSegments("/health");
        }))
        .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation());

    builder.Services.AddCoePlatform(builder.Configuration);
    builder.Services.AddOpenApi();
    builder.Services.AddProblemDetails();

    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        // Same conventions as the compiled templates, so the client parses one set of shapes.
        options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

    // Templates and reference lists are read far more often than they change, and compress well.
    builder.Services.AddResponseCompression(options => options.EnableForHttps = true);
    builder.Services.AddOutputCache(options =>
        options.AddPolicy(TemplateCachePolicy.Name, policy => policy
            .Expire(TimeSpan.FromMinutes(5))
            .SetVaryByQuery("version")));

    const string DevCors = "coe-dev";
    builder.Services.AddCors(options => options.AddPolicy(DevCors, policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
        .AllowAnyHeader()
        .AllowAnyMethod()));

    var app = builder.Build();

    // Logs one line per request with method, path, status and duration, instead of the several
    // ASP.NET Core emits by default.
    app.UseSerilogRequestLogging(options =>
        options.GetLevel = (http, _, ex) =>
            ex is not null || http.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
            : http.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose
            : Serilog.Events.LogEventLevel.Information);

    // The schema scripts are idempotent, so applying them at startup keeps every environment on
    // the shape the repository describes without a separate deployment step.
    var databaseOptions = app.Services.GetRequiredService<SqlConnectionOptions>();
    if (databaseOptions.ApplyScriptsOnStartup)
    {
        var bootstrapper = new DatabaseBootstrapper(
            app.Services.GetRequiredService<ISqlConnectionFactory>(),
            databaseOptions,
            app.Services.GetRequiredService<ILogger<DatabaseBootstrapper>>());
        await bootstrapper.ApplyAsync();
    }

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseResponseCompression();
    app.UseCors(DevCors);
    app.UseOutputCache();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.MapFigureEndpoints();
    app.MapReferenceEndpoints();
    app.MapAssetEndpoints();

    app.MapHealthChecks("/health/live").WithTags("Health");
    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    }).WithTags("Health");

    // Lets an operator re-read the domain files without waiting for the worker's next pass —
    // useful right after dropping in a figure B3 has just published.
    app.MapPost("/api/admin/ingest", async (FigureIngestionService ingestion, CancellationToken ct) =>
        Results.Ok(await ingestion.RunAsync(ct)))
        .WithTags("Admin")
        .WithName("RunIngestion");

    // Pulls CETIP's public directory now rather than on the worker's own schedule, re-reads
    // whatever changed and republishes it to the reference tables.
    app.MapPost("/api/admin/cetip/sync", async (
        ReferenceDataRefresher refresher, bool? force, CancellationToken ct) =>
        Results.Ok(await refresher.RefreshAsync(force ?? true, ct)))
        .WithTags("Admin")
        .WithName("SyncCetipReference")
        .WithSummary("Fetches the dated exports from ftp.cetip.com.br/Public and reloads the reference data.");

    // What the platform is currently checking registrations against, and where it came from.
    app.MapGet("/api/admin/cetip", (
        CetipFtpOptions options, CetipReferenceSync sync, B3ReferenceProvider references) =>
    {
        var manifest = CetipManifest.Load(sync.ManifestPath);
        var reference = references.Current;
        return Results.Ok(new
        {
            source = string.IsNullOrWhiteSpace(options.LocalMirrorDirectory)
                ? $"ftp://{options.Host}{options.Directory}"
                : options.LocalMirrorDirectory,
            options.Enabled,
            options.MinimumInterval,
            manifest.LastSyncUtc,
            nextSyncDue = sync.IsDue(DateTimeOffset.UtcNow),
            references.LoadedUtc,
            figures = reference.Figures.Count,
            underlyings = reference.Underlyings.Count,
            derivativeFields = reference.DerivativeFields.Fields.Count,
            mappedFigures = reference.DerivativeFields.FigureCodes.Count,
            exports = manifest.Entries
        });
    })
        .WithTags("Admin")
        .WithName("GetCetipStatus")
        .WithSummary("Provenance of the reference data: which dated export each file came from, and when.");

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "coe-api terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
