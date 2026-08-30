using System.Data;
using System.Text;
using Coe.Core.Assets;
using Coe.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Coe.Infrastructure;

/// <summary>Filter of the asset list screen.</summary>
public sealed record AssetQuery
{
    /// <summary>Keeps assets live on this date: <c>IssueDate &lt;= referenceDate &lt;= MaturityDate</c>.</summary>
    public DateOnly? ReferenceDate { get; init; }

    public string? FigureCode { get; init; }
    public string? Modality { get; init; }
    public string? Underlying { get; init; }
    public AssetStatus? Status { get; init; }

    /// <summary>Free text over commercial name, instrument code and ISIN.</summary>
    public string? Search { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

/// <summary>
/// One row of the asset list. A projection rather than the <see cref="Asset"/> entity: the grid
/// needs the figure's display name and does not need the instance document, and saying so in the
/// type keeps the query honest about what it reads.
/// </summary>
public sealed record AssetListRow(
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
    AssetStatus Status,
    DateTimeOffset UpdatedUtc);

public sealed record AssetPage(IReadOnlyList<AssetListRow> Items, int Total, int Page, int PageSize);

public interface IAssetRepository
{
    Task<AssetPage> SearchAsync(AssetQuery query, CancellationToken ct = default);
    Task<Asset?> GetAsync(Guid id, CancellationToken ct = default);
    Task<byte[]?> AddAsync(Asset asset, CancellationToken ct = default);
    Task<byte[]?> UpdateAsync(Asset asset, byte[]? expectedRowVersion, CancellationToken ct = default);
    Task<bool> InstrumentCodeTakenAsync(string instrumentCode, Guid? exceptAssetId, CancellationToken ct = default);
}

/// <summary>Raised when someone else saved the asset between load and save.</summary>
public sealed class AssetConcurrencyException(Guid id)
    : Exception($"Asset {id} was modified by another session; reload before saving.");

public sealed class AssetRepository(
    ISqlConnectionFactory connections,
    SqlConnectionOptions options,
    ILogger<AssetRepository> logger) : IAssetRepository
{
    private readonly SqlRetryPolicy _retry = new(options.MaxRetries, logger);

    /// <summary>
    /// Full row for a single-asset read. <c>ValuesJson</c> is appended by the caller that needs it.
    /// </summary>
    private const string AssetColumns = """
        Id, FigureCode, TemplateVersion, InstrumentCode, IsinCode, CommercialName, IssuerAccount,
        IssueDate, MaturityDate, Modality, UnderlyingClass, Underlying, Quantity, UnitIssuePrice,
        NotionalAmount, Status, CreatedUtc, CreatedBy, UpdatedUtc, UpdatedBy, RowVersion
        """;

    /// <summary>
    /// The grid columns only. <c>ValuesJson</c> is an <c>nvarchar(max)</c> holding the whole
    /// instance document and is deliberately absent: reading fifty of them to render a list
    /// would dominate the query's I/O and none of it would be displayed.
    /// </summary>
    private const string GridColumns = """
        a.Id, a.FigureCode, f.Name AS FigureName, a.CommercialName, a.InstrumentCode, a.IsinCode,
        a.IssueDate, a.MaturityDate, a.Modality, a.UnderlyingClass, a.Underlying,
        a.NotionalAmount, a.Status, a.UpdatedUtc
        """;

    public Task<AssetPage> SearchAsync(AssetQuery query, CancellationToken ct = default) =>
        _retry.ExecuteAsync("asset.search", async token =>
        {
            var page = Math.Max(1, query.Page);
            var size = Math.Clamp(query.PageSize, 1, 500);

            var where = new StringBuilder("WHERE 1 = 1");
            if (query.ReferenceDate is not null) where.Append(" AND a.IssueDate <= @referenceDate AND @referenceDate <= a.MaturityDate");
            if (!string.IsNullOrWhiteSpace(query.FigureCode)) where.Append(" AND a.FigureCode = @figureCode");
            if (!string.IsNullOrWhiteSpace(query.Modality)) where.Append(" AND a.Modality = @modality");
            if (!string.IsNullOrWhiteSpace(query.Underlying)) where.Append(" AND a.Underlying = @underlying");
            if (query.Status is not null) where.Append(" AND a.Status = @status");
            if (!string.IsNullOrWhiteSpace(query.Search))
                where.Append(" AND (a.CommercialName LIKE @search OR a.InstrumentCode LIKE @search OR a.IsinCode LIKE @search)");

            // Two things keep this to a single round trip: COUNT(*) OVER () carries the unpaged
            // total alongside the page — a separate COUNT would also be free to disagree with it
            // if a row were written between the two — and the figure name is joined here rather
            // than resolved afterwards.
            var sql = $"""
                SELECT {GridColumns}, COUNT(*) OVER () AS TotalCount
                  FROM asset.Asset AS a
                  LEFT JOIN figure.Figure AS f ON f.Code = a.FigureCode
                {where}
                 ORDER BY a.UpdatedUtc DESC, a.Id
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
                """;

            await using var connection = await connections.OpenAsync(token);
            await using var command = new SqlCommand(sql, connection);

            if (query.ReferenceDate is not null) command.Date("@referenceDate", query.ReferenceDate);
            if (!string.IsNullOrWhiteSpace(query.FigureCode)) command.NVarChar("@figureCode", query.FigureCode, 20);
            if (!string.IsNullOrWhiteSpace(query.Modality)) command.NVarChar("@modality", query.Modality, 10);
            if (!string.IsNullOrWhiteSpace(query.Underlying)) command.NVarChar("@underlying", query.Underlying, 60);
            if (query.Status is not null) command.NVarChar("@status", query.Status.Value.ToString(), 20);
            if (!string.IsNullOrWhiteSpace(query.Search)) command.NVarChar("@search", $"%{query.Search.Trim()}%", 200);
            command.Int("@offset", (page - 1) * size);
            command.Int("@pageSize", size);

            await using var reader = await command.ExecuteReaderAsync(token);

            var items = new List<AssetListRow>(size);
            var total = 0;
            while (await reader.ReadAsync(token))
            {
                items.Add(new AssetListRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetNullableString(2),
                    reader.GetString(3),
                    reader.GetNullableString(4),
                    reader.GetNullableString(5),
                    reader.GetDateOnly(6),
                    reader.GetDateOnly(7),
                    reader.GetNullableString(8),
                    reader.GetNullableString(9),
                    reader.GetNullableString(10),
                    reader.GetNullableDecimal(11),
                    reader.GetEnum(12, AssetStatus.Draft),
                    reader.GetDateTimeOffset(13)));
                total = reader.GetInt32(14);
            }

            return new AssetPage(items, total, page, size);
        }, ct);

    public Task<Asset?> GetAsync(Guid id, CancellationToken ct = default) =>
        _retry.ExecuteAsync("asset.get", async token =>
        {
            var sql = $"SELECT {AssetColumns}, ValuesJson, WarningsJson FROM asset.Asset WHERE Id = @id";

            await using var connection = await connections.OpenAsync(token);
            await using var command = new SqlCommand(sql, connection);
            command.UniqueIdentifier("@id", id);

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, token);
            if (!await reader.ReadAsync(token)) return null;

            var asset = ReadAsset(reader, valuesJsonOrdinal: 21);
            asset.WarningsJson = reader.GetNullableString(22);
            return asset;
        }, ct);

