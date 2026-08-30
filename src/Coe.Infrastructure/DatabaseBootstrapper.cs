using System.Text.RegularExpressions;
using Coe.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Coe.Infrastructure;

/// <summary>
/// Applies the SQL scripts in <c>db/</c> in name order at startup. Every script is written to
/// be re-runnable, which keeps a fresh database and a long-lived one on the same shape without
/// a migration history table to drift out of sync.
/// </summary>
public sealed partial class DatabaseBootstrapper(
    ISqlConnectionFactory connections,
    SqlConnectionOptions options,
    ILogger<DatabaseBootstrapper> logger)
{
    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BatchSeparator();

    public async Task ApplyAsync(CancellationToken ct = default)
    {
        if (options.CreateDatabaseIfMissing) await CreateDatabaseAsync(ct);

        var scriptDirectory = options.ScriptDirectory;
        if (!Directory.Exists(scriptDirectory))
        {
            logger.LogWarning("Script directory {Directory} not found; skipping schema bootstrap", scriptDirectory);
            return;
        }

        await using var connection = await connections.OpenAsync(ct);

        foreach (var path in Directory.EnumerateFiles(scriptDirectory, "*.sql").Order(StringComparer.Ordinal))
        {
            var sql = await File.ReadAllTextAsync(path, ct);
            var batches = BatchSeparator().Split(sql).Where(b => !string.IsNullOrWhiteSpace(b));

            foreach (var batch in batches)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = batch;
                command.CommandTimeout = 120;
                await command.ExecuteNonQueryAsync(ct);
            }

            logger.LogInformation("Applied schema script {Script}", Path.GetFileName(path));
        }
    }

    /// <summary>
    /// Connects to <c>master</c> to create the database when it does not exist yet. The database
    /// name is taken from the parsed connection string and quoted, never concatenated from input.
    /// </summary>
    private async Task CreateDatabaseAsync(CancellationToken ct)
    {
        var master = new SqlConnectionStringBuilder(connections.ConnectionString) { InitialCatalog = "master" };

        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync(ct);

        // EXEC() takes a literal or a variable, not an expression, so the statement is built
        // into one first. QUOTENAME escapes the identifier; the name itself is a parameter.
        const string sql = """
            IF DB_ID(@name) IS NULL
            BEGIN
                DECLARE @statement nvarchar(300) = N'CREATE DATABASE ' + QUOTENAME(@name);
                EXEC sp_executesql @statement;
            END
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@name", System.Data.SqlDbType.NVarChar, 128).Value = connections.DatabaseName;
        await command.ExecuteNonQueryAsync(ct);

        logger.LogInformation("Ensured database {Database} exists", connections.DatabaseName);
    }
}
