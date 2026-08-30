using Coe.Core.Figures;
using Coe.Core.Validation;
using Coe.Infrastructure.ServerChecks;
using Coe.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        services.AddDbContext<CoeDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                sql.CommandTimeout(60);
            }));

        services.AddMemoryCache();

        services.AddScoped<IFigureCatalog, FigureCatalog>();
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<ITemplateStore, TemplateStore>();
        services.AddScoped<AssetBookingService>();
        services.AddSingleton<IBusinessCalendar, BusinessCalendar>();

        // One instance per request: the uniqueness check needs to know which asset is being edited.
        services.AddScoped<ICurrentAssetContext, CurrentAssetContext>();

        services.AddScoped<IServerCheck, BusinessDayCheck>();
        services.AddScoped<IServerCheck, BusinessDaysBeforeCheck>();
        services.AddScoped<IServerCheck, ObservationCountCheck>();
        services.AddScoped<IServerCheck, UniqueInstrumentCodeCheck>();
        services.AddScoped<IServerCheckRegistry>(sp => new ServerCheckRegistry(sp.GetServices<IServerCheck>()));
        services.AddScoped(sp => new ValidationEngine(sp.GetRequiredService<IServerCheckRegistry>()));

        var ingestion = new IngestionOptions();
        configuration.GetSection(IngestionOptions.SectionName).Bind(ingestion);
        ingestion.DomainDirectory = ResolvePath(ingestion.DomainDirectory);
        services.AddSingleton(ingestion);
        services.AddScoped<FigureIngestionService>();

        return services;
    }

    /// <summary>
    /// Resolves a configured path against the content root when it is relative, so the same
    /// appsettings works from a container, a published folder and <c>dotnet run</c>.
    /// </summary>
    public static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
}
