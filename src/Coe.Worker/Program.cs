using Coe.Infrastructure;
using Coe.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddCoePlatform(builder.Configuration);
builder.Services.AddHostedService<FigureIngestionWorker>();

var host = builder.Build();

// The worker owns the schema: it is the first process to touch a fresh database, and the
// scripts are idempotent, so running them here costs nothing when the API already did.
if (builder.Configuration.GetValue("Database:ApplyScriptsOnStartup", true))
{
    await using var scope = host.Services.CreateAsyncScope();
    var bootstrapper = new DatabaseBootstrapper(
        builder.Configuration.GetConnectionString("Coe")!,
        ServiceRegistration.ResolvePath(builder.Configuration["Database:ScriptDirectory"] ?? "db"),
        scope.ServiceProvider.GetRequiredService<ILogger<DatabaseBootstrapper>>());
    await bootstrapper.ApplyAsync();
}

await host.RunAsync();