    /// <summary>
    /// Inserts and returns the new rowversion in the same statement — an OUTPUT clause instead
    /// of a follow-up SELECT, so a save costs one round trip.
    /// </summary>
    public Task<byte[]?> AddAsync(Asset asset, CancellationToken ct = default) =>
        _retry.ExecuteAsync("asset.insert", async token =>
        {
            const string sql = """
                INSERT INTO asset.Asset
                    (Id, FigureCode, TemplateVersion, InstrumentCode, IsinCode, CommercialName, IssuerAccount,
                     IssueDate, MaturityDate, Modality, UnderlyingClass, Underlying, Quantity, UnitIssuePrice,
                     NotionalAmount, Status, ValuesJson, WarningsJson, CreatedUtc, CreatedBy, UpdatedUtc, UpdatedBy)
                OUTPUT inserted.RowVersion
                VALUES
                    (@id, @figureCode, @templateVersion, @instrumentCode, @isinCode, @commercialName, @issuerAccount,
                     @issueDate, @maturityDate, @modality, @underlyingClass, @underlying, @quantity, @unitIssuePrice,
                     @notionalAmount, @status, @valuesJson, @warningsJson, @createdUtc, @createdBy, @updatedUtc, @updatedBy);
                """;

            await using var connection = await connections.OpenAsync(token);
            await using var command = new SqlCommand(sql, connection);
            BindAsset(command, asset);

            var result = await command.ExecuteScalarAsync(token);
            return result as byte[];
        }, ct);

