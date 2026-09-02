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

/// <summary>
/// One figure of B3's catalogue as the picker sees it, whether or not this platform can book it.
/// </summary>
public sealed record FigureCatalogueEntry(
    string Code,
    string Name,
    string? B3Name,
    string? CommercialName,
    string? Description,
    IReadOnlyList<string> Modalities,
    // Available | Pending | Quarantined | Retired | NotConfigured.
    string Availability,
    // True only when a new asset can be booked against this figure right now.
    bool Bookable,
    int? TemplateVersion,
    bool CalculatedByB3,
    // False when the code is modelled here but absent from the loaded B3 export.
    bool InB3Catalogue,
    // Why a quarantined figure does not compile.
    string? LastError);

/// <summary>How much of B3's catalogue the platform actually covers.</summary>
public sealed record FigureCoverage(int Published, int Configured, int Bookable);

public sealed record FigureCatalogueResponse(IReadOnlyList<FigureCatalogueEntry> Figures, FigureCoverage Coverage);

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

/// <summary>One CETIP upload file, with its content as it would be sent.</summary>
/// <param name="Layout">The section of the ENVIAR ARQUIVOS manual it follows.</param>
/// <param name="Operation">The operation code its header carries.</param>
/// <param name="FileName">A suggested name; the operational one is agreed with B3.</param>
/// <param name="Records">Record lines, not counting the header.</param>
/// <param name="Content">The whole file, CRLF-terminated.</param>
public sealed record ClearingFileResponse(
    string Layout, string Operation, string FileName, int Records, string Content);

/// <param name="Files">The files this asset's registration is made of.</param>
/// <param name="Notes">
/// What went into them, and what could not: an attribute B3 registers for the figure that the
/// template cannot address is named here rather than silently left out of the upload.
/// </param>
public sealed record ClearingResponse(
    IReadOnlyList<ClearingFileResponse> Files, IReadOnlyList<string> Notes);

/// <summary>One kept upload file. The bytes stay in the database; this is what identifies them.</summary>
/// <param name="Id">Download it at <c>/api/assets/{assetId}/clearing/saved/{id}</c>.</param>
/// <param name="ContentHash">
/// sha256 over the stored bytes. Two generations of the same certificate on the same day should
/// produce the same file, and this says whether they did without reading both back.
/// </param>
public sealed record StoredClearingFileResponse(
    Guid Id, string Layout, string Operation, string FileName, int Records, int Bytes, string ContentHash);

/// <summary>
/// One generation as it was kept: the files, and what they were written from and under. The
/// inputs are stored rather than re-read, because an edit to the asset, a new template version
/// or a different participant name would all produce a different file from the same certificate.
/// </summary>
public sealed record StoredClearingSetResponse(
    Guid Id,
    Guid AssetId,
    string FigureCode,
    int TemplateVersion,
    string ParticipantName,
    DateOnly FileDate,
    IReadOnlyList<string> Notes,
    DateTimeOffset GeneratedUtc,
    string? GeneratedBy,
    IReadOnlyList<StoredClearingFileResponse> Files);

public static class ContractMapping
{
    public static AssetListItem ToListItem(AssetListRow a) => new(
        a.Id, a.FigureCode, a.FigureName, a.CommercialName, a.InstrumentCode, a.IsinCode,
        a.IssueDate, a.MaturityDate, a.Modality, a.UnderlyingClass, a.Underlying,
        a.NotionalAmount, a.Status.ToString(), a.UpdatedUtc);

    public static StoredClearingSetResponse ToStoredSet(ClearingFileSetRow s) => new(
        s.Id, s.AssetId, s.FigureCode, s.TemplateVersion, s.ParticipantName, s.FileDate,
        s.Notes, s.GeneratedUtc, s.GeneratedBy,
        s.Files.Select(f => new StoredClearingFileResponse(
            f.Id, f.Layout, f.Operation, f.FileName, f.RecordCount, f.ByteCount, f.ContentHash)).ToList());

    public static StoredClearingSetResponse ToStoredSet(StoredClearingFileSet s) => new(
        s.Id, s.AssetId, s.FigureCode, s.TemplateVersion, s.ParticipantName, s.FileDate,
        s.Notes, s.GeneratedUtc, s.GeneratedBy,
        s.Files.Select(f => new StoredClearingFileResponse(
            f.Id, f.Layout, f.Operation, f.FileName, f.RecordCount, f.ByteCount, f.ContentHash)).ToList());

    public static AssetDetail ToDetail(Asset a) => new(
        a.Id, a.FigureCode, a.TemplateVersion, a.Status.ToString(),
        JsonNode.Parse(a.ValuesJson),
        a.RowVersion is null ? null : Convert.ToBase64String(a.RowVersion),
        a.CreatedUtc, a.CreatedBy, a.UpdatedUtc, a.UpdatedBy);
}
