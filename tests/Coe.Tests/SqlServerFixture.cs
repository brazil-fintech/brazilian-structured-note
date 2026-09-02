using Coe.Core.Figures;
using Coe.Infrastructure;
using Coe.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Coe.Tests;

/// <summary>
/// Runs a test only when a SQL Server is reachable, so the suite stays runnable on a laptop with
/// nothing installed. Point <c>COE_TEST_SQL</c> at an instance to turn these on:
///
/// <code>
/// docker compose up -d mssql
/// COE_TEST_SQL="Server=localhost,1433;User Id=sa;Password=Your_password123;TrustServerCertificate=True" dotnet test
/// </code>
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(SqlServerFixture.BaseConnectionString))
            Skip = "Set COE_TEST_SQL to a SQL Server connection string to run the database tests.";
    }
}

/// <summary>
/// Creates a throwaway database, applies the repository's own scripts to it, and drops it
/// afterwards — so these tests exercise the real schema in <c>db/</c> rather than a hand-built
/// approximation of it.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public static string? BaseConnectionString => Environment.GetEnvironmentVariable("COE_TEST_SQL");

    private string _databaseName = null!;

    public ISqlConnectionFactory Connections { get; private set; } = null!;
    public SqlConnectionOptions Options { get; private set; } = null!;
    public FigureCatalog Catalog { get; private set; } = null!;
    public AssetRepository Assets { get; private set; } = null!;
    public ClearingFileRepository ClearingFiles { get; private set; } = null!;
    public BusinessCalendar Calendar { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(BaseConnectionString)) return;

        _databaseName = $"CoeTest_{Guid.NewGuid():N}";

        var builder = new SqlConnectionStringBuilder(BaseConnectionString) { InitialCatalog = _databaseName };
        Options = new SqlConnectionOptions
        {
            CreateDatabaseIfMissing = true,
            ScriptDirectory = Path.Combine(RepositoryRoot(), "db"),
            MinPoolSize = 1,
            MaxPoolSize = 10
        };

        Connections = new SqlConnectionFactory(builder.ConnectionString, Options, NullLogger<SqlConnectionFactory>.Instance);
        await new DatabaseBootstrapper(Connections, Options, NullLogger<DatabaseBootstrapper>.Instance).ApplyAsync();

        Catalog = new FigureCatalog(Connections, Options, NullLogger<FigureCatalog>.Instance);
        Assets = new AssetRepository(Connections, Options, NullLogger<AssetRepository>.Instance);
        ClearingFiles = new ClearingFileRepository(Connections, Options, NullLogger<ClearingFileRepository>.Instance);
        Calendar = new BusinessCalendar(Connections, Options, NullLogger<BusinessCalendar>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(BaseConnectionString) || _databaseName is null) return;

        // The pool holds open connections to the database, which would block the drop.
        SqlConnection.ClearAllPools();

        var master = new SqlConnectionStringBuilder(BaseConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("""
            IF DB_ID(@name) IS NOT NULL
            BEGIN
                DECLARE @statement nvarchar(300) = N'ALTER DATABASE ' + QUOTENAME(@name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(@name);
                EXEC sp_executesql @statement;
            END
            """, connection);
        command.Parameters.Add("@name", System.Data.SqlDbType.NVarChar, 128).Value = _databaseName;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>A figure row the template and asset tests can hang off.</summary>
    public async Task<Figure> SeedFigureAsync(string code = "COE001005")
    {
        var figure = new Figure
        {
            Code = code,
            Name = "Call Spread",
            CommercialName = "Call spread (trava de alta)",
            Modalities = "VNP",
            Status = FigureStatus.Enabled,
            FirstSeenUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow,
            EnabledUtc = DateTimeOffset.UtcNow
        };
        await Catalog.UpsertAsync(figure);
        return figure;
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "db")) &&
                Directory.Exists(Path.Combine(dir.FullName, "domain")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root from the test output path.");
    }
}

[CollectionDefinition("sqlserver")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
