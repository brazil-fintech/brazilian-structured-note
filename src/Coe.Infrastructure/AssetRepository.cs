using Coe.Core.Assets;
using Microsoft.EntityFrameworkCore;

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

public sealed record AssetPage(IReadOnlyList<Asset> Items, int Total, int Page, int PageSize);

public interface IAssetRepository
{
    Task<AssetPage> SearchAsync(AssetQuery query, CancellationToken ct = default);
    Task<Asset?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Asset asset, CancellationToken ct = default);
    Task UpdateAsync(Asset asset, byte[]? expectedRowVersion, CancellationToken ct = default);
}

/// <summary>Raised when someone else saved the asset between load and save.</summary>
public sealed class AssetConcurrencyException(Guid id)
    : Exception($"Asset {id} was modified by another session; reload before saving.");

public sealed class AssetRepository(CoeDbContext db) : IAssetRepository
{
    public async Task<AssetPage> SearchAsync(AssetQuery query, CancellationToken ct = default)
    {
        var q = db.Assets.AsNoTracking();

        if (query.ReferenceDate is { } reference)
            q = q.Where(a => a.IssueDate <= reference && reference <= a.MaturityDate);

        if (!string.IsNullOrWhiteSpace(query.FigureCode)) q = q.Where(a => a.FigureCode == query.FigureCode);
        if (!string.IsNullOrWhiteSpace(query.Modality)) q = q.Where(a => a.Modality == query.Modality);
        if (!string.IsNullOrWhiteSpace(query.Underlying)) q = q.Where(a => a.Underlying == query.Underlying);
        if (query.Status is { } status) q = q.Where(a => a.Status == status);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            q = q.Where(a =>
                EF.Functions.Like(a.CommercialName, term) ||
                (a.InstrumentCode != null && EF.Functions.Like(a.InstrumentCode, term)) ||
                (a.IsinCode != null && EF.Functions.Like(a.IsinCode, term)));
        }

        var total = await q.CountAsync(ct);
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 500);

        var items = await q
            .OrderByDescending(a => a.UpdatedUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return new AssetPage(items, total, page, size);
    }

    public async Task<Asset?> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task AddAsync(Asset asset, CancellationToken ct = default)
    {
        db.Assets.Add(asset);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Asset asset, byte[]? expectedRowVersion, CancellationToken ct = default)
    {
        var tracked = await db.Assets.FirstOrDefaultAsync(a => a.Id == asset.Id, ct)
                      ?? throw new AssetConcurrencyException(asset.Id);

        if (expectedRowVersion is not null)
            db.Entry(tracked).Property(a => a.RowVersion).OriginalValue = expectedRowVersion;

        db.Entry(tracked).CurrentValues.SetValues(asset);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AssetConcurrencyException(asset.Id);
        }
    }
}
