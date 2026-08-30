using System.Text.Json.Nodes;
using Coe.Core.Assets;
using Coe.Core.Validation;
using Coe.Infrastructure;

namespace Coe.Api;

/// <summary>A figure offered in the "new asset" picker.</summary>
public sealed record FigureSummary(
    string Code,
    string Name,
    string? CommercialName,
    string? Description,
    IReadOnlyList<string> Modalities,
    string Status,
    int? TemplateVersion);

/// <summary>A row of the asset list.</summary>
public sealed record AssetListItem(
    Guid Id,
    string FigureCode,
    string? FigureName,
    string CommercialName,
    string? InstrumentCode,
    string? IsinCode,
    DateOnly IssueDate,
    DateOnly MaturityDate,
    string? Modality,
    string? UnderlyingClass,
    string? Underlying,
    decimal? NotionalAmount,
    string Status,
    DateTimeOffset UpdatedUtc);

public sealed record AssetListResponse(IReadOnlyList<AssetListItem> Items, int Total, int Page, int PageSize);

/// <summary>The full asset, as the edit screen loads it.</summary>
public sealed record AssetDetail(
    Guid Id,
    string FigureCode,
    int TemplateVersion,
    string Status,
    JsonNode? Values,
    string? RowVersion,
    DateTimeOffset CreatedUtc,
    string? CreatedBy,
    DateTimeOffset UpdatedUtc,
    string? UpdatedBy);

/// <summary>As-you-type validation. <paramref name="ChangedPaths"/> narrows the pass to what the user just touched.</summary>
public sealed record ValidateRequest(
    string FigureCode,
    JsonObject Values,
    IReadOnlyList<string>? ChangedPaths = null,
    Guid? AssetId = null,
    string Scope = "field",
    string Culture = "pt-BR");

public sealed record ValidateResponse(
    IReadOnlyList<ValidationMessage> Messages,
    IReadOnlyList<string> EvaluatedPaths,
    bool IsValid);

public sealed record SaveAssetRequest(
    string FigureCode,
    JsonObject Values,
    string? RowVersion = null,
    bool AcceptWarnings = false,
    string Culture = "pt-BR");

public sealed record SaveAssetResponse(
    bool Saved,
    Guid? AssetId,
    string? RowVersion,
    IReadOnlyList<ValidationMessage> Messages,
    string? Conflict);

/// <summary>An entry of a reference list named by a field's <c>optionSource</c>.</summary>
public sealed record ReferenceItem(string Code, string Name, string? Group);

public static class ContractMapping
{
    public static AssetListItem ToListItem(AssetListRow a) => new(
        a.Id, a.FigureCode, a.FigureName, a.CommercialName, a.InstrumentCode, a.IsinCode,
        a.IssueDate, a.MaturityDate, a.Modality, a.UnderlyingClass, a.Underlying,
        a.NotionalAmount, a.Status.ToString(), a.UpdatedUtc);

    public static AssetDetail ToDetail(Asset a) => new(
        a.Id, a.FigureCode, a.TemplateVersion, a.Status.ToString(),
        JsonNode.Parse(a.ValuesJson),
        a.RowVersion is null ? null : Convert.ToBase64String(a.RowVersion),
        a.CreatedUtc, a.CreatedBy, a.UpdatedUtc, a.UpdatedBy);
}
