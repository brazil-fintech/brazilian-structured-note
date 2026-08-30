using Coe.Core.Figures;
using Microsoft.EntityFrameworkCore;

namespace Coe.Infrastructure;

/// <inheritdoc cref="IFigureCatalog"/>
public sealed class FigureCatalog(CoeDbContext db) : IFigureCatalog
{
    public async Task<Figure?> GetAsync(string code, CancellationToken ct = default) =>
        await db.Figures.FirstOrDefaultAsync(f => f.Code == code, ct);

    public async Task<IReadOnlyList<Figure>> ListAsync(bool enabledOnly = true, CancellationToken ct = default)
    {
        var query = db.Figures.AsNoTracking();
        if (enabledOnly) query = query.Where(f => f.Status == FigureStatus.Enabled);
        return await query.OrderBy(f => f.Code).ToListAsync(ct);
    }

    public async Task UpsertAsync(Figure figure, CancellationToken ct = default)
    {
        var tracked = await db.Figures.FirstOrDefaultAsync(f => f.Code == figure.Code, ct);
        if (tracked is null)
        {
            db.Figures.Add(figure);
        }
        else if (!ReferenceEquals(tracked, figure))
        {
            db.Entry(tracked).CurrentValues.SetValues(figure);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> LatestTemplateVersionAsync(string code, CancellationToken ct = default) =>
        await db.FigureTemplates
            .Where(t => t.FigureCode == code)
            .Select(t => (int?)t.Version)
            .MaxAsync(ct) ?? 0;

    public async Task AddTemplateVersionAsync(FigureTemplateRecord record, CancellationToken ct = default)
    {
        // Only one version may be active at a time; the unique filtered index enforces it, so
        // the previous one has to be stood down inside the same transaction.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var previous = await db.FigureTemplates
            .Where(t => t.FigureCode == record.FigureCode && t.IsActive)
            .ToListAsync(ct);
        foreach (var t in previous) t.IsActive = false;
        await db.SaveChangesAsync(ct);

        db.FigureTemplates.Add(record);
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }

    public async Task<FigureTemplateRecord?> GetActiveTemplateAsync(string code, CancellationToken ct = default) =>
        await db.FigureTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.FigureCode == code && t.IsActive, ct);

    public async Task<FigureTemplateRecord?> GetTemplateAsync(string code, int version, CancellationToken ct = default) =>
        await db.FigureTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.FigureCode == code && t.Version == version, ct);

    public async Task RecordRunAsync(IngestionRun run, CancellationToken ct = default)
    {
        db.IngestionRuns.Add(run);
        await db.SaveChangesAsync(ct);
    }
}
