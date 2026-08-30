using System.Text.Json.Serialization;
using Coe.Api.Endpoints;
using Coe.Infrastructure;
using Coe.Ingestion;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCoePlatform(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Same conventions as the compiled templates, so the client parses one set of shapes.
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});

const string DevCors = "coe-dev";
builder.Services.AddCors(options => options.AddPolicy(DevCors, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// The schema scripts are idempotent, so applying them at startup keeps every environment on
// the shape the repository describes without a separate deployment step.
if (app.Configuration.GetValue("Database:ApplyScriptsOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    var bootstrapper = new DatabaseBootstrapper(
        app.Configuration.GetConnectionString("Coe")!,
        ServiceRegistration.ResolvePath(app.Configuration["Database:ScriptDirectory"] ?? "db"),
        scope.ServiceProvider.GetRequiredService<ILogger<DatabaseBootstrapper>>());
    await bootstrapper.ApplyAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors(DevCors);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapFigureEndpoints();
app.MapReferenceEndpoints();
app.MapAssetEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithTags("Health");

// Lets an operator re-read the domain files without waiting for the worker's next pass —
// useful right after dropping in a figure B3 has just published.
app.MapPost("/api/admin/ingest", async (FigureIngestionService ingestion, CancellationToken ct) =>
    Results.Ok(await ingestion.RunAsync(ct)))
    .WithTags("Admin")
    .WithName("RunIngestion");

app.Run();

public partial class Program;
