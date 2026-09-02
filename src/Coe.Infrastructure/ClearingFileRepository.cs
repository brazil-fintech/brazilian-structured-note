using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Coe.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Coe.Infrastructure;

/// <summary>One stored upload file: the bytes as they would be sent, and what they are.</summary>
public sealed record StoredClearingFile(
    Guid Id,
    string Layout,
    string Operation,
    string FileName,
    int RecordCount,
    byte[] Content,
    string ContentHash)
{
    public int ByteCount => Content.Length;
}

/// <summary>
/// The files one certificate produced in a single pass, with what they were written from and
/// under. A stored set answers "what did B3 receive, and on what basis" without any of it having
/// to be inferred from the asset as it stands today.
/// </summary>
public sealed record StoredClearingFileSet(
    Guid Id,
    Guid AssetId,
    string FigureCode,
    int TemplateVersion,
    string ParticipantName,
    DateOnly FileDate,
    IReadOnlyList<string> Notes,
    DateTimeOffset GeneratedUtc,
    string? GeneratedBy,
    IReadOnlyList<StoredClearingFile> Files);

/// <summary>
/// A row of the generation history. The content is deliberately absent: listing what an asset
/// has produced does not need the uploads themselves, and reading a few hundred kilobytes of
/// <c>varbinary(max)</c> per row to render a list would dominate the query.
/// </summary>
public sealed record ClearingFileSetRow(
    Guid Id,
    Guid AssetId,
    string FigureCode,
    int TemplateVersion,
    string ParticipantName,
    DateOnly FileDate,
    IReadOnlyList<string> Notes,
    DateTimeOffset GeneratedUtc,
    string? GeneratedBy,
    IReadOnlyList<ClearingFileRow> Files);

public sealed record ClearingFileRow(
    Guid Id, string Layout, string Operation, string FileName, int RecordCount, int ByteCount, string ContentHash);

public interface IClearingFileRepository
{
    /// <summary>Stores a generation and returns it with the identifiers it was written under.</summary>
    Task<StoredClearingFileSet> AddAsync(StoredClearingFileSet set, CancellationToken ct = default);

    /// <summary>What this asset has produced, newest first, without the uploads themselves.</summary>
    Task<IReadOnlyList<ClearingFileSetRow>> ListAsync(Guid assetId, int limit = 50, CancellationToken ct = default);

    /// <summary>One stored file, with its bytes, scoped to the asset it belongs to.</summary>
    Task<StoredClearingFile?> GetFileAsync(Guid assetId, Guid fileId, CancellationToken ct = default);
}

