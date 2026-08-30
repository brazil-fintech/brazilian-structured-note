using Coe.Core.Figures;
using Coe.Core.Validation;
using Coe.Infrastructure.Data;
using Coe.Infrastructure.ServerChecks;
using Coe.Ingestion;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Coe.Infrastructure;

/// <summary>
/// Wiring shared by the API and the worker: the database, the figure catalog, the template
/// cache and the validation engine with its server-side checks.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddCoePlatform(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Coe")
            ?? throw new InvalidOperationException("ConnectionStrings:Coe is not configured.");

        var databaseOptions = configuration.GetSection(SqlConnectionOptions.SectionName).Get<SqlConnectionOptions>()
                              ?? new SqlConnectionOptions();
        databaseOptions.ScriptDirectory = ResolvePath(databaseOptions.ScriptDirectory);
        services.AddSingleton(databaseOptions);

        services.AddSingleton<ISqlConnectionFactory>(sp => new SqlConnectionFactory(
            connectionString, databaseOptions, sp.GetRequiredService<ILogger<SqlConnectionFactory>>()));

        // Stateless over a pooled connection, so a singleton avoids per-request allocation.
        services.AddSingleton<IFigureCatalog, FigureCatalog>();
        services.AddSingleton<IAssetRepository, AssetRepository>();
        services.AddSingleton<IReferenceDataRepository, ReferenceDataRepository>();
        services.AddSingleton<IBusinessCalendar, BusinessCalendar>();
        services.AddSingleton<ITemplateStore, TemplateStore>();
        services.AddSingleton<AssetBookingService>();

        services.AddSingleton<IServerCheck, BusinessDayCheck>();
        services.AddSingleton<IServerCheck, BusinessDaysBeforeCheck>();
        services.AddSingleton<IServerCheck, ObservationCountCheck>();
        services.AddSingleton<IServerCheck, UniqueInstrumentCodeCheck>();
        services.AddSingleton<IServerCheckRegistry>(sp => new ServerCheckRegistry(sp.GetServices<IServerCheck>()));
        services.AddSingleton(sp => new ValidationEngine(sp.GetRequiredService<IServerCheckRegistry>()));

        var ingestion = new IngestionOptions();
        configuration.GetSection(IngestionOptions.SectionName).Bind(ingestion);
        ingestion.DomainDirectory = ResolvePath(ingestion.DomainDirectory);
        services.AddSingleton(ingestion);
        services.AddSingleton<FigureIngestionService>();

        services.AddHealthChecks().AddCheck<SqlHealthCheck>("sql", tags: ["ready"]);

        return services;
    }

    /// <summary>
    /// Resolves a configured path against the application directory when it is relative, so the
    /// same appsettings works from a container, a published folder and <c>dotnet run</c>.
    /// </summary>
    public static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
}

/// <summary>Readiness: can the platform actually reach its database right now?</summary>
public sealed class SqlHealthCheck(ISqlConnectionFactory connections) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connections.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT 1", connection) { CommandTimeout = 5 };
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy($"Connected to {connections.DatabaseName}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Cannot reach {connections.DatabaseName}.", ex);
        }
    }
}
