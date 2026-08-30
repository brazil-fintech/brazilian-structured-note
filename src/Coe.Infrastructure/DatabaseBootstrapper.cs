using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Coe.Infrastructure;

/// <summary>
/// Applies the SQL scripts in <c>db/</c> in name order at startup. Every script is written to
/// be re-runnable, which keeps a fresh database and a long-lived one on the same shape without
/// a migration history table to drift out of sync.
/// </summary>
public sealed partial class DatabaseBootstrapper(
    string connectionString,
    string scriptDirectory,
    ILogger<DatabaseBootstrapper> logger)
{
    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BatchSeparator();

    public async Task ApplyAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(scriptDirectory))
        {
            logger.LogWarning("Script directory {Directory} not found; skipping schema bootstrap", scriptDirectory);
            return;
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

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
}
