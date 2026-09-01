using System.Globalization;
using System.Text.Json.Nodes;

namespace Coe.Core.Expressions;

/// <summary>
/// The values an expression is evaluated against: the instance being edited, the current
/// item when inside a repeating section, and host-supplied variables.
/// </summary>
public sealed class EvaluationContext
{
    private readonly JsonObject _root;
    private readonly IReadOnlyDictionary<string, object?> _vars;

    public EvaluationContext(JsonObject root, IReadOnlyDictionary<string, object?>? variables = null, JsonObject? item = null)
    {
        _root = root;
        _vars = variables ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        Item = item;
    }

    /// <summary>The current item of a repeating section, when one is in scope.</summary>
    public JsonObject? Item { get; }

    public EvaluationContext WithItem(JsonObject? item) => new(_root, _vars, item);

    /// <summary>Resolves a dotted path against the instance root. Missing segments yield null.</summary>
    public object? ResolvePath(string path) => Values.FromJson(Navigate(_root, path));

    /// <summary>Resolves a dotted path against the current repeating item, falling back to the root.</summary>
    public object? ResolveItemPath(string path)
    {
        if (Item is null) return null;
        var node = Navigate(Item, path);
        return Values.FromJson(node);
    }

    public object? ResolveVariable(string name)
    {
        if (_vars.TryGetValue(name, out var v)) return v;
        return name switch
        {
            "today" => DateOnly.FromDateTime(DateTime.UtcNow),
            _ => null
        };
    }

    internal static JsonNode? Navigate(JsonNode? from, string path)
    {
        var node = from;
        foreach (var segment in path.Split('.'))
        {
            if (node is null) return null;
            if (node is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(segment, out node)) return null;
            }
            else if (node is JsonArray arr && int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            {
                node = i >= 0 && i < arr.Count ? arr[i] : null;
            }
            else
            {
                return null;
            }
        }
        return node;
    }
}
