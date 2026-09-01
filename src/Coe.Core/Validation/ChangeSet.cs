namespace Coe.Core.Validation;

/// <summary>
/// The paths a keystroke touched, prepared once per validation pass.
///
/// Matching a changed path against a rule's declared dependencies is the inner loop of
/// as-you-type validation: it runs for every rule and every field on every request. Doing the
/// index-stripping there — <c>cashflows[2].amount</c> to <c>cashflows[].amount</c> — meant a
/// regex per dependency per rule. Normalizing the handful of changed paths once instead turns
/// that into set lookups.
/// </summary>
public sealed class ChangeSet
{
    /// <summary>Matches everything: used when the caller asked for a full pass.</summary>
    public static readonly ChangeSet All = new();

    private readonly HashSet<string>? _exact;
    private readonly HashSet<string>? _normalized;
    private readonly HashSet<string>? _sections;

    private ChangeSet()
    {
    }

    public ChangeSet(IReadOnlyCollection<string> changedPaths)
    {
        _exact = new HashSet<string>(changedPaths, StringComparer.Ordinal);
        _normalized = new HashSet<string>(StringComparer.Ordinal);
        _sections = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in changedPaths)
        {
            _normalized.Add(Instance.Normalize(path));
            _sections.Add(Instance.SectionOf(path));
        }
    }

    /// <summary>True when no narrowing applies and everything should be evaluated.</summary>
    public bool MatchesEverything => _exact is null;

    /// <summary>True when the exact instance path was changed, or anything beneath it was.</summary>
    public bool Touched(string path)
    {
        if (_exact is null) return true;
        if (_exact.Contains(path)) return true;

        foreach (var changed in _exact)
        {
            if (changed.Length <= path.Length) continue;
            if (!changed.StartsWith(path, StringComparison.Ordinal)) continue;
            var next = changed[path.Length];
            if (next is '.' or '[') return true;
        }
        return false;
    }

    /// <summary>
    /// True when any declared dependency was touched. Dependencies arrive already normalized
    /// from the compiler; a bare section key (from <c>count(cashflows)</c>) matches a change
    /// anywhere inside that section.
    /// </summary>
    public bool Intersects(IReadOnlyList<string> dependencies)
    {
        if (_exact is null) return true;
        if (dependencies.Count == 0) return false;

        for (var i = 0; i < dependencies.Count; i++)
        {
            var dependency = dependencies[i];
            if (_normalized!.Contains(dependency)) return true;

            // A section-level dependency has no '.' in it and covers every row and column.
            if (dependency.IndexOf('.') < 0 && _sections!.Contains(dependency)) return true;
        }
        return false;
    }

    public bool ContainsExact(string path) => _exact is null || _exact.Contains(path);
}
