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
            const string sql = """
                SELECT Code, Name, AssetClass
                  FROM ref.Underlying
                 WHERE IsActive = 1
                   AND (@assetClass IS NULL OR AssetClass = @assetClass)
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
}
