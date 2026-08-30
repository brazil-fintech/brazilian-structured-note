using System.Globalization;
using System.Text.Json.Nodes;
using Coe.Core.Expressions;
using Coe.Core.Templates;

namespace Coe.Core.Validation;

/// <summary>
/// Evaluates a template against an instance. This is the authority: the React client runs the
/// same rules for instant feedback, but every save goes through <see cref="ValidationScope.Submit"/>
/// here before anything is written.
/// </summary>
public sealed class ValidationEngine(IServerCheckRegistry? serverChecks = null)
{
    private readonly IServerCheckRegistry _serverChecks = serverChecks ?? ServerCheckRegistry.Empty;

    public ValidationResult Validate(
        FigureTemplate template,
        JsonObject values,
        ValidationScope scope,
        IReadOnlyCollection<string>? changedPaths = null,
        IReadOnlyDictionary<string, object?>? variables = null,
        string culture = "pt-BR")
    {
        var ctx = new EvaluationContext(values, variables);
        var messages = new List<ValidationMessage>();
        var evaluated = new List<string>();
        var changed = changedPaths is null ? null : new HashSet<string>(changedPaths, StringComparer.Ordinal);

        foreach (var section in template.Sections)
        {
            if (!ExpressionEvaluator.EvaluateAsBool(section.VisibleWhen, ctx)) continue;

            if (section.Repeating)
                ValidateRepeatingSection(section, values, ctx, scope, changed, culture, messages, evaluated);
            else
                ValidateFields(section, section.Fields, ctx, prefix: null, scope, changed, culture, messages, evaluated);
        }

        foreach (var rule in template.Rules)
        {
            // Rules marked client-only are advisory hints for the form; re-emitting them here
            // would put messages in the save response that the API never actually checked.
            if (!rule.RunsOnServer) continue;
            if (!AppliesToScope(rule, scope)) continue;
            if (rule.ForEachSection is { } sectionKey)
                EvaluateRowRule(template, rule, sectionKey, values, ctx, scope, changed, culture, messages);
            else
                EvaluateRule(rule, ctx, prefix: null, scope, changed, culture, messages);
        }

        return new ValidationResult
        {
            Messages = Deduplicate(messages),
            EvaluatedPaths = evaluated
        };
    }

    // ----- fields -------------------------------------------------------------------

    private void ValidateRepeatingSection(
        TemplateSection section,
        JsonObject values,
        EvaluationContext ctx,
        ValidationScope scope,
        HashSet<string>? changed,
        string culture,
        List<ValidationMessage> messages,
        List<string> evaluated)
    {
        var array = EvaluationContext.Navigate(values, section.Key) as JsonArray;
        var count = array?.Count ?? 0;

        if (scope != ValidationScope.Field || Touched(changed, section.Key))
        {
            if (section.MinItems is { } min && count < min)
                messages.Add(new ValidationMessage
                {
                    Path = section.Key,
                    Section = section.Key,
                    Origin = ValidationOrigin.Field,
                    Severity = RuleSeverity.Error,
                    Message = Text(culture, section.Label) + ": " +
                              string.Format(CultureInfo.InvariantCulture, ValidationTexts.MinItems(culture), min)
                });

            if (section.MaxItems is { } max && count > max)
                messages.Add(new ValidationMessage
                {
                    Path = section.Key,
                    Section = section.Key,
                    Origin = ValidationOrigin.Field,
                    Severity = RuleSeverity.Error,
                    Message = Text(culture, section.Label) + ": " +
                              string.Format(CultureInfo.InvariantCulture, ValidationTexts.MaxItems(culture), max)
                });
        }

        for (var i = 0; i < count; i++)
        {
            var item = array![i] as JsonObject;
            var scoped = ctx.WithItem(item);
            ValidateFields(section, section.ItemFields, scoped, $"{section.Key}[{i}]", scope, changed, culture, messages, evaluated);
        }
    }

