using System.Text.Json.Nodes;
using Coe.Clearing;
using Coe.Core.Assets;
using Coe.Core.Templates;
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
///
/// Generating and keeping are separate on purpose. A generation is a read — the preview a desk
/// looks at, repeatable and free — while a save is the record that these bytes are what B3 was
/// sent, on this date, under this participant name, from the values the asset held at that
/// moment. Only the second writes, and it stores the files rather than the inputs to rebuild
/// them: an edit, a new template version or a different short name would all produce a different
/// file from the same certificate.
/// </summary>
public sealed class AssetClearingService(
    IAssetRepository assets,
    ITemplateStore templates,
    B3ReferenceProvider references,
    IClearingFileRepository storedFiles,
    ClearingOptions options,
    ILogger<AssetClearingService> logger)
{
    /// <summary>The files as they would be sent, without keeping them.</summary>
    public async Task<ClearingFileSet?> GenerateAsync(
        Guid assetId, string? participantName, DateOnly? fileDate, CancellationToken ct = default) =>
        (await PrepareAsync(assetId, participantName, fileDate, ct))?.Set;

    /// <summary>
    /// Generates the files and stores them, bytes and all. What comes back is the stored set,
    /// with the identifiers each file can be downloaded by afterwards.
    /// </summary>
    public async Task<StoredClearingFileSet?> SaveAsync(
        Guid assetId, string? participantName, DateOnly? fileDate, string? user, CancellationToken ct = default)
    {
        var generation = await PrepareAsync(assetId, participantName, fileDate, ct);
        if (generation is null) return null;

        var set = new StoredClearingFileSet(
            Id: Guid.Empty,
            AssetId: assetId,
            FigureCode: generation.Asset.FigureCode,
            // The version the files were actually written against, which is the one the asset
            // was booked on unless it is gone and the active one stood in for it.
            TemplateVersion: generation.Template.Version,
            ParticipantName: generation.ParticipantName,
            FileDate: generation.FileDate,
            Notes: generation.Set.Notes,
            GeneratedUtc: DateTimeOffset.UtcNow,
            GeneratedBy: user,
            Files: generation.Set.Files.Select(Store).ToList());

        return await storedFiles.AddAsync(set, ct);
    }

    /// <summary>Every generation kept for this asset, newest first, without the uploads.</summary>
    public Task<IReadOnlyList<ClearingFileSetRow>> ListSavedAsync(Guid assetId, CancellationToken ct = default) =>
        storedFiles.ListAsync(assetId, limit: 50, ct);

    /// <summary>One stored file with its bytes, as it was written.</summary>
    public Task<StoredClearingFile?> GetSavedFileAsync(Guid assetId, Guid fileId, CancellationToken ct = default) =>
        storedFiles.GetFileAsync(assetId, fileId, ct);

    /// <summary>What a generation produced, and the inputs it was produced under.</summary>
    private sealed record Generation(
        Asset Asset, FigureTemplate Template, ClearingFileSet Set, string ParticipantName, DateOnly FileDate);

    private async Task<Generation?> PrepareAsync(
        Guid assetId, string? participantName, DateOnly? fileDate, CancellationToken ct)
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

        var stamp = fileDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var request = new ClearingRequest(
            template,
            ParseValues(asset),
            participant,
            references.Current.Figure(asset.FigureCode)?.Ordinal ?? string.Empty,
            stamp,
            asset.InstrumentCode);

        var set = ClearingFileGenerator.ForRegistration(request);

        logger.LogInformation(
            "Wrote {Count} CETIP file(s) for asset {AssetId} ({FigureCode}): {Files}",
            set.Files.Count, assetId, asset.FigureCode, string.Join(", ", set.Files.Select(f => f.Operation)));

        return new Generation(asset, template, set, participant, stamp);
    }

    /// <summary>
    /// One file on its way to the database: the bytes as they would be uploaded, single-byte
    /// encoded, and the hash that says whether a later generation produced the same file.
    /// </summary>
    private static StoredClearingFile Store(CetipFile file)
    {
        var content = file.ToBytes();
        return new StoredClearingFile(
            Guid.Empty, file.Layout, file.Operation, file.FileName, file.RecordCount,
            content, ClearingFileRepository.Hash(content));
    }

    /// <summary>
    /// The booked attributes. A row whose JSON is not an object is a row nothing can be written
    /// from, and saying which asset it was beats a null-reference somewhere inside a layout.
    /// </summary>
    private static JsonObject ParseValues(Asset asset) =>
        JsonNode.Parse(asset.ValuesJson) as JsonObject
        ?? throw new InvalidOperationException($"Asset {asset.Id} does not hold a JSON object of values.");
}
