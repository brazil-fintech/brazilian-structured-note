using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Coe.Core.Diagnostics;
using Coe.Core.Expressions;
using Coe.Core.Templates;

namespace Coe.Core.Validation;

/// <summary>
/// Evaluates a template against an instance. This is the authority: the React client runs the
/// same rules for instant feedback, but every save goes through <see cref="ValidationScope.Submit"/>
/// here before anything is written.
///
/// The booking screen calls this on every keystroke, so the pass is kept narrow rather than
/// fast-by-luck: <see cref="ValidationScope.Field"/> evaluates only the attributes that changed
/// and the rules that read them, using the dependency sets the compiler worked out at ingestion.
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
        var started = Stopwatch.GetTimestamp();

        using var activity = CoeDiagnostics.Validation.StartActivity("coe.validate", ActivityKind.Internal);
        activity?.SetTag("coe.figure.code", template.FigureCode);
        activity?.SetTag("coe.template.version", template.Version);
        activity?.SetTag("coe.validation.scope", scope.ToString());
        activity?.SetTag("coe.validation.changed_paths", changedPaths?.Count ?? 0);

        var ctx = new EvaluationContext(values, variables);
        var changed = changedPaths is null ? ChangeSet.All : new ChangeSet(changedPaths);
        var messages = new List<ValidationMessage>();
        var evaluated = new List<string>();
        var rulesEvaluated = 0;

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
                rulesEvaluated += EvaluateRowRule(template, rule, sectionKey, values, ctx, scope, changed, culture, messages, evaluated);
            else if (EvaluateRule(rule, ctx, prefix: null, scope, changed, culture, messages, evaluated))
                rulesEvaluated++;
        }

        var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Record(template, scope, messages, rulesEvaluated, elapsedMs, activity);

        return new ValidationResult
        {
            Messages = Deduplicate(messages),
            EvaluatedPaths = evaluated
        };
    }

    private static void Record(
        FigureTemplate template,
        ValidationScope scope,
        List<ValidationMessage> messages,
        int rulesEvaluated,
        double elapsedMs,
        Activity? activity)
    {
        var scopeTag = scope.ToString();
        var figureTag = template.FigureCode;

        CoeDiagnostics.ValidationDuration.Record(elapsedMs,
            new KeyValuePair<string, object?>("coe.figure.code", figureTag),
            new KeyValuePair<string, object?>("coe.validation.scope", scopeTag));

        CoeDiagnostics.ValidationRulesEvaluated.Record(rulesEvaluated,
            new KeyValuePair<string, object?>("coe.validation.scope", scopeTag));

        var errors = 0;
        var warnings = 0;
        foreach (var message in messages)
        {
            if (message.Severity == RuleSeverity.Error) errors++;
            else if (message.Severity == RuleSeverity.Warning) warnings++;

            CoeDiagnostics.ValidationMessages.Add(1,
                new KeyValuePair<string, object?>("coe.message.severity", message.Severity.ToString()),
                new KeyValuePair<string, object?>("coe.message.origin", message.Origin.ToString()));
        }

        activity?.SetTag("coe.validation.rules_evaluated", rulesEvaluated);
        activity?.SetTag("coe.validation.errors", errors);
        activity?.SetTag("coe.validation.warnings", warnings);
    }

    // ----- fields -------------------------------------------------------------------

    private void ValidateRepeatingSection(
        TemplateSection section,
        JsonObject values,
        EvaluationContext ctx,
        ValidationScope scope,
        ChangeSet changed,
        string culture,
        List<ValidationMessage> messages,
        List<string> evaluated)
    {
        var array = EvaluationContext.Navigate(values, section.Key) as JsonArray;
        var count = array?.Count ?? 0;

        if (scope != ValidationScope.Field || changed.Touched(section.Key))
        {
            if (section.MinItems is { } min && count < min)
                messages.Add(SectionMessage(section, culture, ValidationTexts.MinItems(culture), min));

            if (section.MaxItems is { } max && count > max)
                messages.Add(SectionMessage(section, culture, ValidationTexts.MaxItems(culture), max));
        }

        for (var i = 0; i < count; i++)
        {
            var item = array![i] as JsonObject;
            var scoped = ctx.WithItem(item);
            ValidateFields(section, section.ItemFields, scoped, $"{section.Key}[{i}]", scope, changed, culture, messages, evaluated);
        }
    }

    private static ValidationMessage SectionMessage(TemplateSection section, string culture, string format, int value) => new()
    {
        Path = section.Key,
        Section = section.Key,
        Origin = ValidationOrigin.Field,
        Severity = RuleSeverity.Error,
        Message = section.Label.For(culture) + ": " + string.Format(CultureInfo.InvariantCulture, format, value)
    };

    private static void ValidateFields(
        TemplateSection section,
        IReadOnlyList<TemplateField> fields,
        EvaluationContext ctx,
        string? prefix,
        ValidationScope scope,
        ChangeSet changed,
        string culture,
        List<ValidationMessage> messages,
        List<string> evaluated)
    {
        for (var f = 0; f < fields.Count; f++)
        {
            var field = fields[f];
            var path = Instance.PathFor(field, prefix);
            if (!ExpressionEvaluator.EvaluateAsBool(field.VisibleWhen, ctx)) continue;

            // As-you-type we only speak about what the user just touched, plus anything
            // that reads it — otherwise every keystroke lights up the whole form.
            if (scope == ValidationScope.Field && !changed.Touched(path) && !changed.Intersects(field.DependsOn))
                continue;

            evaluated.Add(path);

            var raw = prefix is null ? ctx.ResolvePath(field.Path) : ctx.ResolveItemPath(field.Key);
            var absent = Values.IsAbsent(raw) || (raw is JsonArray a && a.Count == 0);

            if (absent)
            {
                var required = field.Required ||
                               (field.RequiredWhen is not null && ExpressionEvaluator.EvaluateAsBool(field.RequiredWhen, ctx));

                // "Fill this in" is only useful once the user has left the field or pressed save.
                if (required && scope != ValidationScope.Form)
                    messages.Add(FieldMessage(section, path, RuleSeverity.Error,
                        string.Format(CultureInfo.InvariantCulture, ValidationTexts.Required(culture), field.Label.For(culture))));
                continue;
            }

            CheckValue(section, field, path, raw, culture, messages);
        }
    }

    private static void CheckValue(
        TemplateSection section, TemplateField field, string path, object? raw, string culture, List<ValidationMessage> messages)
    {
        var label = field.Label.For(culture);

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
                    messages.Add(FieldMessage(section, path, RuleSeverity.Error,
                        string.Format(CultureInfo.InvariantCulture, ValidationTexts.NotANumber(culture), label)));
                    return;
                }
                if (field.DataType == FieldDataType.Integer && decimal.Truncate(n.Value) != n.Value)
                    messages.Add(FieldMessage(section, path, RuleSeverity.Error,
                        string.Format(CultureInfo.InvariantCulture, ValidationTexts.NotAnInteger(culture), label)));
                if (field.Min is { } min && n < min)
                    messages.Add(FieldMessage(section, path, RuleSeverity.Error,
                        string.Format(CultureInfo.InvariantCulture, ValidationTexts.Min(culture), label, min)));
                if (field.Max is { } max && n > max)
                    messages.Add(FieldMessage(section, path, RuleSeverity.Error,
                        string.Format(CultureInfo.InvariantCulture, ValidationTexts.Max(culture), label, max)));
                if (field.Decimals is { } decimals && Scale(n.Value) > decimals)
                    messages.Add(FieldMessage(section, path, RuleSeverity.Warning,
                        string.Format(CultureInfo.InvariantCulture, ValidationTexts.Decimals(culture), label, decimals)));
                return;
            }

            case FieldDataType.Date:
                if (Values.AsDate(raw) is null)
                    messages.Add(FieldMessage(section, path, RuleSeverity.Error,
                        string.Format(CultureInfo.InvariantCulture, ValidationTexts.NotADate(culture), label)));
                return;

            case FieldDataType.Boolean:
                if (Values.AsBool(raw) is null)
                    messages.Add(FieldMessage(section, path, RuleSeverity.Error,
                        string.Format(CultureInfo.InvariantCulture, ValidationTexts.NotABoolean(culture), label)));
                return;

            case FieldDataType.Enum:
            {
                if (field.Options.Count == 0) return;
                var code = Values.AsString(raw);
                if (!HasOption(field, code))
                    messages.Add(FieldMessage(section, path, RuleSeverity.Error,
                        string.Format(CultureInfo.InvariantCulture, ValidationTexts.NotAnOption(culture), label, code)));
                return;
            }

            case FieldDataType.EnumSet:
            {
                if (field.Options.Count == 0) return;
                foreach (var node in Values.AsList(raw))
                {
                    var code = Values.AsString(Values.FromJson(node));
                    if (!HasOption(field, code))
                        messages.Add(FieldMessage(section, path, RuleSeverity.Error,
                            string.Format(CultureInfo.InvariantCulture, ValidationTexts.NotAnOption(culture), label, code)));
                }
                return;
            }

            case FieldDataType.String:
            case FieldDataType.Text:
                if (field.MaxLength is { } maxLength && (Values.AsString(raw)?.Length ?? 0) > maxLength)
                    messages.Add(FieldMessage(section, path, RuleSeverity.Error,
                        string.Format(CultureInfo.InvariantCulture, ValidationTexts.MaxLength(culture), label, maxLength)));
                return;
        }
    }

    private static bool HasOption(TemplateField field, string? code)
    {
        var options = field.Options;
        for (var i = 0; i < options.Count; i++)
            if (string.Equals(options[i].Code, code, StringComparison.Ordinal))
                return true;
        return false;
    }

    // ----- rules --------------------------------------------------------------------

    private int EvaluateRowRule(
        FigureTemplate template,
        TemplateRule rule,
        string sectionKey,
        JsonObject values,
        EvaluationContext ctx,
        ValidationScope scope,
        ChangeSet changed,
        string culture,
        List<ValidationMessage> messages,
        List<string> evaluated)
    {
        var section = template.FindSection(sectionKey);
        if (section is null || !ExpressionEvaluator.EvaluateAsBool(section.VisibleWhen, ctx)) return 0;
        if (EvaluationContext.Navigate(values, sectionKey) is not JsonArray array) return 0;

        var evaluatedRules = 0;
        for (var i = 0; i < array.Count; i++)
        {
            var scoped = ctx.WithItem(array[i] as JsonObject);
            if (EvaluateRule(rule, scoped, $"{sectionKey}[{i}]", scope, changed, culture, messages, evaluated)) evaluatedRules++;
        }
        return evaluatedRules;
    }

    /// <summary>Returns true when the rule was actually evaluated rather than skipped.</summary>
    private bool EvaluateRule(
        TemplateRule rule,
        EvaluationContext ctx,
        string? prefix,
        ValidationScope scope,
        ChangeSet changed,
        string culture,
        List<ValidationMessage> messages,
        List<string> evaluated)
    {
        if (scope == ValidationScope.Field && !changed.Intersects(rule.DependsOn) && !TargetsChanged(rule, prefix, changed))
            return false;

        // Everything this rule can say lands on its targets, so having looked at it, this pass
        // supersedes whatever it said about them last time — including when the guard below now
        // skips it, or its inputs are not all filled in. A narrow pass reports the paths it is
        // authoritative about, not only the ones it had a complaint about; recording them here
        // is what lets a caller replace those messages instead of piling new ones on top.
        foreach (var target in rule.Targets)
        {
            if (target.Length > 0) evaluated.Add(Instance.Resolve(target, prefix));
        }

        if (!ExpressionEvaluator.EvaluateAsBool(rule.When, ctx)) return false;

        bool? holds;
        if (rule.ServerCheck is { } checkId)
        {
            if (!_serverChecks.TryGet(checkId, out var check)) return false;
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
            return false;
        }

        if (holds is not false) return true;

        var text = rule.Message.For(culture);
        IReadOnlyList<string> targets = rule.Targets.Count > 0 ? rule.Targets : [string.Empty];
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var dot = target.IndexOf('.');
            messages.Add(new ValidationMessage
            {
                Path = Instance.Resolve(target, prefix),
                Message = text,
                Severity = rule.Severity,
                Origin = rule.ServerCheck is null ? ValidationOrigin.Rule : ValidationOrigin.ServerCheck,
                RuleId = rule.Id,
                Section = dot > 0 ? target[..dot] : null
            });
        }
        return true;
    }

    private static bool AppliesToScope(TemplateRule rule, ValidationScope scope) => scope switch
    {
        ValidationScope.Submit => true,
        _ => rule.Trigger is RuleTrigger.Change or RuleTrigger.Both
    };

    // ----- helpers ------------------------------------------------------------------

    private static bool TargetsChanged(TemplateRule rule, string? prefix, ChangeSet changed)
    {
        if (changed.MatchesEverything) return false;
        var targets = rule.Targets;
        for (var i = 0; i < targets.Count; i++)
            if (changed.ContainsExact(Instance.Resolve(targets[i], prefix)))
                return true;
        return false;
    }

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
        if (messages.Count <= 1) return messages;

        var seen = new HashSet<string>(messages.Count, StringComparer.Ordinal);
        var result = new List<ValidationMessage>(messages.Count);
        foreach (var m in messages)
            if (seen.Add($"{m.Path}|{m.RuleId}|{m.Message}"))
                result.Add(m);
        return result;
    }

    private static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xFF;
}
