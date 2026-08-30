using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Coe.Infrastructure.Data;

public interface ISqlConnectionFactory
{
    /// <summary>Opens a pooled connection. The caller disposes it, returning it to the pool.</summary>
    Task<SqlConnection> OpenAsync(CancellationToken ct = default);

    string ConnectionString { get; }
    string DatabaseName { get; }
}

public sealed class SqlConnectionOptions
{
    public const string SectionName = "Database";

    /// <summary>Pool floor. Keeping a few connections warm avoids paying TLS + login on a cold morning.</summary>
    public int MinPoolSize { get; set; } = 4;

    /// <summary>Pool ceiling. Above this, requests queue rather than piling connections onto the server.</summary>
    public int MaxPoolSize { get; set; } = 100;

    /// <summary>Default statement timeout, in seconds.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    public int MaxRetries { get; set; } = 4;

    public bool ApplyScriptsOnStartup { get; set; } = true;

    /// <summary>
    /// Creates the database when it is missing. Off by default: a deployed environment should
    /// have its database provisioned, and silently creating an empty one on a mistyped
    /// connection string hides the mistake. Development turns it on so a fresh container works.
    /// </summary>
    public bool CreateDatabaseIfMissing { get; set; }

    public string ScriptDirectory { get; set; } = "db";
}

/// <summary>
/// Hands out pooled <see cref="SqlConnection"/>s over one normalised connection string.
///
/// The settings applied here are the ones that matter under load and are easy to forget in a
/// deployed configuration: an application name so the connections are identifiable in
/// <c>sys.dm_exec_sessions</c>, an explicit pool range, and MARS left off so a forgotten open
/// reader fails loudly instead of quietly serialising work onto one connection.
/// </summary>
public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    public SqlConnectionFactory(string connectionString, SqlConnectionOptions options, ILogger<SqlConnectionFactory> logger)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = "coe-platform",
            Pooling = true,
            MinPoolSize = options.MinPoolSize,
            MaxPoolSize = options.MaxPoolSize,
            CommandTimeout = options.CommandTimeoutSeconds,
            MultipleActiveResultSets = false
        };

        ConnectionString = builder.ConnectionString;
        DatabaseName = builder.InitialCatalog;

        logger.LogInformation(
            "SQL connection factory ready for {Database} on {DataSource} (pool {MinPoolSize}-{MaxPoolSize})",
            builder.InitialCatalog, builder.DataSource, builder.MinPoolSize, builder.MaxPoolSize);
    }

    public string ConnectionString { get; }
    public string DatabaseName { get; }

    public async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqlConnection(ConnectionString);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
