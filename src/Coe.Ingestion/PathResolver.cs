using Coe.Core.Expressions;

namespace Coe.Ingestion;

/// <summary>
/// Rewrites the bare names authors use (<c>cap</c>, <c>issueDate</c>) into absolute instance
/// paths (<c>payoff.cap</c>, <c>common.issueDate</c>), and turns references to a repeating
/// section's own columns into <see cref="ItemExpr"/> nodes.
///
/// Resolution order for a name written inside section <c>S</c>:
/// <list type="number">
///   <item>a column of <c>S</c> when <c>S</c> repeats — becomes <c>@.name</c>;</item>
///   <item>a field of <c>S</c>;</item>
///   <item>a field anywhere in the template, provided the name is unique;</item>
/// </list>
/// anything else is a compile error, so a renamed attribute cannot silently turn a rule
/// into a no-op.
/// </summary>
public sealed class PathResolver
{
    private readonly Dictionary<string, string> _byAbsolutePath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _byName = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sectionKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _repeatingSections = new(StringComparer.Ordinal);

    public void AddSection(string key, bool repeating)
    {
        _sectionKeys.Add(key);
        if (repeating) _repeatingSections.Add(key);
    }

    public void AddField(string sectionKey, string fieldKey, bool repeating)
    {
        var path = repeating ? $"{sectionKey}[].{fieldKey}" : $"{sectionKey}.{fieldKey}";
        _byAbsolutePath[path] = path;
        if (!_byName.TryGetValue(fieldKey, out var list)) _byName[fieldKey] = list = [];
        list.Add(path);
    }

    public bool IsSection(string key) => _sectionKeys.Contains(key);
    public bool KnowsPath(string path) => _byAbsolutePath.ContainsKey(path);

    /// <summary>Resolves a rule target (a path, possibly bare) written inside <paramref name="scope"/>.</summary>
    public string ResolveTarget(string target, string scope, IList<string> errors)
    {
        // A rule about the collection as a whole (row count, weights summing to 100) lands on the section.
        if (_sectionKeys.Contains(target) && !_byName.ContainsKey(target)) return target;

        var resolved = ResolveName(target, scope, out var itemScoped, out var error);
        if (error is not null) { errors.Add(error); return target; }
        return itemScoped ? $"{scope}[].{resolved}" : resolved!;
    }

    public Expr Rewrite(Expr expr, string scope, IList<string> errors) => expr switch
    {
        FieldExpr f => RewriteField(f, scope, errors),
        ItemExpr i => scope.Length > 0 && _repeatingSections.Contains(scope) && !_byAbsolutePath.ContainsKey($"{scope}[].{i.P}")
            ? Fail(i, $"'@.{i.P}' is not a column of section '{scope}'.", errors)
            : i,
        OpExpr o => new OpExpr(o.O, o.A.Select(a => Rewrite(a, scope, errors)).ToList()),
        FnExpr fn => RewriteCall(fn, scope, errors),
        _ => expr
    };

    private Expr RewriteCall(FnExpr fn, string scope, IList<string> errors)
    {
        // any/all/sum/isDistinct take a collection then an item-scoped expression: the second
        // argument must resolve against the collection's section, not the enclosing one.
        if (Functions.ItemScoped.Contains(fn.N) && fn.A.Count >= 2 && fn.A[0] is FieldExpr collection)
        {
            var sectionKey = collection.P;
            var args = new List<Expr> { new FieldExpr(sectionKey) };
            for (var i = 1; i < fn.A.Count; i++)
                args.Add(Rewrite(fn.A[i], sectionKey, errors));

            if (!_sectionKeys.Contains(sectionKey))
                errors.Add($"'{fn.N}' expects a repeating section as its first argument; '{sectionKey}' is not one.");

            return new FnExpr(fn.N, args);
        }

        return new FnExpr(fn.N, fn.A.Select(a => Rewrite(a, scope, errors)).ToList());
    }

    private Expr RewriteField(FieldExpr f, string scope, IList<string> errors)
    {
        // A reference to a whole repeating section (count(cashflows)) stays as-is.
        if (_sectionKeys.Contains(f.P) && !_byName.ContainsKey(f.P)) return f;

        var resolved = ResolveName(f.P, scope, out var itemScoped, out var error);
        if (error is not null) return Fail(f, error, errors);
        return itemScoped ? new ItemExpr(resolved!) : new FieldExpr(resolved!);
    }

    private string? ResolveName(string name, string scope, out bool itemScoped, out string? error)
    {
        itemScoped = false;
        error = null;

        if (_byAbsolutePath.ContainsKey(name))
        {
            // Inside its own repeating section, an explicit column path is item-scoped.
            if (scope.Length > 0 && name.StartsWith(scope + "[].", StringComparison.Ordinal))
            {
                itemScoped = true;
                return name[(scope.Length + 3)..];
            }
            return name;
        }

        if (name.Contains('.', StringComparison.Ordinal))
        {
            error = $"Unknown attribute '{name}'.";
            return null;
        }

        if (scope.Length > 0)
        {
            var scoped = _repeatingSections.Contains(scope) ? $"{scope}[].{name}" : $"{scope}.{name}";
            if (_byAbsolutePath.ContainsKey(scoped))
            {
                itemScoped = _repeatingSections.Contains(scope);
                return itemScoped ? name : scoped;
            }
        }

        if (_byName.TryGetValue(name, out var candidates))
        {
            if (candidates.Count == 1) return candidates[0];
            error = $"'{name}' is ambiguous — it exists as {string.Join(", ", candidates)}. Qualify it with its section.";
            return null;
        }

        error = $"Unknown attribute '{name}'.";
        return null;
    }

    private static Expr Fail(Expr original, string error, IList<string> errors)
    {
        errors.Add(error);
        return original;
    }
}
