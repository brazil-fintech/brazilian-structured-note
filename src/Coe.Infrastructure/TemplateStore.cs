using System.Collections.Concurrent;
using Coe.Core.Diagnostics;
using Coe.Core.Figures;
using Coe.Core.Templates;

namespace Coe.Infrastructure;

/// <summary>
/// Reads compiled templates and keeps the deserialized form in memory.
///
/// This sits on the per-keystroke path: every validate call needs the template, and a miss costs
/// a query plus parsing a document that can run to hundreds of kilobytes. A specific version is
/// immutable once published, so it is cached for the life of the process; only the pointer to
/// "which version is active" is re-read, on a short TTL, which is what lets a template the worker
/// publishes take effect without a restart.
/// </summary>
public interface ITemplateStore
{
    Task<FigureTemplate?> GetActiveAsync(string figureCode, CancellationToken ct = default);
    Task<FigureTemplate?> GetAsync(string figureCode, int version, CancellationToken ct = default);
    void Invalidate(string figureCode);
}

public sealed class TemplateStore(IFigureCatalog catalog) : ITemplateStore
{
    private static readonly TimeSpan ActiveTtl = TimeSpan.FromSeconds(30);

    // Immutable versions, keyed by "code:version". Never invalidated: a published version's
    // content cannot change, so a hit is always correct.
    private static readonly ConcurrentDictionary<string, FigureTemplate> Versions = new(StringComparer.Ordinal);

    // Which version is currently active, re-read after the TTL.
    private static readonly ConcurrentDictionary<string, ActiveEntry> Active = new(StringComparer.Ordinal);

    private sealed record ActiveEntry(FigureTemplate? Template, DateTimeOffset LoadedUtc)
    {
        public bool IsFresh => DateTimeOffset.UtcNow - LoadedUtc < ActiveTtl;
    }

    public async Task<FigureTemplate?> GetActiveAsync(string figureCode, CancellationToken ct = default)
    {
        if (Active.TryGetValue(figureCode, out var cached) && cached.IsFresh)
        {
            CountLookup("hit");
            return cached.Template;
        }

        CountLookup("miss");
        var record = await catalog.GetActiveTemplateAsync(figureCode, ct);
        var template = record is null ? null : Materialize(record);
        Active[figureCode] = new ActiveEntry(template, DateTimeOffset.UtcNow);
        return template;
    }

    public async Task<FigureTemplate?> GetAsync(string figureCode, int version, CancellationToken ct = default)
    {
        var key = Key(figureCode, version);
        if (Versions.TryGetValue(key, out var cached))
        {
            CountLookup("hit");
            return cached;
        }

        CountLookup("miss");
        var record = await catalog.GetTemplateAsync(figureCode, version, ct);
        return record is null ? null : Materialize(record);
    }

    /// <summary>Drops the active-version pointer, so the next read picks up a freshly published template.</summary>
    public void Invalidate(string figureCode) => Active.TryRemove(figureCode, out _);

    private static FigureTemplate Materialize(FigureTemplateRecord record)
    {
        var key = Key(record.FigureCode, record.Version);
        // Parsing is the expensive half of a miss, so two racing callers should not both do it.
        return Versions.GetOrAdd(key, _ => TemplateJson.Deserialize(record.TemplateJson));
    }

    private static string Key(string figureCode, int version) => $"{figureCode}:{version}";

    private static void CountLookup(string result) =>
        CoeDiagnostics.TemplateCacheLookups.Add(1, new KeyValuePair<string, object?>("coe.cache.result", result));
}
