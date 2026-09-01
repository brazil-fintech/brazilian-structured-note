using Coe.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Coe.Infrastructure;

public sealed record UnderlyingRef(string Code, string Name, string AssetClass);

/// <summary>
/// Lists that fields reference by <c>optionSource</c> instead of inlining them in a template —
/// the underlying master and anything else that changes on its own cadence.
/// </summary>
public interface IReferenceDataRepository
{
    Task<IReadOnlyList<UnderlyingRef>> UnderlyingsAsync(string? assetClass, CancellationToken ct = default);

    /// <summary>True when B3's master lists the code, optionally within a given class.</summary>
    Task<bool> UnderlyingExistsAsync(string code, string? assetClass, CancellationToken ct = default);
}

public sealed class ReferenceDataRepository(
    ISqlConnectionFactory connections,
    SqlConnectionOptions options,
    ILogger<ReferenceDataRepository> logger) : IReferenceDataRepository
{
    private readonly SqlRetryPolicy _retry = new(options.MaxRetries, logger);

    public Task<IReadOnlyList<UnderlyingRef>> UnderlyingsAsync(string? assetClass, CancellationToken ct = default) =>
        _retry.ExecuteAsync<IReadOnlyList<UnderlyingRef>>("reference.underlyings", async token =>
        {
            // B3 lists an asset once per valuation index; the picker wants the codes, so the
            // variants collapse here rather than in the browser.
            const string sql = """
                SELECT Code, MIN(ValuationIndex) AS ValuationIndex, AssetClass
                  FROM ref.Underlying
                 WHERE IsActive = 1
                   AND (@assetClass IS NULL OR AssetClass = @assetClass)
                 GROUP BY AssetClass, Code
                 ORDER BY AssetClass, Code;
                """;

            await using var connection = await connections.OpenAsync(token);
            await using var command = new SqlCommand(sql, connection);
            command.NVarChar("@assetClass", assetClass, 30);

            await using var reader = await command.ExecuteReaderAsync(token);
            var items = new List<UnderlyingRef>();
            while (await reader.ReadAsync(token))
                items.Add(new UnderlyingRef(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            return items;
        }, ct);

    public Task<bool> UnderlyingExistsAsync(string code, string? assetClass, CancellationToken ct = default) =>
        _retry.ExecuteAsync("reference.underlying_exists", async token =>
        {
            const string sql = """
                SELECT TOP (1) 1
                  FROM ref.Underlying
                 WHERE Code = @code
                   AND IsActive = 1
                   AND (@assetClass IS NULL OR AssetClass = @assetClass);
                """;

            await using var connection = await connections.OpenAsync(token);
            await using var command = new SqlCommand(sql, connection);
            command.NVarChar("@code", code, 60);
            command.NVarChar("@assetClass", assetClass, 40);

            return await command.ExecuteScalarAsync(token) is not null;
        }, ct);
}