    private void ValidateFields(
        TemplateSection section,
        IReadOnlyList<TemplateField> fields,
        EvaluationContext ctx,
        string? prefix,
        ValidationScope scope,
        HashSet<string>? changed,
        string culture,
        List<ValidationMessage> messages,
        List<string> evaluated)
    {
        foreach (var field in fields)
        {
            var path = Instance.PathFor(field, prefix);
            if (!ExpressionEvaluator.EvaluateAsBool(field.VisibleWhen, ctx)) continue;

            // As-you-type we only speak about what the user just touched, plus anything
            // that reads it — otherwise every keystroke lights up the whole form.
            if (scope == ValidationScope.Field && !Touched(changed, path) && !DependsOnChanged(field.DependsOn, changed))
                continue;

            evaluated.Add(path);

            var raw = prefix is null ? ctx.ResolvePath(field.Path) : ctx.ResolveItemPath(field.Key);
            var absent = Values.IsAbsent(raw) || (raw is JsonArray a && a.Count == 0);

            var required = field.Required ||
                           (field.RequiredWhen is not null && ExpressionEvaluator.EvaluateAsBool(field.RequiredWhen, ctx));
            if (absent)
            {
                // "Fill this in" is only useful once the user has left the field or pressed save.
                if (required && scope != ValidationScope.Form)
                    messages.Add(FieldMessage(section, path, RuleSeverity.Error,
                        string.Format(CultureInfo.InvariantCulture, ValidationTexts.Required(culture), Text(culture, field.Label))));
                continue;
            }

            foreach (var m in CheckValue(section, field, path, raw, culture))
                messages.Add(m);
        }
    }

    private static IEnumerable<ValidationMessage> CheckValue(
        TemplateSection section, TemplateField field, string path, object? raw, string culture)
    {
        var label = Text(culture, field.Label);

        switch (field.DataType)
        {
            case FieldDataType.Integer:
            case FieldDataType.Decimal:
            case FieldDataType.Percent:
            case FieldDataType.Money:
            {
                var n = Values.AsNumber(raw);
                if (n is null)
                {
                    yield return FieldMessage(section, path, RuleSeverity.Error,
                        string.Format(CultureInfo.InvariantCulture, ValidationTexts.NotANumber(culture), label));
                    yield break;
                }
                if (field.DataType == FieldDataType.Integer && decimal.Truncate(n.Value) != n.Value)
                    yield return FieldMessage(section, path, RuleSeverity.Error,
                        string.Format(CultureInfo.InvariantCulture, ValidationTexts.NotAnInteger(culture), label));
                if (field.Min is { } min && n < min)
                    yield return FieldMessage(section, path, RuleSeverity.Error,
                        string.Format(CultureInfo.InvariantCulture, ValidationTexts.Min(culture), label, min));
                if (field.Max is { } max && n > max)
                    yield return FieldMessage(section, path, RuleSeverity.Error,
                        string.Format(CultureInfo.InvariantCulture, ValidationTexts.Max(culture), label, max));
                if (field.Decimals is { } decimals && Scale(n.Value) > decimals)
                    yield return FieldMessage(section, path, RuleSeverity.Warning,
                        string.Format(CultureInfo.InvariantCulture, ValidationTexts.Decimals(culture), label, decimals));
                break;
            }

            case FieldDataType.Date:
                if (Values.AsDate(raw) is null)
                    yield return FieldMessage(section, path, RuleSeverity.Error,
                        string.Format(CultureInfo.InvariantCulture, ValidationTexts.NotADate(culture), label));
                break;

            case FieldDataType.Boolean:
                if (Values.AsBool(raw) is null)
                    yield return FieldMessage(section, path, RuleSeverity.Error,
                        string.Format(CultureInfo.InvariantCulture, ValidationTexts.NotABoolean(culture), label));
                break;

            case FieldDataType.Enum:
                if (field.Options.Count > 0)
                {
                    var code = Values.AsString(raw);
                    if (!field.Options.Any(o => string.Equals(o.Code, code, StringComparison.Ordinal)))
                        yield return FieldMessage(section, path, RuleSeverity.Error,
                            string.Format(CultureInfo.InvariantCulture, ValidationTexts.NotAnOption(culture), label, code));
                }
                break;

            case FieldDataType.EnumSet:
                if (field.Options.Count > 0)
                {
                    foreach (var node in Values.AsList(raw))
                    {
                        var code = Values.AsString(Values.FromJson(node));
                        if (!field.Options.Any(o => string.Equals(o.Code, code, StringComparison.Ordinal)))
                            yield return FieldMessage(section, path, RuleSeverity.Error,
                                string.Format(CultureInfo.InvariantCulture, ValidationTexts.NotAnOption(culture), label, code));
                    }
                }
                break;

            case FieldDataType.String:
            case FieldDataType.Text:
                if (field.MaxLength is { } maxLength && (Values.AsString(raw)?.Length ?? 0) > maxLength)
                    yield return FieldMessage(section, path, RuleSeverity.Error,
                        string.Format(CultureInfo.InvariantCulture, ValidationTexts.MaxLength(culture), label, maxLength));
                break;
        }
    }

