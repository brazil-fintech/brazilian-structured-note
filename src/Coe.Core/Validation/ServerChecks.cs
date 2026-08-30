using Coe.Core.Expressions;
using Coe.Core.Templates;

namespace Coe.Core.Validation;

/// <summary>
/// A check the browser cannot do on its own — anything that needs reference data: the B3
/// business-day calendar, the underlying master, code uniqueness, issuer limits.
/// Templates reference these by id via <see cref="TemplateRule.ServerCheck"/>.
/// </summary>
public interface IServerCheck
{
    string Id { get; }

    /// <summary>True when the check passes, false when it fails, null when it cannot be decided yet.</summary>
    bool? Evaluate(TemplateRule rule, EvaluationContext ctx);
}

public interface IServerCheckRegistry
{
    bool TryGet(string id, out IServerCheck? check);
    IReadOnlyCollection<string> Ids { get; }
}

public sealed class ServerCheckRegistry : IServerCheckRegistry
{
    private readonly Dictionary<string, IServerCheck> _checks;

    public ServerCheckRegistry(IEnumerable<IServerCheck> checks) =>
        _checks = checks.ToDictionary(c => c.Id, StringComparer.Ordinal);

    public static readonly ServerCheckRegistry Empty = new([]);

    public IReadOnlyCollection<string> Ids => _checks.Keys;

    public bool TryGet(string id, out IServerCheck? check)
    {
        var found = _checks.TryGetValue(id, out var value);
        check = value;
        return found;
    }
}

/// <summary>Convenience base that reads a rule argument as an expression target path.</summary>
public abstract class ServerCheckBase : IServerCheck
{
    public abstract string Id { get; }
    public abstract bool? Evaluate(TemplateRule rule, EvaluationContext ctx);

    protected static string? Arg(TemplateRule rule, string name) =>
        rule.Args.TryGetValue(name, out var node) ? Values.AsString(Values.FromJson(node)) : null;

    protected static decimal? ArgNumber(TemplateRule rule, string name) =>
        rule.Args.TryGetValue(name, out var node) ? Values.AsNumber(Values.FromJson(node)) : null;

    /// <summary>
    /// Reads the value the argument points at. Inside a repeating-section rule the path is a
    /// bare column name and resolves against the current row first.
    /// </summary>
    protected static object? Read(TemplateRule rule, EvaluationContext ctx, string argName)
    {
        var path = Arg(rule, argName);
        if (path is null) return null;
        if (ctx.Item is not null && ctx.ResolveItemPath(path) is { } fromItem) return fromItem;
        return ctx.ResolvePath(path);
    }
}
