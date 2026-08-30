using System.Text.Json.Nodes;
using Coe.Core.Expressions;
using Coe.Core.Templates;

namespace Coe.Ingestion;

public sealed record CompilationResult(FigureTemplate? Template, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool Succeeded => Template is not null && Errors.Count == 0;
}

/// <summary>
/// Turns a domain file plus the fragments it extends into a <see cref="FigureTemplate"/>:
/// merges sections, assigns absolute paths, parses every condition and rule into the portable
/// AST, resolves bare attribute names, and computes each rule's dependency set so the client
/// knows which keystroke should re-run which check.
///
/// A file that does not compile cleanly never becomes an active template — the figure is
/// quarantined with the errors instead, which is what keeps a bad edit out of the booking screen.
/// </summary>
public sealed class TemplateCompiler
{
    public CompilationResult Compile(DomainFile file, IReadOnlyDictionary<string, DomainFile> fragments, int version)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(file.FigureCode)) errors.Add("figureCode is required.");
        if (string.IsNullOrWhiteSpace(file.FigureName)) errors.Add("figureName is required.");

        var sections = MergeSections(file, fragments, errors);
        if (errors.Count > 0) return new CompilationResult(null, errors, warnings);

        var resolver = BuildResolver(sections, errors);
        var compiledSections = new List<TemplateSection>();

        foreach (var dto in sections.OrderBy(s => s.Order).ThenBy(s => s.Key, StringComparer.Ordinal))
            compiledSections.Add(CompileSection(dto, resolver, errors, warnings));

        var rules = new List<TemplateRule>();
        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dto in file.Rules.Concat(FragmentRules(file, fragments)))
        {
            if (string.IsNullOrWhiteSpace(dto.Id)) { errors.Add("A rule is missing its id."); continue; }
            if (!ruleIds.Add(dto.Id)) { errors.Add($"Duplicate rule id '{dto.Id}'."); continue; }
            var rule = CompileRule(dto, resolver, errors);
            if (rule is not null) rules.Add(rule);
        }

        if (errors.Count > 0) return new CompilationResult(null, errors, warnings);

        var template = new FigureTemplate
        {
            FigureCode = file.FigureCode!,
            FigureName = file.FigureName!,
            CommercialName = file.CommercialName,
            Description = Text(file.Description),
            Version = version,
            Modalities = file.Modalities,
            UnderlyingClasses = file.UnderlyingClasses,
            SourceFile = file.SourcePath,
            SourceHash = file.SourceHash,
            CompiledAtUtc = DateTimeOffset.UtcNow,
            Sections = compiledSections,
            Rules = rules
        };

        return new CompilationResult(template, errors, warnings);
    }

    // ----- merging -------------------------------------------------------------------

    private static List<SectionDto> MergeSections(
        DomainFile file, IReadOnlyDictionary<string, DomainFile> fragments, List<string> errors)
    {
        var merged = new List<SectionDto>();
        var byKey = new Dictionary<string, SectionDto>(StringComparer.Ordinal);

        void Absorb(SectionDto incoming)
        {
            if (byKey.TryGetValue(incoming.Key, out var existing))
            {
                // A figure may add columns to, or override the metadata of, an inherited section.
                existing.Label = incoming.Label ?? existing.Label;
                existing.Help = incoming.Help ?? existing.Help;
                existing.VisibleWhen = incoming.VisibleWhen ?? existing.VisibleWhen;
                existing.MinItems = incoming.MinItems ?? existing.MinItems;
                existing.MaxItems = incoming.MaxItems ?? existing.MaxItems;
                if (incoming.Order != 0) existing.Order = incoming.Order;
                existing.Repeating |= incoming.Repeating;
                MergeFields(existing.Fields, incoming.Fields);
                MergeFields(existing.ItemFields, incoming.ItemFields);
                return;
            }
            byKey[incoming.Key] = incoming;
            merged.Add(incoming);
        }

        foreach (var id in file.Extends)
        {
            if (!fragments.TryGetValue(id, out var fragment))
            {
                errors.Add($"Unknown fragment '{id}' in extends.");
                continue;
            }
            foreach (var section in fragment.Sections) Absorb(Clone(section));
        }

        foreach (var section in file.Sections)
        {
            if (string.IsNullOrWhiteSpace(section.Key)) { errors.Add("A section is missing its key."); continue; }
            Absorb(Clone(section));
        }

        foreach (var key in file.RemoveSections)
        {
            if (byKey.Remove(key, out var removed)) merged.Remove(removed);
            else errors.Add($"removeSections lists '{key}', which no fragment provides.");
        }

        return merged;
    }

    private static void MergeFields(List<FieldDto> target, List<FieldDto> incoming)
    {
        foreach (var field in incoming)
        {
            var existing = target.FindIndex(f => string.Equals(f.Key, field.Key, StringComparison.Ordinal));
            if (existing >= 0) target[existing] = field;
            else target.Add(field);
        }
    }

    private static IEnumerable<RuleDto> FragmentRules(DomainFile file, IReadOnlyDictionary<string, DomainFile> fragments) =>
        file.Extends
            .Where(fragments.ContainsKey)
            .SelectMany(id => fragments[id].Rules)
            // A rule whose section the figure removed does not apply to it.
            .Where(r => r.ForEachSection is null || !file.RemoveSections.Contains(r.ForEachSection));

    private static SectionDto Clone(SectionDto s) => new()
    {
        Key = s.Key,
        Label = s.Label,
        Help = s.Help,
        Kind = s.Kind,
        Order = s.Order,
        VisibleWhen = s.VisibleWhen,
        Repeating = s.Repeating,
        MinItems = s.MinItems,
        MaxItems = s.MaxItems,
        Fields = [.. s.Fields],
        ItemFields = [.. s.ItemFields]
    };

    // ----- compiling -----------------------------------------------------------------

    private static PathResolver BuildResolver(List<SectionDto> sections, List<string> errors)
    {
        var resolver = new PathResolver();
        foreach (var section in sections)
        {
            resolver.AddSection(section.Key, section.Repeating);
            var fields = section.Repeating ? section.ItemFields : section.Fields;

            if (section.Repeating && section.Fields.Count > 0)
                errors.Add($"Section '{section.Key}' repeats, so its attributes belong in itemFields, not fields.");
            if (!section.Repeating && section.ItemFields.Count > 0)
                errors.Add($"Section '{section.Key}' does not repeat, so it cannot declare itemFields.");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                if (string.IsNullOrWhiteSpace(field.Key)) { errors.Add($"A field of section '{section.Key}' is missing its key."); continue; }
                if (!seen.Add(field.Key)) { errors.Add($"Duplicate attribute '{field.Key}' in section '{section.Key}'."); continue; }
                resolver.AddField(section.Key, field.Key, section.Repeating);
            }
        }
        return resolver;
    }

    private static TemplateSection CompileSection(
        SectionDto dto, PathResolver resolver, List<string> errors, List<string> warnings)
    {
        var fields = (dto.Repeating ? dto.ItemFields : dto.Fields)
            .OrderBy(f => f.Order)
            .Select(f => CompileField(dto, f, resolver, errors, warnings))
            .ToList();

        return new TemplateSection
        {
            Key = dto.Key,
            Label = Text(dto.Label) ?? new LocalizedText(dto.Key),
            Help = Text(dto.Help),
            Kind = string.Equals(dto.Kind, "common", StringComparison.OrdinalIgnoreCase) ? SectionKind.Common : SectionKind.Tab,
            Order = dto.Order,
            // A section's own visibility is evaluated outside any row scope.
            VisibleWhen = Compile(dto.VisibleWhen, string.Empty, resolver, errors, $"section '{dto.Key}' visibleWhen"),
            Repeating = dto.Repeating,
            MinItems = dto.MinItems,
            MaxItems = dto.MaxItems,
            Fields = dto.Repeating ? [] : fields,
            ItemFields = dto.Repeating ? fields : []
        };
    }

    private static TemplateField CompileField(
        SectionDto section, FieldDto dto, PathResolver resolver, List<string> errors, List<string> warnings)
    {
        var scope = section.Key;
        var path = section.Repeating ? $"{section.Key}[].{dto.Key}" : $"{section.Key}.{dto.Key}";
        var dataType = ParseDataType(dto.DataType, path, errors);

        var visibleWhen = Compile(dto.VisibleWhen, scope, resolver, errors, $"'{path}' visibleWhen");
        var requiredWhen = Compile(dto.RequiredWhen, scope, resolver, errors, $"'{path}' requiredWhen");
        var enabledWhen = Compile(dto.EnabledWhen, scope, resolver, errors, $"'{path}' enabledWhen");
        var computed = Compile(dto.Computed, scope, resolver, errors, $"'{path}' computed");

        if ((dataType is FieldDataType.Enum or FieldDataType.EnumSet) && dto.Options.Count == 0 && dto.OptionSource is null)
            warnings.Add($"'{path}' is an enum with neither options nor an optionSource.");

        // Inside a repeating section a condition reads sibling columns through @, so item
        // references have to be recorded against this section to be matchable later.
        var itemScope = section.Repeating ? section.Key : null;
        var dependsOn = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var expr in new[] { visibleWhen, requiredWhen, enabledWhen, computed })
            if (expr is not null)
                foreach (var dep in expr.Dependencies(itemScope)) dependsOn.Add(dep);

        return new TemplateField
        {
            Key = dto.Key,
            Path = path,
            Label = Text(dto.Label) ?? new LocalizedText(dto.Key),
            Help = Text(dto.Help),
            DataType = dataType,
            B3Field = dto.B3Field,
            Symbol = dto.Symbol,
            Unit = dto.Unit,
            Decimals = dto.Decimals,
            MaxLength = dto.MaxLength,
            Min = dto.Min,
            Max = dto.Max,
            Default = dto.Default?.DeepClone(),
            Required = dto.Required,
            RequiredWhen = requiredWhen,
            VisibleWhen = visibleWhen,
            EnabledWhen = enabledWhen,
            Computed = computed,
            OptionSource = dto.OptionSource,
            Options = dto.Options.Select(o => new FieldOption(o.Code, Text(o.Label) ?? new LocalizedText(o.Code))
            {
                Help = Text(o.Help),
                VisibleWhen = Compile(o.VisibleWhen, scope, resolver, errors, $"'{path}' option '{o.Code}' visibleWhen")
            }).ToList(),
            DependsOn = [.. dependsOn],
            Order = dto.Order,
            InGrid = dto.InGrid
        };
    }

    private static TemplateRule? CompileRule(RuleDto dto, PathResolver resolver, List<string> errors)
    {
        var scope = dto.ForEachSection ?? string.Empty;

        if (dto.Assert is null && dto.ServerCheck is null)
        {
            errors.Add($"Rule '{dto.Id}' declares neither assert nor serverCheck.");
            return null;
        }
        if (dto.Assert is not null && dto.ServerCheck is not null)
        {
            errors.Add($"Rule '{dto.Id}' declares both assert and serverCheck; pick one.");
            return null;
        }
        if (dto.Message is null || string.IsNullOrWhiteSpace(dto.Message.Pt))
        {
            errors.Add($"Rule '{dto.Id}' is missing its message.");
            return null;
        }
        if (scope.Length > 0 && !resolver.IsSection(scope))
        {
            errors.Add($"Rule '{dto.Id}' targets section '{scope}', which does not exist.");
            return null;
        }

        var when = Compile(dto.When, scope, resolver, errors, $"rule '{dto.Id}' when");
        var assert = Compile(dto.Assert, scope, resolver, errors, $"rule '{dto.Id}' assert");

        var targets = dto.Targets.Select(t => resolver.ResolveTarget(t, scope, errors)).ToList();
        if (targets.Count == 0)
            errors.Add($"Rule '{dto.Id}' has no targets; a message with nowhere to land is invisible.");

        var dependsOn = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var expr in new[] { when, assert })
            if (expr is not null)
                foreach (var dep in expr.Dependencies(dto.ForEachSection)) dependsOn.Add(dep);
        foreach (var target in targets) dependsOn.Add(target);

        return new TemplateRule
        {
            Id = dto.Id,
            Targets = targets,
            When = when,
            Assert = assert,
            ServerCheck = dto.ServerCheck,
            Args = dto.Args.ToDictionary(kv => kv.Key, kv => kv.Value?.DeepClone(), StringComparer.Ordinal),
            Message = Text(dto.Message)!,
            Severity = ParseEnum(dto.Severity, RuleSeverity.Error),
            Execution = ParseExecution(dto.Execution),
            Trigger = ParseEnum(dto.Trigger, RuleTrigger.Change),
            ForEachSection = dto.ForEachSection,
            DependsOn = [.. dependsOn]
        };
    }

    // ----- helpers -------------------------------------------------------------------

    private static Expr? Compile(string? source, string scope, PathResolver resolver, List<string> errors, string where)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        try
        {
            var parsed = ExpressionParser.Parse(source);
            return resolver.Rewrite(parsed, scope, errors);
        }
        catch (ExpressionParseException ex)
        {
            errors.Add($"{where}: {ex.Message}");
            return null;
        }
    }

    private static FieldDataType ParseDataType(string value, string path, List<string> errors)
    {
        if (Enum.TryParse<FieldDataType>(value, ignoreCase: true, out var parsed)) return parsed;
        errors.Add($"'{path}' declares unknown dataType '{value}'.");
        return FieldDataType.String;
    }

    private static T ParseEnum<T>(string value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : fallback;

    private static RuleExecution ParseExecution(string value) => value.ToLowerInvariant() switch
    {
        "client" => RuleExecution.Client,
        "server" => RuleExecution.Server,
        _ => RuleExecution.Both
    };

    private static LocalizedText? Text(LocalizedTextDto? dto) =>
        dto is null ? null : new LocalizedText(dto.Pt, dto.En);
}
