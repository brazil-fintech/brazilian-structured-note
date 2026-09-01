using System.Data;
using Coe.Core.Figures;
using Coe.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Coe.Infrastructure;

/// <inheritdoc cref="IFigureCatalog"/>
public sealed class FigureCatalog(
    ISqlConnectionFactory connections,
    SqlConnectionOptions options,
    ILogger<FigureCatalog> logger) : IFigureCatalog
{
    private readonly SqlRetryPolicy _retry = new(options.MaxRetries, logger);

    private const string FigureColumns = """
        Code, Name, CommercialName, DescriptionPt, DescriptionEn, Modalities, Status,
        ActiveTemplateVersion, SourceFile, SourceHash, LastError, FirstSeenUtc, UpdatedUtc, EnabledUtc
        """;


    public Task<Figure?> GetAsync(string code, CancellationToken ct = default) =>
        _retry.ExecuteAsync("figure.get", async token =>
        {
            await using var connection = await connections.OpenAsync(token);
            await using var command = new SqlCommand($"SELECT {FigureColumns} FROM figure.Figure WHERE Code = @code", connection);
            command.NVarChar("@code", code, 20);

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, token);
            return await reader.ReadAsync(token) ? ReadFigure(reader) : null;
        }, ct);

    public Task<IReadOnlyList<Figure>> ListAsync(bool enabledOnly = true, CancellationToken ct = default) =>
        _retry.ExecuteAsync<IReadOnlyList<Figure>>("figure.list", async token =>
        {
            var sql = enabledOnly
                ? $"SELECT {FigureColumns} FROM figure.Figure WHERE Status = @status ORDER BY Code"
                : $"SELECT {FigureColumns} FROM figure.Figure ORDER BY Code";

            await using var connection = await connections.OpenAsync(token);
            await using var command = new SqlCommand(sql, connection);
            if (enabledOnly) command.NVarChar("@status", nameof(FigureStatus.Enabled), 20);

            await using var reader = await command.ExecuteReaderAsync(token);
            var figures = new List<Figure>();
            while (await reader.ReadAsync(token)) figures.Add(ReadFigure(reader));
            return figures;
        }, ct);

    /// <summary>
    /// B3's catalogue and the platform's figures, side by side.
    ///
    /// A FULL OUTER JOIN rather than a LEFT JOIN from either side: the reference export may not
    /// have been loaded yet (b3.Figure empty, and the picker must still offer what is bookable),
    /// and a figure modelled here may be missing from a newer export (which is worth showing, not
    /// hiding). One round trip; both tables are small enough that the sort is free.
    /// </summary>
    public Task<IReadOnlyList<CatalogueFigure>> ListCatalogueAsync(CancellationToken ct = default) =>
        _retry.ExecuteAsync<IReadOnlyList<CatalogueFigure>>("figure.list_catalogue", async token =>
        {
            // The figure columns keep the order ReadFigure expects, starting at ordinal 4.
            const string sql = """
                SELECT COALESCE(b.Code, f.Code) AS Code,
                       b.Name AS B3Name,
                       ISNULL(b.Calculated, CAST(0 AS bit)) AS Calculated,
                       CASE WHEN b.Code IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS InCatalogue,
                       f.Code, f.Name, f.CommercialName, f.DescriptionPt, f.DescriptionEn,
                       f.Modalities, f.Status, f.ActiveTemplateVersion, f.SourceFile, f.SourceHash,
                       f.LastError, f.FirstSeenUtc, f.UpdatedUtc, f.EnabledUtc
                  FROM b3.Figure b
                  FULL OUTER JOIN figure.Figure f ON f.Code = b.Code
                 ORDER BY COALESCE(b.Code, f.Code);
                """;

            await using var connection = await connections.OpenAsync(token);
            await using var command = new SqlCommand(sql, connection);

            await using var reader = await command.ExecuteReaderAsync(token);
            var figures = new List<CatalogueFigure>();
            while (await reader.ReadAsync(token))
            {
                figures.Add(new CatalogueFigure
                {
                    Code = reader.GetString(0),
                    B3Name = reader.GetNullableString(1),
                    CalculatedByB3 = reader.GetBoolean(2),
                    InB3Catalogue = reader.GetBoolean(3),
                    // The platform side of an outer join is null for a figure with no domain file.
                    Figure = reader.IsDBNull(4) ? null : ReadFigure(reader, offset: 4)
                });
            }

            return figures;
        }, ct);

    /// <summary>
    /// Single-statement upsert. The ingestion worker runs this for every changed figure, and a
    /// read-then-write would leave a window for two workers to both decide the row is missing.
    /// </summary>
    public Task UpsertAsync(Figure figure, CancellationToken ct = default) =>
        _retry.ExecuteAsync("figure.upsert", async token =>
        {
            const string sql = """
                UPDATE figure.Figure
                   SET Name = @name, CommercialName = @commercialName,
                       DescriptionPt = @descriptionPt, DescriptionEn = @descriptionEn,
                       Modalities = @modalities, Status = @status,
                       ActiveTemplateVersion = @activeTemplateVersion,
                       SourceFile = @sourceFile, SourceHash = @sourceHash,
                       LastError = @lastError, UpdatedUtc = @updatedUtc, EnabledUtc = @enabledUtc
                 WHERE Code = @code;

                IF @@ROWCOUNT = 0
                    INSERT INTO figure.Figure
                        (Code, Name, CommercialName, DescriptionPt, DescriptionEn, Modalities, Status,
                         ActiveTemplateVersion, SourceFile, SourceHash, LastError, FirstSeenUtc, UpdatedUtc, EnabledUtc)
                    VALUES
                        (@code, @name, @commercialName, @descriptionPt, @descriptionEn, @modalities, @status,
                         @activeTemplateVersion, @sourceFile, @sourceHash, @lastError, @firstSeenUtc, @updatedUtc, @enabledUtc);
                """;

            await using var connection = await connections.OpenAsync(token);
            await using var command = new SqlCommand(sql, connection);
            command.NVarChar("@code", figure.Code, 20);
            command.NVarChar("@name", figure.Name, 200);
            command.NVarChar("@commercialName", figure.CommercialName, 200);
            command.NVarCharMax("@descriptionPt", figure.DescriptionPt);
            command.NVarCharMax("@descriptionEn", figure.DescriptionEn);
            command.NVarChar("@modalities", figure.Modalities, 50);
            command.NVarChar("@status", figure.Status.ToString(), 20);
            command.Int("@activeTemplateVersion", figure.ActiveTemplateVersion);
            command.NVarChar("@sourceFile", figure.SourceFile, 400);
            command.NVarChar("@sourceHash", figure.SourceHash, 80);
            command.NVarCharMax("@lastError", figure.LastError);
            command.DateTimeOffset("@firstSeenUtc", figure.FirstSeenUtc);
            command.DateTimeOffset("@updatedUtc", figure.UpdatedUtc);
            var enabled = command.Parameters.Add("@enabledUtc", SqlDbType.DateTimeOffset);
            enabled.Value = (object?)figure.EnabledUtc ?? DBNull.Value;

            await command.ExecuteNonQueryAsync(token);
        }, ct);

    public Task<int> LatestTemplateVersionAsync(string code, CancellationToken ct = default) =>
        _retry.ExecuteAsync("figure.latest_template_version", async token =>
        {
            await using var connection = await connections.OpenAsync(token);
            await using var command = new SqlCommand(
                "SELECT ISNULL(MAX(Version), 0) FROM figure.FigureTemplate WHERE FigureCode = @code", connection);
            command.NVarChar("@code", code, 20);

            var result = await command.ExecuteScalarAsync(token);
            return result is int version ? version : 0;
        }, ct);

    /// <summary>
    /// Stores a new version and makes it the active one. Both statements run in one transaction
    /// because the filtered unique index allows only a single active row per figure — a partial
    /// application would leave the figure with no servable template.
    /// </summary>
    public Task AddTemplateVersionAsync(FigureTemplateRecord record, CancellationToken ct = default) =>
        _retry.ExecuteAsync("figure.add_template_version", async token =>
        {
            await using var connection = await connections.OpenAsync(token);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);

            const string sql = """
                UPDATE figure.FigureTemplate SET IsActive = 0
                 WHERE FigureCode = @figureCode AND IsActive = 1;

                INSERT INTO figure.FigureTemplate
                    (FigureCode, Version, SchemaVersion, TemplateJson, SourceHash, SourceFile, IsActive, CreatedUtc, CreatedBy)
                VALUES
                    (@figureCode, @version, @schemaVersion, @templateJson, @sourceHash, @sourceFile, @isActive, @createdUtc, @createdBy);
                """;

            await using var command = new SqlCommand(sql, connection, transaction);
            command.NVarChar("@figureCode", record.FigureCode, 20);
            command.Int("@version", record.Version);
            command.NVarChar("@schemaVersion", record.SchemaVersion, 10);
            command.NVarCharMax("@templateJson", record.TemplateJson);
            command.NVarChar("@sourceHash", record.SourceHash, 80);
            command.NVarChar("@sourceFile", record.SourceFile, 400);
            command.Bit("@isActive", record.IsActive);
            command.DateTimeOffset("@createdUtc", record.CreatedUtc);
            command.NVarChar("@createdBy", record.CreatedBy, 100);

            await command.ExecuteNonQueryAsync(token);
            await transaction.CommitAsync(token);
        }, ct);

    public Task<FigureTemplateRecord?> GetActiveTemplateAsync(string code, CancellationToken ct = default) =>
        ReadTemplateAsync(
            "SELECT Id, FigureCode, Version, SchemaVersion, SourceHash, SourceFile, IsActive, CreatedUtc, CreatedBy, TemplateJson " +
            "FROM figure.FigureTemplate WHERE FigureCode = @code AND IsActive = 1",
            command => command.NVarChar("@code", code, 20),
            "figure.get_active_template", ct);

    public Task<FigureTemplateRecord?> GetTemplateAsync(string code, int version, CancellationToken ct = default) =>
        ReadTemplateAsync(
            "SELECT Id, FigureCode, Version, SchemaVersion, SourceHash, SourceFile, IsActive, CreatedUtc, CreatedBy, TemplateJson " +
            "FROM figure.FigureTemplate WHERE FigureCode = @code AND Version = @version",
            command =>
            {
                command.NVarChar("@code", code, 20);
                command.Int("@version", version);
            },
            "figure.get_template", ct);

    private Task<FigureTemplateRecord?> ReadTemplateAsync(
        string sql, Action<SqlCommand> bind, string operation, CancellationToken ct) =>
        _retry.ExecuteAsync(operation, async token =>
        {
            await using var connection = await connections.OpenAsync(token);
            await using var command = new SqlCommand(sql, connection);
            bind(command);

            // TemplateJson is the last column and can be hundreds of kilobytes; sequential
            // access streams it instead of buffering the whole row up front.
            await using var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SingleRow | CommandBehavior.SequentialAccess, token);

            if (!await reader.ReadAsync(token)) return null;

            return new FigureTemplateRecord
            {
                Id = reader.GetInt64(0),
                FigureCode = reader.GetString(1),
                Version = reader.GetInt32(2),
                SchemaVersion = reader.GetString(3),
                SourceHash = reader.GetString(4),
                SourceFile = reader.GetNullableString(5),
                IsActive = reader.GetBoolean(6),
                CreatedUtc = reader.GetDateTimeOffset(7),
                CreatedBy = reader.GetNullableString(8),
                TemplateJson = reader.GetString(9)
            };
        }, ct);

    public Task RecordRunAsync(IngestionRun run, CancellationToken ct = default) =>
        _retry.ExecuteAsync("figure.record_run", async token =>
        {
            const string sql = """
                INSERT INTO figure.IngestionRun
                    (StartedUtc, CompletedUtc, FilesScanned, FiguresCreated, TemplatesCreated,
                     FiguresQuarantined, Status, Details)
                VALUES
                    (@startedUtc, @completedUtc, @filesScanned, @figuresCreated, @templatesCreated,
                     @figuresQuarantined, @status, @details);
                """;

            await using var connection = await connections.OpenAsync(token);
            await using var command = new SqlCommand(sql, connection);
            command.DateTimeOffset("@startedUtc", run.StartedUtc);
            var completed = command.Parameters.Add("@completedUtc", SqlDbType.DateTimeOffset);
            completed.Value = (object?)run.CompletedUtc ?? DBNull.Value;
            command.Int("@filesScanned", run.FilesScanned);
            command.Int("@figuresCreated", run.FiguresCreated);
            command.Int("@templatesCreated", run.TemplatesCreated);
            command.Int("@figuresQuarantined", run.FiguresQuarantined);
            command.NVarChar("@status", run.Status, 30);
            command.NVarCharMax("@details", run.Details);

            await command.ExecuteNonQueryAsync(token);
        }, ct);

    /// <summary>
    /// Reads the figure columns starting at <paramref name="offset"/>, so the same mapping serves a
    /// plain SELECT and the catalogue join that puts B3's columns first.
    /// </summary>
    private static Figure ReadFigure(SqlDataReader reader, int offset = 0) => new()
    {
        Code = reader.GetString(offset),
        Name = reader.GetString(offset + 1),
        CommercialName = reader.GetNullableString(offset + 2),
        DescriptionPt = reader.GetNullableString(offset + 3),
        DescriptionEn = reader.GetNullableString(offset + 4),
        Modalities = reader.GetString(offset + 5),
        Status = reader.GetEnum(offset + 6, FigureStatus.Pending),
        ActiveTemplateVersion = reader.GetNullableInt32(offset + 7),
        SourceFile = reader.GetNullableString(offset + 8),
        SourceHash = reader.GetNullableString(offset + 9),
        LastError = reader.GetNullableString(offset + 10),
        FirstSeenUtc = reader.GetDateTimeOffset(offset + 11),
        UpdatedUtc = reader.GetDateTimeOffset(offset + 12),
        EnabledUtc = reader.GetNullableDateTimeOffset(offset + 13)
    };
}