public sealed class ClearingFileRepository(
    ISqlConnectionFactory connections,
    SqlConnectionOptions options,
    ILogger<ClearingFileRepository> logger) : IClearingFileRepository
{
    private readonly SqlRetryPolicy _retry = new(options.MaxRetries, logger);

    public Task<StoredClearingFileSet> AddAsync(StoredClearingFileSet set, CancellationToken ct = default) =>
        _retry.ExecuteAsync("clearing.insert", async token =>
        {
            const string insertSet = """
                INSERT INTO clearing.FileSet
                    (Id, AssetId, FigureCode, TemplateVersion, ParticipantName, FileDate,
                     NotesJson, GeneratedUtc, GeneratedBy)
                VALUES
                    (@id, @assetId, @figureCode, @templateVersion, @participantName, @fileDate,
                     @notesJson, @generatedUtc, @generatedBy);
                """;

            const string insertFile = """
                INSERT INTO clearing.UploadFile
                    (Id, SetId, Layout, Operation, FileName, RecordCount, Content, ByteCount, ContentHash)
                VALUES
                    (@id, @setId, @layout, @operation, @fileName, @recordCount, @content, @byteCount, @contentHash);
                """;

            var setId = set.Id == Guid.Empty ? Guid.NewGuid() : set.Id;
            var files = set.Files
                .Select(f => f with { Id = f.Id == Guid.Empty ? Guid.NewGuid() : f.Id })
                .ToList();

            await using var connection = await connections.OpenAsync(token);

            // The set and its files are one artifact: a Registro COE stored without the Fluxo de
            // Caixa that completes it would read as a registration that needed none.
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(token);

            await using (var command = new SqlCommand(insertSet, connection, transaction))
            {
                command.UniqueIdentifier("@id", setId);
                command.UniqueIdentifier("@assetId", set.AssetId);
                command.NVarChar("@figureCode", set.FigureCode, 20);
                command.Int("@templateVersion", set.TemplateVersion);
                command.NVarChar("@participantName", set.ParticipantName, 60);
                command.Date("@fileDate", set.FileDate);
                command.NVarCharMax("@notesJson", JsonSerializer.Serialize(set.Notes));
                command.DateTimeOffset("@generatedUtc", set.GeneratedUtc);
                command.NVarChar("@generatedBy", set.GeneratedBy, 100);
                await command.ExecuteNonQueryAsync(token);
            }

            foreach (var file in files)
            {
                await using var command = new SqlCommand(insertFile, connection, transaction);
                command.UniqueIdentifier("@id", file.Id);
                command.UniqueIdentifier("@setId", setId);
                command.NVarChar("@layout", file.Layout, 80);
                command.NVarChar("@operation", file.Operation, 10);
                command.NVarChar("@fileName", file.FileName, 120);
                command.Int("@recordCount", file.RecordCount);
                command.VarBinaryMax("@content", file.Content);
                command.Int("@byteCount", file.Content.Length);
                command.NVarChar("@contentHash", file.ContentHash, 80);
                await command.ExecuteNonQueryAsync(token);
            }

            await transaction.CommitAsync(token);

            logger.LogInformation(
                "Stored clearing set {SetId} for asset {AssetId}: {Count} file(s), {Operations}",
                setId, set.AssetId, files.Count, string.Join(", ", files.Select(f => f.Operation)));

            return set with { Id = setId, Files = files };
        }, ct);

    public Task<IReadOnlyList<ClearingFileSetRow>> ListAsync(Guid assetId, int limit = 50, CancellationToken ct = default) =>
        _retry.ExecuteAsync<IReadOnlyList<ClearingFileSetRow>>("clearing.list", async token =>
        {
            // Two result sets over one round trip: the generations, then the files of exactly
            // those generations. A join would repeat every set column once per file it holds.
            const string sql = """
                SELECT TOP (@limit) Id, AssetId, FigureCode, TemplateVersion, ParticipantName,
                       FileDate, NotesJson, GeneratedUtc, GeneratedBy
                  FROM clearing.FileSet
                 WHERE AssetId = @assetId
                 ORDER BY GeneratedUtc DESC, Id DESC;

                SELECT f.SetId, f.Id, f.Layout, f.Operation, f.FileName, f.RecordCount, f.ByteCount, f.ContentHash
                  FROM clearing.UploadFile AS f
                 WHERE f.SetId IN (SELECT TOP (@limit) Id
                                     FROM clearing.FileSet
                                    WHERE AssetId = @assetId
                                    ORDER BY GeneratedUtc DESC, Id DESC)
                 ORDER BY f.Operation;
                """;

            await using var connection = await connections.OpenAsync(token);
            await using var command = new SqlCommand(sql, connection);
            command.UniqueIdentifier("@assetId", assetId);
            command.Int("@limit", Math.Clamp(limit, 1, 200));

            await using var reader = await command.ExecuteReaderAsync(token);

            var sets = new List<ClearingFileSetRow>();
            while (await reader.ReadAsync(token))
                sets.Add(new ClearingFileSetRow(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetString(4),
                    reader.GetDateOnly(5),
                    ReadNotes(reader.GetNullableString(6)),
                    reader.GetDateTimeOffset(7),
                    reader.GetNullableString(8),
                    []));

            var filesBySet = new Dictionary<Guid, List<ClearingFileRow>>();
            if (await reader.NextResultAsync(token))
            {
                while (await reader.ReadAsync(token))
                {
                    var setId = reader.GetGuid(0);
                    if (!filesBySet.TryGetValue(setId, out var files))
                    {
                        files = [];
                        filesBySet[setId] = files;
                    }

                    files.Add(new ClearingFileRow(
                        reader.GetGuid(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetInt32(5),
                        reader.GetInt32(6),
                        reader.GetString(7)));
                }
            }

            return sets
                .Select(set => filesBySet.TryGetValue(set.Id, out var files) ? set with { Files = files } : set)
                .ToList();
        }, ct);

    public Task<StoredClearingFile?> GetFileAsync(Guid assetId, Guid fileId, CancellationToken ct = default) =>
        _retry.ExecuteAsync<StoredClearingFile?>("clearing.get_file", async token =>
        {
            // Joined to the set on the asset rather than looked up by id alone: a file id from
            // one certificate must not read another's upload.
            const string sql = """
                SELECT f.Id, f.Layout, f.Operation, f.FileName, f.RecordCount, f.Content, f.ContentHash
                  FROM clearing.UploadFile AS f
                  JOIN clearing.FileSet AS s ON s.Id = f.SetId
                 WHERE f.Id = @fileId AND s.AssetId = @assetId;
                """;

            await using var connection = await connections.OpenAsync(token);
            await using var command = new SqlCommand(sql, connection);
            command.UniqueIdentifier("@fileId", fileId);
            command.UniqueIdentifier("@assetId", assetId);

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, token);
            if (!await reader.ReadAsync(token)) return null;

            return new StoredClearingFile(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetFieldValue<byte[]>(5), reader.GetString(6));
        }, ct);

    /// <summary>
    /// The notes as stored. A row written before the column carried anything, or one whose JSON
    /// no longer parses, lists as a set without notes rather than failing the whole history.
    /// </summary>
    private static IReadOnlyList<string> ReadNotes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The identity of an upload, so two generations can be compared without both.</summary>
    public static string Hash(byte[] content) => $"sha256:{Convert.ToHexStringLower(SHA256.HashData(content))}";
}
