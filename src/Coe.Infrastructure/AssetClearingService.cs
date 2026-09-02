using System.Text.Json.Nodes;
using Coe.Clearing;
using Coe.Core.Assets;
using Coe.Ingestion;
using Microsoft.Extensions.Logging;

namespace Coe.Infrastructure;

public sealed class ClearingOptions
{
    public const string SectionName = "Clearing";

    /// <summary>
    /// "Nome Simplificado do Participante": the issuer's short name at B3, which every upload
    /// header carries. It is published in the <c>mnemonicos_cetip</c> export against the
    /// institution's account, and is not something the platform can derive.
    /// </summary>
    public string ParticipantName { get; set; } = string.Empty;
}

/// <summary>
/// Turns a booked asset into the files its registration is sent to B3 as.
///
/// The asset is the source of everything in them: the template it was booked against says which
/// attribute is which of B3's, and the values say what they hold. Nothing is asked of the caller
/// beyond the issuer's short name, which is not a property of the certificate.
/// </summary>
public sealed class AssetClearingService(
    IAssetRepository assets,
    ITemplateStore templates,
    B3ReferenceProvider references,
    ClearingOptions options,
    ILogger<AssetClearingService> logger)
{
    public async Task<ClearingFileSet?> GenerateAsync(
        Guid assetId, string? participantName, DateOnly? fileDate, CancellationToken ct = default)
    {
        var asset = await assets.GetAsync(assetId, ct);
        if (asset is null) return null;

        // The version the asset was booked against, not the current one: the codes and options
        // its values mean are the ones that template carried.
        var template = await templates.GetAsync(asset.FigureCode, asset.TemplateVersion, ct)
                       ?? await templates.GetActiveAsync(asset.FigureCode, ct);

        if (template is null)
            throw new InvalidOperationException(
                $"No template is stored for {asset.FigureCode} v{asset.TemplateVersion}; the asset cannot be written out.");

        var participant = string.IsNullOrWhiteSpace(participantName) ? options.ParticipantName : participantName!;
        if (string.IsNullOrWhiteSpace(participant))
            throw new InvalidOperationException(
                "No participant short name is configured (Clearing:ParticipantName) and none was given; "
                + "every CETIP upload header carries one.");

        var request = new ClearingRequest(
            template,
            ParseValues(asset),
            participant,
            references.Current.Figure(asset.FigureCode)?.Ordinal ?? string.Empty,
            fileDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            asset.InstrumentCode);

        var set = ClearingFileGenerator.ForRegistration(request);

        logger.LogInformation(
            "Wrote {Count} CETIP file(s) for asset {AssetId} ({FigureCode}): {Files}",
            set.Files.Count, assetId, asset.FigureCode, string.Join(", ", set.Files.Select(f => f.Operation)));

        return set;
    }

    /// <summary>
    /// The booked attributes. A row whose JSON is not an object is a row nothing can be written
    /// from, and saying which asset it was beats a null-reference somewhere inside a layout.
    /// </summary>
    private static JsonObject ParseValues(Asset asset) =>
        JsonNode.Parse(asset.ValuesJson) as JsonObject
        ?? throw new InvalidOperationException($"Asset {asset.Id} does not hold a JSON object of values.");
}
