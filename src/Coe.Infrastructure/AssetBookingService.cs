using System.Text.Json;
using System.Text.Json.Nodes;
using Coe.Core.Assets;
using Coe.Core.Expressions;
using Coe.Core.Figures;
using Coe.Core.Templates;
using Coe.Core.Validation;
using Coe.Infrastructure.ServerChecks;

namespace Coe.Infrastructure;

public sealed record BookingRequest
{
    public Guid? Id { get; init; }
    public required string FigureCode { get; init; }
    public required JsonObject Values { get; init; }

    /// <summary>Base64 rowversion from the load, for optimistic concurrency on edit.</summary>
    public string? RowVersion { get; init; }

    public string? User { get; init; }
    public string Culture { get; init; } = "pt-BR";

    /// <summary>Save despite warnings. Errors always block.</summary>
    public bool AcceptWarnings { get; init; }
}

public sealed record BookingResult(bool Saved, Guid? AssetId, ValidationResult Validation, string? RowVersion, string? Conflict = null);

/// <summary>
/// The save path. Whatever the client validated as the user typed, this runs the full
/// <see cref="ValidationScope.Submit"/> pass — including the server-only checks — recomputes
/// derived attributes, and only then writes. The client's checks are for feedback; this is
/// the gate.
/// </summary>
public sealed class AssetBookingService(
    ITemplateStore templates,
    IAssetRepository assets,
    IFigureCatalog figures,
    ValidationEngine engine,
    ICurrentAssetContext currentAsset)
{
    public async Task<BookingResult> ValidateAsync(
        string figureCode,
        JsonObject values,
        ValidationScope scope,
        IReadOnlyCollection<string>? changedPaths,
        Guid? assetId,
        string culture,
        CancellationToken ct = default)
    {
        var template = await templates.GetActiveAsync(figureCode, ct)
                       ?? throw new FigureNotAvailableException(figureCode);

        currentAsset.AssetId = assetId;
        ComputedFields.Apply(template, values, Variables());

        var result = engine.Validate(template, values, scope, changedPaths, Variables(), culture);
        return new BookingResult(false, assetId, result, null);
    }

    public async Task<BookingResult> SaveAsync(BookingRequest request, CancellationToken ct = default)
    {
        var figure = await figures.GetAsync(request.FigureCode, ct)
                     ?? throw new FigureNotAvailableException(request.FigureCode);

        if (figure.Status != FigureStatus.Enabled && request.Id is null)
            throw new FigureNotAvailableException(request.FigureCode,
                $"Figure {request.FigureCode} is {figure.Status} and cannot be booked.");

        var template = await templates.GetActiveAsync(request.FigureCode, ct)
                       ?? throw new FigureNotAvailableException(request.FigureCode);

        currentAsset.AssetId = request.Id;
        var values = request.Values;
        ComputedFields.Apply(template, values, Variables());

        var validation = engine.Validate(template, values, ValidationScope.Submit, null, Variables(), request.Culture);

        var blocking = validation.Messages.Where(m => m.Severity == RuleSeverity.Error).ToList();
        var warnings = validation.Messages.Where(m => m.Severity == RuleSeverity.Warning).ToList();

        if (blocking.Count > 0 || (warnings.Count > 0 && !request.AcceptWarnings))
            return new BookingResult(false, request.Id, validation, request.RowVersion);

        var asset = Project(request, template, values, warnings);

        try
        {
            if (request.Id is null)
            {
                await assets.AddAsync(asset, ct);
            }
            else
            {
                // Creation stamps belong to the original booking, not to this edit.
                var existing = await assets.GetAsync(asset.Id, ct);
                if (existing is not null)
                {
                    asset.CreatedUtc = existing.CreatedUtc;
                    asset.CreatedBy = existing.CreatedBy;
                }

                var rowVersion = request.RowVersion is null ? null : Convert.FromBase64String(request.RowVersion);
                await assets.UpdateAsync(asset, rowVersion, ct);
            }
        }
        catch (AssetConcurrencyException ex)
        {
            return new BookingResult(false, request.Id, validation, request.RowVersion, ex.Message);
        }

        var saved = await assets.GetAsync(asset.Id, ct);
        return new BookingResult(true, asset.Id, validation,
            saved?.RowVersion is null ? null : Convert.ToBase64String(saved.RowVersion));
    }

    // The grid columns are a projection of the instance document: booking writes them, nothing
    // else does, so the list can filter on indexed columns without opening the JSON.
    private static Asset Project(BookingRequest request, FigureTemplate template, JsonObject values, List<ValidationMessage> warnings)
    {
        var now = DateTimeOffset.UtcNow;
        var common = values["common"] as JsonObject;
        var underlying = values["underlying"] as JsonObject;

        return new Asset
        {
            Id = request.Id ?? Guid.CreateVersion7(),
            FigureCode = request.FigureCode,
            TemplateVersion = template.Version,
            InstrumentCode = Str(common, "instrumentCode"),
            IsinCode = Str(common, "isin"),
            CommercialName = Str(common, "commercialName") ?? "(sem nome)",
            IssuerAccount = Str(common, "issuerAccount"),
            IssueDate = Date(common, "issueDate") ?? DateOnly.FromDateTime(now.UtcDateTime),
            MaturityDate = Date(common, "maturityDate") ?? DateOnly.FromDateTime(now.UtcDateTime).AddYears(1),
            Modality = Str(common, "modality"),
            UnderlyingClass = Str(underlying, "assetClass"),
            Underlying = Str(underlying, "asset"),
            Quantity = (long?)Num(common, "quantity"),
            UnitIssuePrice = Num(common, "unitIssuePrice"),
            NotionalAmount = Num(common, "notional"),
            Status = AssetStatus.Validated,
            ValuesJson = values.ToJsonString(TemplateJson.Options),
            WarningsJson = warnings.Count == 0 ? null : JsonSerializer.Serialize(warnings, TemplateJson.Options),
            CreatedUtc = now,
            CreatedBy = request.User,
            UpdatedUtc = now,
            UpdatedBy = request.User
        };
    }

    private static IReadOnlyDictionary<string, object?> Variables() => new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["today"] = DateOnly.FromDateTime(DateTime.UtcNow)
    };

    private static string? Str(JsonObject? o, string key) => Values.AsString(Values.FromJson(o?[key]));
    private static decimal? Num(JsonObject? o, string key) => Values.AsNumber(Values.FromJson(o?[key]));
    private static DateOnly? Date(JsonObject? o, string key) => Values.AsDate(Values.FromJson(o?[key]));
}

public sealed class FigureNotAvailableException(string figureCode, string? message = null)
    : Exception(message ?? $"No active template for figure {figureCode}.")
{
    public string FigureCode { get; } = figureCode;
}
