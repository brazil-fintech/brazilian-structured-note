using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coe.Core.Assets;
using Coe.Core.Diagnostics;
using Coe.Core.Expressions;
using Coe.Core.Figures;
using Coe.Core.Templates;
using Coe.Core.Validation;
using Coe.Infrastructure.ServerChecks;
using Microsoft.Extensions.Logging;

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
    IBusinessCalendar calendar,
    ValidationEngine engine,
    ILogger<AssetBookingService> logger)
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

        var facts = await ResolveFactsAsync(values, assetId, ct);
        ComputedFields.Apply(template, values, facts);

        var result = engine.Validate(template, values, scope, changedPaths, facts, culture);
        return new BookingResult(false, assetId, result, null);
    }

    public async Task<BookingResult> SaveAsync(BookingRequest request, CancellationToken ct = default)
    {
        using var activity = CoeDiagnostics.Booking.StartActivity("coe.asset.save", ActivityKind.Internal);
        activity?.SetTag("coe.figure.code", request.FigureCode);
        activity?.SetTag("coe.asset.id", request.Id);

        var figure = await figures.GetAsync(request.FigureCode, ct)
                     ?? throw new FigureNotAvailableException(request.FigureCode);

        if (figure.Status != FigureStatus.Enabled && request.Id is null)
            throw new FigureNotAvailableException(request.FigureCode,
                $"Figure {request.FigureCode} is {figure.Status} and cannot be booked.");

        var template = await templates.GetActiveAsync(request.FigureCode, ct)
                       ?? throw new FigureNotAvailableException(request.FigureCode);

        var values = request.Values;
        var facts = await ResolveFactsAsync(values, request.Id, ct);
        ComputedFields.Apply(template, values, facts);

        var validation = engine.Validate(template, values, ValidationScope.Submit, null, facts, request.Culture);

        var blocking = validation.Messages.Where(m => m.Severity == RuleSeverity.Error).ToList();
        var warnings = validation.Messages.Where(m => m.Severity == RuleSeverity.Warning).ToList();

        if (blocking.Count > 0 || (warnings.Count > 0 && !request.AcceptWarnings))
        {
            RecordSave("rejected", activity);
            logger.LogInformation(
                "Rejected save of {FigureCode} asset {AssetId}: {ErrorCount} error(s), {WarningCount} warning(s)",
                request.FigureCode, request.Id, blocking.Count, warnings.Count);
            return new BookingResult(false, request.Id, validation, request.RowVersion);
        }

        var asset = Project(request, template, values, warnings);

        try
        {
            byte[]? rowVersion;
            if (request.Id is null)
            {
                rowVersion = await assets.AddAsync(asset, ct);
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

                var expected = request.RowVersion is null ? null : Convert.FromBase64String(request.RowVersion);
                rowVersion = await assets.UpdateAsync(asset, expected, ct);
            }

            RecordSave("saved", activity);
            logger.LogInformation(
                "Saved {FigureCode} asset {AssetId} against template v{TemplateVersion} with {WarningCount} accepted warning(s)",
                request.FigureCode, asset.Id, template.Version, warnings.Count);

            return new BookingResult(true, asset.Id, validation,
                rowVersion is null ? null : Convert.ToBase64String(rowVersion));
        }
        catch (AssetConcurrencyException ex)
        {
            RecordSave("conflict", activity);
            logger.LogWarning("Concurrent edit detected on asset {AssetId}", asset.Id);
            return new BookingResult(false, request.Id, validation, request.RowVersion, ex.Message);
        }
    }

    /// <summary>
    /// Resolves everything a server-side check needs before the synchronous pass starts: the
    /// holiday calendar, and whether the instrument code is already taken. Doing it here means
    /// one query per request instead of one per rule evaluation, and keeps the engine free of I/O.
    /// </summary>
    private async Task<Dictionary<string, object?>> ResolveFactsAsync(
        JsonObject values, Guid? assetId, CancellationToken ct)
    {
        await calendar.EnsureLoadedAsync(BookingFacts.DefaultCalendar, ct);

        var facts = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["today"] = DateOnly.FromDateTime(DateTime.UtcNow)
        };

        var instrumentCode = Str(values["common"] as JsonObject, "instrumentCode");
        if (!string.IsNullOrWhiteSpace(instrumentCode))
            facts[BookingFacts.InstrumentCodeTaken] = await assets.InstrumentCodeTakenAsync(instrumentCode, assetId, ct);

        return facts;
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
            // A sequential GUID keeps inserts at the end of the clustered index instead of
            // splitting pages all over it.
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

    private static void RecordSave(string outcome, Activity? activity)
    {
        CoeDiagnostics.AssetSaves.Add(1, new KeyValuePair<string, object?>("coe.save.outcome", outcome));
        activity?.SetTag("coe.save.outcome", outcome);
        if (outcome != "saved") activity?.SetStatus(ActivityStatusCode.Error, outcome);
    }

    private static string? Str(JsonObject? o, string key) => Values.AsString(Values.FromJson(o?[key]));
    private static decimal? Num(JsonObject? o, string key) => Values.AsNumber(Values.FromJson(o?[key]));
    private static DateOnly? Date(JsonObject? o, string key) => Values.AsDate(Values.FromJson(o?[key]));
}

public sealed class FigureNotAvailableException(string figureCode, string? message = null)
    : Exception(message ?? $"No active template for figure {figureCode}.")
{
    public string FigureCode { get; } = figureCode;
}
