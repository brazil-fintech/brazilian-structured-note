using Coe.Infrastructure;
using Coe.Infrastructure.Data;
using Coe.Observability;
using Coe.Worker;
using Serilog;

Observability.UseBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);
    const string serviceName = "coe-worker";

    builder.Services.AddCoeLogging(builder.Configuration, serviceName);
    builder.Services.AddCoeTelemetry(builder.Configuration, serviceName);

    builder.Services.AddCoePlatform(builder.Configuration);
    builder.Services.AddHostedService<FigureIngestionWorker>();

    var host = builder.Build();

    // The worker owns the schema: it is the first process to touch a fresh database, and the
    // scripts are idempotent, so running them here costs nothing when the API already did.
    var databaseOptions = host.Services.GetRequiredService<SqlConnectionOptions>();
    if (databaseOptions.ApplyScriptsOnStartup)
    {
        var bootstrapper = new DatabaseBootstrapper(
            host.Services.GetRequiredService<ISqlConnectionFactory>(),
            databaseOptions,
            host.Services.GetRequiredService<ILogger<DatabaseBootstrapper>>());
        await bootstrapper.ApplyAsync();
    }

    await host.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "coe-worker terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
