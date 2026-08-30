using System.Text.Json.Nodes;
using Coe.Core.Expressions;
using Coe.Core.Templates;

namespace Coe.Core.Validation;

/// <summary>
/// Recomputes every derived attribute in place. The client does the same as the user types so
/// the value is visible immediately; the API redoes it before validating and saving, so what
/// lands in the database always agrees with its inputs even if the payload said otherwise.
/// </summary>
public static class ComputedFields
{
    public static void Apply(FigureTemplate template, JsonObject values, IReadOnlyDictionary<string, object?>? variables = null)
    {
        var ctx = new EvaluationContext(values, variables);

        foreach (var section in template.Sections)
        {
            if (section.Repeating)
            {
                if (values[section.Key] is not JsonArray rows) continue;
                foreach (var row in rows)
                {
                    if (row is not JsonObject item) continue;
                    var scoped = ctx.WithItem(item);
                    foreach (var field in section.ItemFields)
                        if (field.Computed is { } expr)
                            item[field.Key] = ToNode(ExpressionEvaluator.Evaluate(expr, scoped));
                }
                continue;
            }

            foreach (var field in section.Fields)
            {
                if (field.Computed is not { } expr) continue;
                var target = values[section.Key] as JsonObject;
                if (target is null)
                {
                    target = new JsonObject();
                    values[section.Key] = target;
                }
                target[field.Key] = ToNode(ExpressionEvaluator.Evaluate(expr, ctx));
            }
        }
    }

    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        bool b => JsonValue.Create(b),
        decimal d => JsonValue.Create(d),
        DateOnly date => JsonValue.Create(date.ToString("yyyy-MM-dd")),
        string s => JsonValue.Create(s),
        JsonNode node => node.DeepClone(),
        _ => JsonValue.Create(value.ToString())
    };
}