    // ----- rules --------------------------------------------------------------------

    private void EvaluateRowRule(
        FigureTemplate template,
        TemplateRule rule,
        string sectionKey,
        JsonObject values,
        EvaluationContext ctx,
        ValidationScope scope,
        HashSet<string>? changed,
        string culture,
        List<ValidationMessage> messages)
    {
        var section = template.FindSection(sectionKey);
        if (section is null || !ExpressionEvaluator.EvaluateAsBool(section.VisibleWhen, ctx)) return;
        if (EvaluationContext.Navigate(values, sectionKey) is not JsonArray array) return;

        for (var i = 0; i < array.Count; i++)
        {
            var scoped = ctx.WithItem(array[i] as JsonObject);
            EvaluateRule(rule, scoped, $"{sectionKey}[{i}]", scope, changed, culture, messages);
        }
    }

    private void EvaluateRule(
        TemplateRule rule,
        EvaluationContext ctx,
        string? prefix,
        ValidationScope scope,
        HashSet<string>? changed,
        string culture,
        List<ValidationMessage> messages)
    {
        if (scope == ValidationScope.Field && !DependsOnChanged(rule.DependsOn, changed) && !TargetsChanged(rule, prefix, changed))
            return;

        if (!ExpressionEvaluator.EvaluateAsBool(rule.When, ctx)) return;

        bool? holds;
        if (rule.ServerCheck is { } checkId)
        {
            if (!_serverChecks.TryGet(checkId, out var check)) return;
            holds = check!.Evaluate(rule, ctx);
        }
        else if (rule.Assert is { } assert)
        {
            var value = ExpressionEvaluator.Evaluate(assert, ctx);
            // A rule whose inputs are not all filled in yet says nothing.
            holds = value is null ? null : Values.Truthy(value);
        }
        else
        {
            return;
        }

        if (holds is not false) return;

        var text = Text(culture, rule.Message);
        IReadOnlyList<string> targets = rule.Targets.Count > 0 ? rule.Targets : [string.Empty];
        foreach (var target in targets)
        {
            messages.Add(new ValidationMessage
            {
                Path = Instance.Resolve(target, prefix),
                Message = text,
                Severity = rule.Severity,
                Origin = rule.ServerCheck is null ? ValidationOrigin.Rule : ValidationOrigin.ServerCheck,
                RuleId = rule.Id,
                Section = target.Contains('.', StringComparison.Ordinal) ? target[..target.IndexOf('.')] : null
            });
        }
    }

    private static bool AppliesToScope(TemplateRule rule, ValidationScope scope) => scope switch
    {
        ValidationScope.Submit => true,
        _ => rule.Trigger is RuleTrigger.Change or RuleTrigger.Both
    };

    // ----- helpers ------------------------------------------------------------------

    private static bool Touched(HashSet<string>? changed, string path) =>
        changed is null || changed.Contains(path) ||
        changed.Any(c => c.StartsWith(path + ".", StringComparison.Ordinal) ||
                         c.StartsWith(path + "[", StringComparison.Ordinal));

    private static bool DependsOnChanged(IReadOnlyList<string> dependsOn, HashSet<string>? changed)
    {
        if (changed is null) return true;
        if (dependsOn.Count == 0) return false;
        foreach (var dep in dependsOn)
        {
            var normalized = Instance.Normalize(dep);
            foreach (var c in changed)
                if (Instance.Normalize(c) == normalized) return true;
        }
        return false;
    }

    private static bool TargetsChanged(TemplateRule rule, string? prefix, HashSet<string>? changed) =>
        changed is not null && rule.Targets.Any(t => changed.Contains(Instance.Resolve(t, prefix)));

    private static ValidationMessage FieldMessage(TemplateSection section, string path, RuleSeverity severity, string message) =>
        new()
        {
            Path = path,
            Message = message,
            Severity = severity,
            Origin = ValidationOrigin.Field,
            Section = section.Key
        };

    private static IReadOnlyList<ValidationMessage> Deduplicate(List<ValidationMessage> messages)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ValidationMessage>(messages.Count);
        foreach (var m in messages)
            if (seen.Add($"{m.Path}|{m.RuleId}|{m.Message}"))
                result.Add(m);
        return result;
    }

    private static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xFF;

    private static string Text(string culture, LocalizedText text) => text.For(culture);
}
