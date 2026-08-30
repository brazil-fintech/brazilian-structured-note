using System.Data;
using System.Diagnostics;
using Coe.Core.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Coe.Infrastructure.Data;

/// <summary>
/// Parameter and execution helpers for the ADO.NET data layer.
///
/// Every string parameter is declared with an explicit type <em>and size</em>. Left to infer,
/// SqlClient sizes an <c>nvarchar</c> parameter from the value it happens to hold, so the same
/// query issued with a 6-character name and a 12-character name arrives as two different
/// statements. On a busy server that fills the plan cache with near-duplicates and forces
/// recompiles; fixed sizes keep one plan per query.
/// </summary>
public static class Sql
{
    public static SqlParameter NVarChar(this SqlCommand command, string name, string? value, int size)
    {
        var p = command.Parameters.Add(name, SqlDbType.NVarChar, size);
        p.Value = (object?)value ?? DBNull.Value;
        return p;
    }

    /// <summary>For <c>nvarchar(max)</c> columns, where -1 is the correct declared size.</summary>
    public static SqlParameter NVarCharMax(this SqlCommand command, string name, string? value)
    {
        var p = command.Parameters.Add(name, SqlDbType.NVarChar, -1);
        p.Value = (object?)value ?? DBNull.Value;
        return p;
    }

    public static SqlParameter Int(this SqlCommand command, string name, int? value)
    {
        var p = command.Parameters.Add(name, SqlDbType.Int);
        p.Value = (object?)value ?? DBNull.Value;
        return p;
    }

    public static SqlParameter BigInt(this SqlCommand command, string name, long? value)
    {
        var p = command.Parameters.Add(name, SqlDbType.BigInt);
        p.Value = (object?)value ?? DBNull.Value;
        return p;
    }

    public static SqlParameter Bit(this SqlCommand command, string name, bool value)
    {
        var p = command.Parameters.Add(name, SqlDbType.Bit);
        p.Value = value;
        return p;
    }

    public static SqlParameter Decimal(this SqlCommand command, string name, decimal? value, byte precision = 28, byte scale = 8)
    {
        var p = command.Parameters.Add(name, SqlDbType.Decimal);
        p.Precision = precision;
        p.Scale = scale;
        p.Value = (object?)value ?? DBNull.Value;
        return p;
    }

    public static SqlParameter Date(this SqlCommand command, string name, DateOnly? value)
    {
        var p = command.Parameters.Add(name, SqlDbType.Date);
        p.Value = value is null ? DBNull.Value : value.Value.ToDateTime(TimeOnly.MinValue);
        return p;
    }

    public static SqlParameter DateTimeOffset(this SqlCommand command, string name, DateTimeOffset value)
    {
        var p = command.Parameters.Add(name, SqlDbType.DateTimeOffset);
        p.Value = value;
        return p;
    }

    public static SqlParameter UniqueIdentifier(this SqlCommand command, string name, Guid? value)
    {
        var p = command.Parameters.Add(name, SqlDbType.UniqueIdentifier);
        p.Value = (object?)value ?? DBNull.Value;
        return p;
    }

    public static SqlParameter RowVersion(this SqlCommand command, string name, byte[]? value)
    {
        var p = command.Parameters.Add(name, SqlDbType.Binary, 8);
        p.Value = (object?)value ?? DBNull.Value;
        return p;
    }

    // ----- readers ------------------------------------------------------------------

    public static string? GetNullableString(this SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static int? GetNullableInt32(this SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    public static long? GetNullableInt64(this SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    public static decimal? GetNullableDecimal(this SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);

    public static DateOnly GetDateOnly(this SqlDataReader reader, int ordinal) =>
        DateOnly.FromDateTime(reader.GetDateTime(ordinal));

    public static DateTimeOffset GetDateTimeOffsetValue(this SqlDataReader reader, int ordinal) =>
        reader.GetDateTimeOffset(ordinal);

    public static DateTimeOffset? GetNullableDateTimeOffset(this SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDateTimeOffset(ordinal);

    public static byte[]? GetNullableBytes(this SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var bytes = new byte[8];
        reader.GetBytes(ordinal, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static T GetEnum<T>(this SqlDataReader reader, int ordinal, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(reader.GetString(ordinal), ignoreCase: true, out var parsed) ? parsed : fallback;
}

/// <summary>
/// Retries the transient faults a SQL Server client is expected to absorb — failover, throttling,
/// deadlock victim, connection reset — with exponential backoff and jitter. Anything else is a
/// real error and surfaces immediately rather than being retried into a longer outage.
/// </summary>
public sealed class SqlRetryPolicy(int maxRetries, ILogger logger)
{
    // Documented transient error numbers, plus the deadlock victim (1205) and client timeout (-2).
    private static readonly HashSet<int> Transient =
    [
        -2, 20, 64, 121, 233, 1205, 4060, 4221,
        10053, 10054, 10060, 10928, 10929, 40197, 40501, 40613, 49918, 49919, 49920
    ];

    public async Task<T> ExecuteAsync<T>(string operation, Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var result = await action(ct);
                CoeDiagnostics.SqlCommandDuration.Record(
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    new KeyValuePair<string, object?>("coe.sql.operation", operation));
                return result;
            }
            catch (SqlException ex) when (attempt < maxRetries && IsTransient(ex))
            {
                var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 100 + Random.Shared.Next(0, 100));
                CoeDiagnostics.SqlRetries.Add(1, new KeyValuePair<string, object?>("coe.sql.operation", operation));
                logger.LogWarning(ex,
                    "Transient SQL fault on {Operation} (error {ErrorNumber}); retrying in {Delay} (attempt {Attempt}/{MaxRetries})",
                    operation, ex.Number, delay, attempt + 1, maxRetries);
                await Task.Delay(delay, ct);
            }
        }
    }

    public Task ExecuteAsync(string operation, Func<CancellationToken, Task> action, CancellationToken ct) =>
        ExecuteAsync(operation, async token => { await action(token); return true; }, ct);

    private static bool IsTransient(SqlException ex)
    {
        foreach (SqlError error in ex.Errors)
            if (Transient.Contains(error.Number))
                return true;
        return false;
    }
}
