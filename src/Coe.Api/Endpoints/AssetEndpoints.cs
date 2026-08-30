using System.Globalization;
using Coe.Core.Assets;
using Coe.Core.Validation;
using Coe.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Coe.Api.Endpoints;

public static class AssetEndpoints
{
    public static IEndpointRouteBuilder MapAssetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/assets").WithTags("Assets");

        // The landing screen: everything live on the reference date, i.e. issued on or before it
        // and not yet matured.
        group.MapGet("/", async (
            string? referenceDate,
            string? figureCode,
            string? modality,
            string? underlying,
            string? status,
            string? search,
            int? page,
            int? pageSize,
            IAssetRepository repository,
            CoeDbContext db,
            CancellationToken ct) =>
        {
            if (!TryParseDate(referenceDate, out var reference))
                return Results.BadRequest(new { message = "referenceDate must be an ISO date (yyyy-MM-dd)." });

            AssetStatus? parsedStatus = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<AssetStatus>(status, ignoreCase: true, out var s))
                    return Results.BadRequest(new { message = $"Unknown status '{status}'." });
                parsedStatus = s;
            }

            var result = await repository.SearchAsync(new AssetQuery
            {
                ReferenceDate = reference ?? DateOnly.FromDateTime(DateTime.UtcNow),
                FigureCode = figureCode,
                Modality = modality,
                Underlying = underlying,
                Status = parsedStatus,
                Search = search,
                Page = page ?? 1,
                PageSize = pageSize ?? 50
            }, ct);

            var codes = result.Items.Select(a => a.FigureCode).Distinct().ToList();
            var names = await db.Figures.AsNoTracking()
                .Where(f => codes.Contains(f.Code))
                .ToDictionaryAsync(f => f.Code, f => f.Name, ct);

            return Results.Ok(new AssetListResponse(
                result.Items.Select(a => ContractMapping.ToListItem(a, names.GetValueOrDefault(a.FigureCode))).ToList(),
                result.Total, result.Page, result.PageSize));
        })
        .WithName("ListAssets")
        .WithSummary("Assets live on the reference date (issueDate <= referenceDate <= maturityDate).");

        group.MapGet("/{id:guid}", async (Guid id, IAssetRepository repository, CancellationToken ct) =>
        {
            var asset = await repository.GetAsync(id, ct);
            return asset is null ? Results.NotFound() : Results.Ok(ContractMapping.ToDetail(asset));
        })
        .WithName("GetAsset");

        // Called as the user types. Scope "field" narrows the pass to the changed paths and
        // whatever reads them, so a keystroke does not light up the whole form.
        group.MapPost("/validate", async (ValidateRequest request, AssetBookingService booking, CancellationToken ct) =>
        {
            var scope = request.Scope.ToLowerInvariant() switch
            {
                "submit" => ValidationScope.Submit,
                "form" => ValidationScope.Form,
                _ => ValidationScope.Field
            };

            try
            {
                var result = await booking.ValidateAsync(
                    request.FigureCode, request.Values, scope, request.ChangedPaths,
                    request.AssetId, request.Culture, ct);

                return Results.Ok(new ValidateResponse(
                    result.Validation.Messages, result.Validation.EvaluatedPaths, result.Validation.IsValid));
            }
            catch (FigureNotAvailableException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("ValidateAsset")
        .WithSummary("Asynchronous validation while booking. Returns messages pinned to instance paths.");

        group.MapPost("/", async (SaveAssetRequest request, AssetBookingService booking, HttpContext http, CancellationToken ct) =>
            await SaveAsync(null, request, booking, http, ct))
            .WithName("CreateAsset");

        group.MapPut("/{id:guid}", async (Guid id, SaveAssetRequest request, AssetBookingService booking, HttpContext http, CancellationToken ct) =>
            await SaveAsync(id, request, booking, http, ct))
            .WithName("UpdateAsset");

        return app;
    }

    // Whatever the client checked, the save path always runs the full submit-scope validation.
    private static async Task<IResult> SaveAsync(
        Guid? id, SaveAssetRequest request, AssetBookingService booking, HttpContext http, CancellationToken ct)
    {
        try
        {
            var result = await booking.SaveAsync(new BookingRequest
            {
                Id = id,
                FigureCode = request.FigureCode,
                Values = request.Values,
                RowVersion = request.RowVersion,
                AcceptWarnings = request.AcceptWarnings,
                Culture = request.Culture,
                User = http.User.Identity?.Name
            }, ct);

            var response = new SaveAssetResponse(
                result.Saved, result.AssetId, result.RowVersion, result.Validation.Messages, result.Conflict);

            if (result.Conflict is not null) return Results.Conflict(response);
            if (!result.Saved) return Results.UnprocessableEntity(response);

            return id is null
                ? Results.Created($"/api/assets/{result.AssetId}", response)
                : Results.Ok(response);
        }
        catch (FigureNotAvailableException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static bool TryParseDate(string? value, out DateOnly? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return false;
        parsed = d;
        return true;
    }
}
