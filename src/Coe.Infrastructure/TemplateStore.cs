using Coe.Core.Figures;
using Coe.Core.Templates;
using Microsoft.Extensions.Caching.Memory;

namespace Coe.Infrastructure;

/// <summary>
/// Reads compiled templates and keeps the deserialized form in memory. Every keystroke in the
/// booking screen can reach the validate endpoint, so parsing the template JSON per request
/// would dominate the cost of validating.
///
/// Cache keys carry the version, and the active version is re-read on a short TTL, so a
/// template the worker publishes becomes visible without a restart.
/// </summary>
public interface ITemplateStore
{
    Task<FigureTemplate?> GetActiveAsync(string figureCode, CancellationToken ct = default);
    Task<FigureTemplate?> GetAsync(string figureCode, int version, CancellationToken ct = default);
    void Invalidate(string figureCode);
}

public sealed class TemplateStore(IFigureCatalog catalog, IMemoryCache cache) : ITemplateStore
{
    private static readonly TimeSpan ActiveTtl = TimeSpan.FromSeconds(30);

    public async Task<FigureTemplate?> GetActiveAsync(string figureCode, CancellationToken ct = default)
    {
        var key = $"template:active:{figureCode}";
        if (cache.TryGetValue(key, out FigureTemplate? cached)) return cached;

        var record = await catalog.GetActiveTemplateAsync(figureCode, ct);
        var template = record is null ? null : TemplateJson.Deserialize(record.TemplateJson);
        cache.Set(key, template, ActiveTtl);
        return template;
    }

    public async Task<FigureTemplate?> GetAsync(string figureCode, int version, CancellationToken ct = default)
    {
        var key = $"template:{figureCode}:{version}";
        if (cache.TryGetValue(key, out FigureTemplate? cached)) return cached;

        var record = await catalog.GetTemplateAsync(figureCode, version, ct);
        var template = record is null ? null : TemplateJson.Deserialize(record.TemplateJson);

        // A specific version is immutable once published, so it can be cached indefinitely.
        if (template is not null) cache.Set(key, template);
        return template;
    }

    public void Invalidate(string figureCode) => cache.Remove($"template:active:{figureCode}");
}
