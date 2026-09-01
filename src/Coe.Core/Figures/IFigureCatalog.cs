namespace Coe.Core.Figures;

/// <summary>Outcome of one pass of the ingestion worker over the domain files.</summary>
public sealed class IngestionRun
{
    public long Id { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public int FilesScanned { get; set; }
    public int FiguresCreated { get; set; }
    public int TemplatesCreated { get; set; }
    public int FiguresQuarantined { get; set; }
    public string Status { get; set; } = "Running";

    /// <summary>Compilation errors, one per line, kept so a failing figure can be diagnosed from the database.</summary>
    public string? Details { get; set; }
}

/// <summary>
/// Storage for figures and their compiled templates. Kept as an interface so the compiler and
/// the ingestion loop can be exercised without a SQL Server.
/// </summary>
public interface IFigureCatalog
{
    Task<Figure?> GetAsync(string code, CancellationToken ct = default);

    Task<IReadOnlyList<Figure>> ListAsync(bool enabledOnly = true, CancellationToken ct = default);

    /// <summary>
    /// Every figure B3 publishes, joined with the platform's own — so a caller can tell a figure
    /// that is missing from the picker apart from one nobody has modelled yet.
    /// </summary>
    Task<IReadOnlyList<CatalogueFigure>> ListCatalogueAsync(CancellationToken ct = default);

    /// <summary>Inserts the figure, or updates the mutable columns of an existing one.</summary>
    Task UpsertAsync(Figure figure, CancellationToken ct = default);

    /// <summary>Highest template version issued for the figure, or 0.</summary>
    Task<int> LatestTemplateVersionAsync(string code, CancellationToken ct = default);

    /// <summary>Stores a new version and makes it the active one, deactivating the previous.</summary>
    Task AddTemplateVersionAsync(FigureTemplateRecord record, CancellationToken ct = default);

    Task<FigureTemplateRecord?> GetActiveTemplateAsync(string code, CancellationToken ct = default);

    Task<FigureTemplateRecord?> GetTemplateAsync(string code, int version, CancellationToken ct = default);

    Task RecordRunAsync(IngestionRun run, CancellationToken ct = default);
}
