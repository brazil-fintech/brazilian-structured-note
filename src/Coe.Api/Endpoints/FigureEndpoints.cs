using System.Globalization;
using Coe.Core.Figures;
using Coe.Core.Templates;
using Coe.Infrastructure;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Net.Http.Headers;

namespace Coe.Api.Endpoints;

public static class TemplateCachePolicy
{
    public const string Name = "templates";
}

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
        //
        // A template version is immutable once published, so a request for an explicit version
        // is answered with a long-lived cache directive and a strong ETag: the browser fetches
        // each one once and revalidates for free afterwards. The active-version request cannot
        // be cached the same way — the point of it is to pick up a newly published template —
        // so it gets a short window instead.
        group.MapGet("/{code}/template", async (
            string code, int? version, ITemplateStore store, HttpContext http, CancellationToken ct) =>
        {
            var template = version is { } v
                ? await store.GetAsync(code, v, ct)
                : await store.GetActiveAsync(code, ct);

            if (template is null)
                return Results.NotFound(new { message = $"No template for figure {code}." });

            var etag = $"\"{code}-v{template.Version}-{template.SourceHash?[^12..] ?? "0"}\"";
            if (http.Request.Headers.IfNoneMatch.Count > 0 &&
                http.Request.Headers.IfNoneMatch.ToString().Contains(etag, StringComparison.Ordinal))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            var headers = http.Response.GetTypedHeaders();
            headers.ETag = new EntityTagHeaderValue(etag);
            headers.CacheControl = version is null
                ? new CacheControlHeaderValue { Private = true, MaxAge = TimeSpan.FromSeconds(30), MustRevalidate = true }
                : new CacheControlHeaderValue { Public = true, MaxAge = TimeSpan.FromDays(365) };

            return Results.Text(TemplateJson.Serialize(template), "application/json");
        })
        .CacheOutput(TemplateCachePolicy.Name)
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
        app.MapGet("/api/reference/{source}", async (
            string source, string? assetClass, IReferenceDataRepository reference, HttpContext http, CancellationToken ct) =>
        {
            if (!string.Equals(source, "underlyings", StringComparison.OrdinalIgnoreCase))
                return Results.NotFound(new { message = $"Unknown reference source '{source}'." });

            var items = await reference.UnderlyingsAsync(assetClass, ct);

            http.Response.GetTypedHeaders().CacheControl =
                new CacheControlHeaderValue { Public = true, MaxAge = TimeSpan.FromMinutes(10) };

            return Results.Ok(items.Select(u => new ReferenceItem(u.Code, u.Name, u.AssetClass)));
        })
        .CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(10)).SetVaryByQuery("assetClass"))
        .WithTags("Reference")
        .WithName("GetReferenceList");

        return app;
    }
}
