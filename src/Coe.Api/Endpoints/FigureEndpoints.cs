using Coe.Core.Figures;
using Coe.Core.Templates;
using Coe.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Coe.Api.Endpoints;

public static class FigureEndpoints
{
    public static IEndpointRouteBuilder MapFigureEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/figures").WithTags("Figures");

        group.MapGet("/", async (IFigureCatalog catalog, bool? includeDisabled, CancellationToken ct) =>
        {
            var figures = await catalog.ListAsync(enabledOnly: includeDisabled != true, ct);
            return Results.Ok(figures.Select(f => new FigureSummary(
                f.Code, f.Name, f.CommercialName, f.DescriptionPt,
                f.ModalityList().ToList(), f.Status.ToString(), f.ActiveTemplateVersion)));
        })
        .WithName("ListFigures")
        .WithSummary("Figures available for booking; pass includeDisabled=true to see pending and quarantined ones too.");

        // The whole dynamic form comes from here: sections, fields, conditions and rules.
        group.MapGet("/{code}/template", async (string code, int? version, ITemplateStore store, CancellationToken ct) =>
        {
            var template = version is { } v
                ? await store.GetAsync(code, v, ct)
                : await store.GetActiveAsync(code, ct);

            return template is null
                ? Results.NotFound(new { message = $"No template for figure {code}." })
                : Results.Text(TemplateJson.Serialize(template), "application/json");
        })
        .WithName("GetFigureTemplate")
        .WithSummary("The compiled template driving the dynamic form. Omit version for the active one.");

        group.MapGet("/{code}", async (string code, IFigureCatalog catalog, CancellationToken ct) =>
        {
            var figure = await catalog.GetAsync(code, ct);
            return figure is null
                ? Results.NotFound()
                : Results.Ok(new FigureSummary(
                    figure.Code, figure.Name, figure.CommercialName, figure.DescriptionPt,
                    figure.ModalityList().ToList(), figure.Status.ToString(), figure.ActiveTemplateVersion));
        })
        .WithName("GetFigure");

        return app;
    }

    public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        // Fields declare an optionSource instead of inlining long lists that change on their own cadence.
        app.MapGet("/api/reference/{source}", async (string source, string? assetClass, CoeDbContext db, CancellationToken ct) =>
        {
            if (!string.Equals(source, "underlyings", StringComparison.OrdinalIgnoreCase))
                return Results.NotFound(new { message = $"Unknown reference source '{source}'." });

            var query = db.Underlyings.AsNoTracking().Where(u => u.IsActive);
            if (!string.IsNullOrWhiteSpace(assetClass)) query = query.Where(u => u.AssetClass == assetClass);

            var items = await query
                .OrderBy(u => u.AssetClass).ThenBy(u => u.Code)
                .Select(u => new ReferenceItem(u.Code, u.Name, u.AssetClass))
                .ToListAsync(ct);

            return Results.Ok(items);
        })
        .WithTags("Reference")
        .WithName("GetReferenceList");

        return app;
    }
}
