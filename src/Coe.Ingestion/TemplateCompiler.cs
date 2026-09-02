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
public sealed class TemplateCompiler(B3Reference? reference = null)
{
    private readonly B3Reference _reference = reference ?? B3Reference.Empty;

    public CompilationResult Compile(DomainFile file, IReadOnlyDictionary<string, DomainFile> fragments, int version)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(file.FigureCode)) errors.Add("figureCode is required.");
        if (string.IsNullOrWhiteSpace(file.FigureName)) errors.Add("figureName is required.");

        CheckAgainstFigureCatalogue(file, errors, warnings);

        var sections = MergeSections(file, fragments, errors);
        if (errors.Count > 0) return new CompilationResult(null, errors, warnings);

        var resolver = BuildResolver(sections, errors);
        var compiledSections = new List<TemplateSection>();

        // The attributes B3 publishes for this figure, keyed on its own name for them. A field
        // that names the attribute the way B3 prints it adopts the code B3 registers it under,
        // so the registration file can be written without anyone copying 1,600 codes by hand.
        var figureCode = file.FigureCode ?? string.Empty;
        var published = _reference.FigureAttributesByName(figureCode);

        foreach (var dto in sections.OrderBy(s => s.Order).ThenBy(s => s.Key, StringComparer.Ordinal))
            compiledSections.Add(CompileSection(dto, resolver, errors, warnings, _reference, published, figureCode));

        CheckFigureAttributeCoverage(file, compiledSections, warnings);

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
        SectionDto dto, PathResolver resolver, List<string> errors, List<string> warnings, B3Reference reference,
        IReadOnlyDictionary<string, B3FigureAttribute> published, string figureCode)
    {
        var fields = (dto.Repeating ? dto.ItemFields : dto.Fields)
            .OrderBy(f => f.Order)
            .Select(f => CompileField(dto, f, resolver, errors, warnings, reference, published, figureCode))
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
        SectionDto section, FieldDto dto, PathResolver resolver, List<string> errors, List<string> warnings,
        B3Reference reference, IReadOnlyDictionary<string, B3FigureAttribute> published, string figureCode)
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

        // A code the author wrote down is a claim the compiler holds them to; one it inferred
        // from B3's name for the attribute is a convenience, and a disagreement there says the
        // guess was wrong, not that the figure is broken.
        //
        // Nothing is inferred for a column of a repeating section. The variable-data record of
        // the Registro COE has no row index, so it cannot carry one; those attributes belong to
        // the cash-flow and basket files, which have their own codes for them. Matching by name
        // would attach the wrong one — B3 calls the basket component's quotation type "Tipo de
        // Cotação para Liquidação", exactly what it calls the registration-level field, and the
        // two are registered under different code sets in different files.
        var declaredDataCode = dto.B3DataCode is not null;
        var dataCode = dto.B3DataCode
                       ?? (section.Repeating ? null : ResolveDataCode(dto, dataType, published, reference, figureCode));

        if (declaredDataCode && section.Repeating)
            warnings.Add(
                $"'{path}' declares a b3DataCode, but the variable-data record cannot address a "
                + "repeating column; name the numbered series with b3Series instead.");

        // A repeating column maps to a run of numbered attributes rather than to one: B3's file
        // format is flat, so what the form shows as ten rows it registers as ten fields.
        var seriesCodes = section.Repeating && dto.B3Series is { Length: > 0 } concept
            ? reference.FigureAttributeSeries(figureCode, concept).Select(a => a.FieldCode).ToList()
            : [];

        if (dto.B3Series is { Length: > 0 } && !section.Repeating)
            warnings.Add($"'{path}' names a b3Series but its section does not repeat; use b3DataCode.");

        // A code that exists in the dictionary but not in this figure's attribute list is a
        // field B3 will reject on the registration file as not belonging to the figure.
        if (declaredDataCode && published.Count > 0 && !published.Values.Any(a => a.FieldCode == dataCode))
            warnings.Add($"'{path}' names B3 data code '{dataCode}', which B3 does not register for this figure.");

        CheckAgainstB3Dictionary(dto, path, dataType, reference, errors, warnings);
        CheckAgainstDerivativeDictionary(dto, dataCode, declaredDataCode, path, dataType, reference, errors, warnings);

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
            B3FieldCode = dto.B3FieldCode,
            B3DataCode = dataCode,
            B3SeriesCodes = seriesCodes,
            B3Domain = dto.B3Domain,
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
                B3Code = o.B3Code,
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

    /// <summary>
    /// A figure the platform books must be one B3 actually publishes, under the name B3 gives it.
    /// A rename in the catalogue then surfaces at ingestion rather than at registration.
    /// </summary>
    private void CheckAgainstFigureCatalogue(DomainFile file, List<string> errors, List<string> warnings)
    {
        if (_reference.Figures.Count == 0 || string.IsNullOrWhiteSpace(file.FigureCode)) return;

        var published = _reference.Figure(file.FigureCode);
        if (published is null)
        {
            errors.Add($"'{file.FigureCode}' is not in B3's figure catalogue ({B3Reference.FiguresFile}).");
            return;
        }

        if (!string.IsNullOrWhiteSpace(file.FigureName) &&
            !string.Equals(published.Name, file.FigureName, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                $"figureName '{file.FigureName}' differs from B3's '{published.Name}' for {file.FigureCode}.");
        }

        // A house label that happens to be another figure's registered name is not a naming
        // quibble: the booking screen would head a COE001001 with the name B3 gives COE001064,
        // and whoever reads it has no way to tell which figure is being registered.
        if (!string.IsNullOrWhiteSpace(file.CommercialName))
        {
            var clash = _reference.Figures.FirstOrDefault(f =>
                !string.Equals(f.Code, file.FigureCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.Name, file.CommercialName, StringComparison.OrdinalIgnoreCase));

            if (clash is not null)
            {
                errors.Add(
                    $"commercialName '{file.CommercialName}' is B3's registered name for {clash.Code}; "
                    + $"{file.FigureCode} must not advertise itself as another figure.");
            }
        }
    }

    /// <summary>
    /// Finds the code B3 registers this attribute under, for a field that does not name one.
    ///
    /// The match is on B3's own name for the attribute, normalised: a domain file writes
    /// <c>b3Field</c> exactly as the registration screen prints it, and the export names it the
    /// same way. Where a file gives no <c>b3Field</c>, its Portuguese label is tried, because a
    /// generated file takes that label from the manual's annex, which is the same text again.
    ///
    /// A file that states <c>b3DataCode</c> outright always wins: this only fills a blank.
    /// </summary>
    private static string? ResolveDataCode(
        FieldDto dto, FieldDataType dataType, IReadOnlyDictionary<string, B3FigureAttribute> published,
        B3Reference reference, string figureCode)
    {
        if (published.Count == 0) return null;

        foreach (var candidate in new[] { dto.B3Field, dto.Label?.Pt })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;

            // B3's name for the attribute first; failing that, the same name reduced to the
            // words that carry its meaning, which is what bridges "Data de verificação
            // amortização 1" to the export's "Data verificação amortização 1".
            var attribute = published.GetValueOrDefault(B3DerivativeFields.NormalizeName(candidate))
                            ?? reference.FigureAttributeLike(figureCode, candidate);
            if (attribute is null) continue;

            // Concept names repeat across the catalogue: a "Strike" that B3 registers as a date
            // is not this figure's percentage strike, whatever the two are called. A match the
            // types contradict is a coincidence, and adopting it would write the wrong code into
            // a registration, so it is dropped rather than reported.
            return Accepts(attribute.DataType).Contains(dataType) ? attribute.FieldCode : null;
        }

        return null;
    }

    /// <summary>
    /// Reports the attributes B3 registers for this figure that the domain file does not carry.
    ///
    /// <c>DTpFigurasDadosDerivativo</c> lists them per figure, so "is this figure complete?" is
    /// a question with a published answer rather than a reading of the manual. A mandatory
    /// attribute the file omits is one the registration file cannot carry, which is a warning
    /// and not an error: a figure is still bookable and still validates, it simply cannot be
    /// uploaded to B3 until the gap is closed.
    /// </summary>
    private void CheckFigureAttributeCoverage(
        DomainFile file, List<TemplateSection> sections, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(file.FigureCode)) return;

        var expected = _reference.FigureAttributes(file.FigureCode);
        if (expected.Count == 0) return;

        var mapped = sections
            .Where(s => !s.Repeating)
            .SelectMany(s => s.Fields)
            .Select(f => f.B3DataCode ?? string.Empty)
            .Where(code => code.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A repeating column that names a series accounts for every attribute in that run.
        foreach (var code in sections.Where(s => s.Repeating).SelectMany(s => s.ItemFields).SelectMany(f => f.B3SeriesCodes))
            mapped.Add(code);

        var missing = expected.Where(a => !mapped.Contains(a.FieldCode)).ToList();
        if (missing.Count == 0) return;

        var mandatory = missing.Where(a => a.Mandatory).ToList();
        if (mapped.Count == 0)
        {
            // Nothing is mapped at all: the file predates the association B3 publishes. One
            // line, not a list of forty.
            warnings.Add(
                $"{file.FigureCode} carries no b3DataCode; B3 registers {expected.Count} attribute(s) for it "
                + $"({mandatory.Count} mandatory), none of which this file can write to the registration file.");
            return;
        }

        if (mandatory.Count > 0)
        {
            var named = string.Join(", ", mandatory.Take(8).Select(a => $"{a.FieldCode} '{a.Name}'"));
            var rest = mandatory.Count > 8 ? $" and {mandatory.Count - 8} more" : string.Empty;
            warnings.Add($"{file.FigureCode} does not carry B3's mandatory attribute(s) {named}{rest}.");
        }
    }

    /// <summary>
    /// Checks a field against B3's published metadata where the domain file claims a mapping:
    /// the option codes against the named domain, and the declared size and decimals against the
    /// strategy-field dictionary.
    /// </summary>
    private static void CheckAgainstB3Dictionary(
        FieldDto dto, string path, FieldDataType dataType, B3Reference reference,
        List<string> errors, List<string> warnings)
    {
        if (dto.B3Domain is { } domainType)
        {
            var domain = reference.Domain(domainType);
            if (domain.Count == 0)
            {
                warnings.Add($"'{path}' names B3 domain '{domainType}', which the reference export does not contain.");
            }
            else
            {
                foreach (var option in dto.Options)
                {
                    if (string.IsNullOrWhiteSpace(option.B3Code))
                    {
                        errors.Add($"'{path}' option '{option.Code}' has no b3Code, but the field maps to domain '{domainType}'.");
                        continue;
                    }

                    var match = domain.FirstOrDefault(v => v.Code == option.B3Code);
                    if (match is null)
                        errors.Add($"'{path}' option '{option.Code}' has b3Code '{option.B3Code}', which is not a value of '{domainType}'.");
                    else if (!match.Enabled)
                        warnings.Add($"'{path}' option '{option.Code}' maps to '{domainType}' code {option.B3Code} ('{match.Name}'), which B3 has disabled.");
                }
            }
        }

        if (dto.B3FieldCode is not { } fieldCode) return;

        var published = reference.StrategyField(fieldCode);
        if (published is null)
        {
            if (reference.StrategyFields.Count > 0)
                errors.Add($"'{path}' names B3 field code '{fieldCode}', which is not in the strategy-field dictionary.");
            return;
        }

        var expected = Accepts(published.DataType);
        if (expected.Length > 0 && !expected.Contains(dataType))
            errors.Add($"'{path}' is {dataType} but B3 field {fieldCode} ('{published.Name}') is {published.DataType}.");

        if (dto.Decimals is { } decimals && published.Decimals != decimals)
            warnings.Add($"'{path}' declares {decimals} decimal place(s); B3 field {fieldCode} registers {published.Decimals}.");

        if (dto.MaxLength is { } maxLength && published.Length > 0 && maxLength > published.Length)
            errors.Add($"'{path}' allows {maxLength} characters; B3 field {fieldCode} registers {published.Length}.");
    }

    /// <summary>
    /// Checks a field that declares a <c>b3DataCode</c> against B3's derivative-data dictionary:
    /// the code must exist, the declared type must be the one B3 registers, and the precision
    /// must fit. This is the dictionary the Registro COE upload writes against, so a mismatch
    /// here is a file B3 will reject — caught at ingestion instead.
    /// </summary>
    private static void CheckAgainstDerivativeDictionary(
        FieldDto dto, string? dataCode, bool declared, string path, FieldDataType dataType,
        B3Reference reference, List<string> errors, List<string> warnings)
    {
        if (dataCode is not { } code) return;

        // Only a code the author wrote down can fail the figure. An inferred one that turns out
        // to disagree is reported and the registration writer is left without it, which is the
        // safe direction: a field B3 will not accept is better than a field written wrongly.
        var report = declared ? errors : warnings;
        var origin = declared ? "names" : "matches B3's name for";

        var published = reference.DerivativeField(code);
        if (published is null)
        {
            if (reference.DerivativeFields.Fields.Count > 0)
                report.Add($"'{path}' {origin} B3 data code '{code}', which is not in {B3DerivativeFields.FieldsFile}.");
            return;
        }

        var expected = Accepts(published.DataType);
        if (expected.Length > 0 && !expected.Contains(dataType))
            report.Add($"'{path}' is {dataType} but B3 data field {code} ('{published.Name}') is {published.DataType}.");

        if (dto.Decimals is { } decimals && published.Decimals != decimals)
            warnings.Add($"'{path}' declares {decimals} decimal place(s); B3 data field {code} registers {published.Decimals}.");

        if (dto.MaxLength is { } maxLength && published.Length > 0 && maxLength > published.Length)
            report.Add($"'{path}' allows {maxLength} characters; B3 data field {code} registers {published.Length}.");

        // An enum mapped to a dictionary field registers one of that field's values; anything
        // else is an option the upload cannot carry.
        if (!published.IsDomain || published.DomainValues.Count == 0) return;

        foreach (var option in dto.Options)
        {
            if (string.IsNullOrWhiteSpace(option.B3Code)) continue;
            if (!published.DomainValues.Any(v => v.Code == option.B3Code))
                report.Add(
                    $"'{path}' option '{option.Code}' has b3Code '{option.B3Code}', which B3 data field "
                    + $"{code} ('{published.Name}') does not accept.");
        }
    }

    /// <summary>The template data types one of B3's published data types may be modelled as.</summary>
    private static FieldDataType[] Accepts(string b3DataType) => b3DataType.ToUpperInvariant() switch
    {
        "NUMERICO" => [FieldDataType.Decimal, FieldDataType.Percent, FieldDataType.Money, FieldDataType.Integer],
        "DATA" => [FieldDataType.Date],
        "TEXTO" => [FieldDataType.String, FieldDataType.Text],
        "DOMINIO" => [FieldDataType.Enum, FieldDataType.EnumSet, FieldDataType.Boolean],
        _ => []
    };

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