    /// <summary>
    /// Optimistic concurrency: the update only matches while the rowversion the caller loaded is
    /// still current. No rows affected means someone else saved first, which is a conflict to
    /// report rather than an overwrite to perform.
    /// </summary>
    public Task<byte[]?> UpdateAsync(Asset asset, byte[]? expectedRowVersion, CancellationToken ct = default) =>
        _retry.ExecuteAsync<byte[]?>("asset.update", async token =>
        {
            var sql = $"""
                UPDATE asset.Asset
                   SET FigureCode = @figureCode, TemplateVersion = @templateVersion,
                       InstrumentCode = @instrumentCode, IsinCode = @isinCode,
                       CommercialName = @commercialName, IssuerAccount = @issuerAccount,
                       IssueDate = @issueDate, MaturityDate = @maturityDate, Modality = @modality,
                       UnderlyingClass = @underlyingClass, Underlying = @underlying,
                       Quantity = @quantity, UnitIssuePrice = @unitIssuePrice,
                       NotionalAmount = @notionalAmount, Status = @status,
                       ValuesJson = @valuesJson, WarningsJson = @warningsJson,
                       UpdatedUtc = @updatedUtc, UpdatedBy = @updatedBy
                 OUTPUT inserted.RowVersion
                 WHERE Id = @id {(expectedRowVersion is null ? string.Empty : "AND RowVersion = @expectedRowVersion")};
                """;

            await using var connection = await connections.OpenAsync(token);
            await using var command = new SqlCommand(sql, connection);
            BindAsset(command, asset);
            if (expectedRowVersion is not null) command.RowVersion("@expectedRowVersion", expectedRowVersion);

            var result = await command.ExecuteScalarAsync(token);
            if (result is byte[] rowVersion) return rowVersion;

            throw new AssetConcurrencyException(asset.Id);
        }, ct);

    public Task<bool> InstrumentCodeTakenAsync(string instrumentCode, Guid? exceptAssetId, CancellationToken ct = default) =>
        _retry.ExecuteAsync("asset.instrument_code_taken", async token =>
        {
            const string sql = """
                SELECT TOP (1) 1 FROM asset.Asset
                 WHERE InstrumentCode = @instrumentCode
                   AND (@exceptId IS NULL OR Id <> @exceptId);
                """;

            await using var connection = await connections.OpenAsync(token);
            await using var command = new SqlCommand(sql, connection);
            command.NVarChar("@instrumentCode", instrumentCode, 20);
            command.UniqueIdentifier("@exceptId", exceptAssetId);

            return await command.ExecuteScalarAsync(token) is not null;
        }, ct);

    private static void BindAsset(SqlCommand command, Asset asset)
    {
        command.UniqueIdentifier("@id", asset.Id);
        command.NVarChar("@figureCode", asset.FigureCode, 20);
        command.Int("@templateVersion", asset.TemplateVersion);
        command.NVarChar("@instrumentCode", asset.InstrumentCode, 20);
        command.NVarChar("@isinCode", asset.IsinCode, 12);
        command.NVarChar("@commercialName", asset.CommercialName, 200);
        command.NVarChar("@issuerAccount", asset.IssuerAccount, 20);
        command.Date("@issueDate", asset.IssueDate);
        command.Date("@maturityDate", asset.MaturityDate);
        command.NVarChar("@modality", asset.Modality, 10);
        command.NVarChar("@underlyingClass", asset.UnderlyingClass, 30);
        command.NVarChar("@underlying", asset.Underlying, 60);
        command.BigInt("@quantity", asset.Quantity);
        command.Decimal("@unitIssuePrice", asset.UnitIssuePrice);
        command.Decimal("@notionalAmount", asset.NotionalAmount);
        command.NVarChar("@status", asset.Status.ToString(), 20);
        command.NVarCharMax("@valuesJson", asset.ValuesJson);
        command.NVarCharMax("@warningsJson", asset.WarningsJson);
        command.DateTimeOffset("@createdUtc", asset.CreatedUtc);
        command.NVarChar("@createdBy", asset.CreatedBy, 100);
        command.DateTimeOffset("@updatedUtc", asset.UpdatedUtc);
        command.NVarChar("@updatedBy", asset.UpdatedBy, 100);
    }

    private static Asset ReadAsset(SqlDataReader reader, int? valuesJsonOrdinal) => new()
    {
        Id = reader.GetGuid(0),
        FigureCode = reader.GetString(1),
        TemplateVersion = reader.GetInt32(2),
        InstrumentCode = reader.GetNullableString(3),
        IsinCode = reader.GetNullableString(4),
        CommercialName = reader.GetString(5),
        IssuerAccount = reader.GetNullableString(6),
        IssueDate = reader.GetDateOnly(7),
        MaturityDate = reader.GetDateOnly(8),
        Modality = reader.GetNullableString(9),
        UnderlyingClass = reader.GetNullableString(10),
        Underlying = reader.GetNullableString(11),
        Quantity = reader.GetNullableInt64(12),
        UnitIssuePrice = reader.GetNullableDecimal(13),
        NotionalAmount = reader.GetNullableDecimal(14),
        Status = reader.GetEnum(15, AssetStatus.Draft),
        CreatedUtc = reader.GetDateTimeOffset(16),
        CreatedBy = reader.GetNullableString(17),
        UpdatedUtc = reader.GetDateTimeOffset(18),
        UpdatedBy = reader.GetNullableString(19),
        RowVersion = reader.GetNullableBytes(20),
        ValuesJson = valuesJsonOrdinal is { } ordinal ? reader.GetString(ordinal) : string.Empty
    };
}
