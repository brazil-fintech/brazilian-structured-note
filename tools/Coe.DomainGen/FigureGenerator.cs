using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Coe.Ingestion;

namespace Coe.DomainGen;

/// <summary>What one figure's generation produced, for the run report.</summary>
public sealed record GeneratedFigure(
    string Code, string Name, string RelativePath, int Fields, int Inherited, int Skipped, int Rules, int FromExport);

/// <summary>
/// Writes a domain file for every figure in B3's catalogue whose attributes the Manual de
/// Operações annex publishes.
///
/// Ten figures are modelled by hand: their files carry the formula symbols, the economic
/// warnings a desk would otherwise catch by eye, and prose written for the people booking them.
/// Nothing here can produce that, and it does not try — a curated file always wins. What it does
/// produce is the rest of the catalogue, faithfully: the attributes B3 registers for the figure,
/// with the type, precision, domain and conditions taken from B3's own instructions, so every
/// figure can be booked instead of only the ten somebody had time to write out.
/// </summary>
public sealed class FigureGenerator(B3Reference reference, DomainFileSet domain)
{
    // Written to be read: accented Portuguese and the comparison operators in a rule stay as
    // themselves rather than as \u escapes, and an attribute that says nothing about a property
    // leaves it out instead of writing null. The relaxed encoder is safe here because these files
    // are read by the compiler, never interpolated into a page.
    private static readonly JsonSerializerOptions Output = new(DomainFile.Json)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly Dictionary<string, FieldDto> _fragmentFields = IndexFragments(domain);
    private readonly HashSet<string> _curated = domain.Figures
        .Where(f => !f.RelativePath.Contains(DomainFileLoader.GeneratedFolder, StringComparison.Ordinal))
        .Select(f => f.File.FigureCode ?? string.Empty)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Figures B3 lists but whose field annex it has withdrawn from the manual.</summary>
    public List<string> WithoutAnnex { get; } = [];

    /// <summary>Figures already modelled by hand, left untouched.</summary>
    public List<string> Curated { get; } = [];

    public IReadOnlyList<GeneratedFigure> Generate(string domainDirectory)
    {
        var directory = Path.Combine(domainDirectory, "figures", DomainFileLoader.GeneratedFolder);
        Directory.CreateDirectory(directory);

        // The folder is rewritten wholesale: a figure that leaves B3's catalogue, or gets
        // promoted to a curated file, must not survive as a stale generated copy.
        foreach (var stale in Directory.EnumerateFiles(directory, "*.json")) File.Delete(stale);

        var written = new List<GeneratedFigure>();

        foreach (var figure in reference.Figures.OrderBy(f => f.Code, StringComparer.Ordinal))
        {
            if (_curated.Contains(figure.Code)) { Curated.Add(figure.Code); continue; }

            var rows = reference.FigureFields.Fields(figure.Code);
            if (rows.Count == 0) { WithoutAnnex.Add(figure.Code); continue; }

            var (file, report) = Build(figure, rows);
            var path = Path.Combine(directory, $"{figure.Code.ToLowerInvariant()}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(file, Output) + Environment.NewLine);

            written.Add(report with { RelativePath = Path.GetRelativePath(domainDirectory, path).Replace('\\', '/') });
        }

        return written;
    }

    private (DomainFile File, GeneratedFigure Report) Build(B3Figure figure, IReadOnlyList<B3FigureField> rows)
    {
        var drafts = new List<DraftField>();
        var inherited = 0;
        var skipped = 0;
        var fromExport = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var accountedFor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inheritedSections = new HashSet<string>(StringComparer.Ordinal);
        var order = 0;

        // What B3 publishes for this figure, keyed on its own name for each attribute. The annex
        // states an attribute's precision in prose — "Numérico com 4 inteiros e 8 decimais" — and
        // the export states it as data; where both speak, the export wins.
        var published = reference.FigureAttributesByName(figure.Code);

        foreach (var row in rows.OrderBy(r => r.Ordinal))
        {
            if (!FieldInterpreter.IsField(row)) { skipped++; continue; }

            // An attribute a common fragment already carries is inherited, not restated: the
            // curated version has the labels, defaults and rules this generator cannot infer.
            if (Vocabulary.CoveredBy(Vocabulary.Normalize(row.Label)) is { } covered)
            {
                inherited++;
                inheritedSections.Add(SectionOf(covered));
                continue;
            }

            order += 10;
            var draft = FieldInterpreter.Interpret(row, order);
            if (!seen.Add($"{draft.Placement.Section}.{draft.Placement.Key}")) { skipped++; continue; }

            var matched = ApplyPublishedMetadata(draft, figure.Code, published);
            if (matched is not null) accountedFor.Add(matched.FieldCode);

            draft.Path = $"{draft.Placement.Section}.{draft.Placement.Key}";
            drafts.Add(draft);
        }

        // Whatever B3 publishes for the figure and the annex never mentioned. Written from the
        // export alone, so the figure can carry every attribute its registration may need.
        foreach (var attribute in reference.FigureAttributes(figure.Code))
        {
            if (accountedFor.Contains(attribute.FieldCode)) continue;
            if (Vocabulary.CoveredBy(Vocabulary.Normalize(attribute.Name)) is { } coveredByFragment)
            {
                inherited++;
                inheritedSections.Add(SectionOf(coveredByFragment));
                continue;
            }

            order += 10;
            var draft = FromPublished(attribute, order);
            if (!seen.Add($"{draft.Placement.Section}.{draft.Placement.Key}")) { skipped++; continue; }

            drafts.Add(draft);
            fromExport++;
        }

        var sections = BuildSections(drafts);
        var rules = BuildRules(figure, drafts);

        var file = new DomainFile
        {
            FigureCode = figure.Code,
            FigureName = figure.Name,
            CommercialName = figure.Name,
            Description = new LocalizedTextDto
            {
                Pt = $"Figura {figure.Code} do catálogo da B3. Os atributos abaixo são os que a B3 "
                   + "registra para esta figura, conforme o anexo “Descrição dos campos das figuras” "
                   + "do Manual de Operações — COE.",
                En = $"B3 catalogue figure {figure.Code}. The attributes below are the ones B3 registers "
                   + "for it, from the field annex of the Manual de Operações — COE."
            },
            // B3 does not publish the modality per figure, so neither is asserted here: a figure
            // is offered under both and the desk's own rules decide, rather than this file
            // inventing a restriction B3 never stated.
            Modalities = ["VNP", "VNR"],
            UnderlyingClasses = [.. UnderlyingClasses],
            Extends = [.. BuildExtends(drafts, inheritedSections)],
            Sections = sections,
            Rules = rules
        };

        var fieldCount = sections.Sum(s => s.Fields.Count);
        return (file, new GeneratedFigure(figure.Code, figure.Name, string.Empty, fieldCount, inherited, skipped, rules.Count, fromExport));
    }

    /// <summary>
    /// The published attribute this drafted one describes: B3's own name for it first, then the
    /// same name reduced to the words that carry its meaning. The annex and the export write the
    /// same attribute differently often enough — "Data de verificação amortização 1" against
    /// "Data verificação amortização 1" — that a name match alone leaves a whole figure unmapped.
    /// </summary>
    private B3FigureAttribute? Match(
        string label, string figureCode, IReadOnlyDictionary<string, B3FigureAttribute> published) =>
        published.GetValueOrDefault(B3DerivativeFields.NormalizeName(label))
        ?? reference.FigureAttributeLike(figureCode, label);

    /// <summary>
    /// Corrects a drafted attribute against B3's published metadata for the same figure.
    ///
    /// The annex describes an attribute in a sentence, and reading a sentence is guesswork: it
    /// gives the precision as "4 inteiros e 8 decimais" in most places, as "8 decimais" alone in
    /// others, and sometimes not at all, in which case the interpreter falls back to text.
    /// <c>DTpFigurasDadosDerivativo</c> states the type, the precision, the size, the mandatory
    /// flag and — for a domain — the accepted values, as data, per figure. Where a name matches,
    /// those come from there, and the prose is left to do what it is good at: explaining the field.
    ///
    /// The bounds are not touched. B3's export says how many integer digits a number has; it
    /// does not say a participation may not exceed 1,000%, which the annex does.
    /// </summary>
    /// <returns>The attribute that was applied, so the caller knows it is accounted for.</returns>
    private B3FigureAttribute? ApplyPublishedMetadata(
        DraftField draft, string figureCode, IReadOnlyDictionary<string, B3FigureAttribute> published)
    {
        if (published.Count == 0) return null;

        var attribute = Match(draft.Source.Label, figureCode, published);
        if (attribute is null) return null;

        var field = draft.Field;

        switch (attribute.DataType.ToUpperInvariant())
        {
            case "NUMERICO":
                // A number the prose failed to describe was drafted as text. B3 says otherwise,
                // and a registration would be rejected either way, so B3's type is taken --
                // percent where the field is already one, decimal otherwise.
                if (field.DataType is not ("percent" or "money" or "integer")) field.DataType = "decimal";
                field.Decimals = attribute.Decimals;
                field.MaxLength = null;
                break;

            case "TEXTO":
                if (field.DataType is not ("string" or "text")) field.DataType = "string";
                field.Decimals = null;
                if (attribute.Length > 0) field.MaxLength = attribute.Length;
                break;

            case "DATA":
                if (field.DataType != "date") return null;
                break;

            case "DOMINIO":
                // The annex lists a domain's values only sometimes, and drafts the field as text
                // when it does not. The export always lists them, with the code B3 registers
                // each under, so a field with none takes both from there.
                if (field.Options.Count == 0 && attribute.DomainValues.Count > 0)
                {
                    field.DataType = "enum";
                    field.Decimals = null;
                    field.MaxLength = null;
                    field.Options = OptionsOf(attribute);
                }
                else if (field.DataType is not ("enum" or "enumSet" or "boolean"))
                {
                    return null;
                }
                break;

            default:
                return null;
        }

        // B3's flag is the figure's own, and can be stricter than the annex's wording.
        if (attribute.Mandatory) field.Required = true;

        field.B3DataCode = attribute.FieldCode;
        return attribute;
    }

    /// <summary>The values a domain field accepts, with the code B3 registers each under.</summary>
    private static List<OptionDto> OptionsOf(B3FigureAttribute attribute) =>
    [
        .. attribute.DomainValues
            .Where(value => !string.IsNullOrWhiteSpace(value.Name))
            .DistinctBy(value => Vocabulary.OptionCode(value.Name), StringComparer.Ordinal)
            .Select(value => new OptionDto
            {
                Code = Vocabulary.OptionCode(value.Name),
                B3Code = value.Code,
                Label = new LocalizedTextDto { Pt = value.Name }
            })
    ];

    /// <summary>
    /// Drafts an attribute B3 publishes for the figure that the annex never described.
    ///
    /// The annex is a table in a manual and it has gaps — 80 of the 180 attributes B3 registers
    /// for the basket-of-options figure are simply not in it. Everything a field needs is in the
    /// export: the name, the type, the precision or the size, whether it is mandatory, and the
    /// values a domain accepts. What is missing is the prose, so the help text says where the
    /// field came from rather than pretending to an explanation.
    /// </summary>
    private static DraftField FromPublished(B3FigureAttribute attribute, int order)
    {
        var normalized = Vocabulary.Normalize(attribute.Name);
        var placement = Vocabulary.Place(normalized);

        var field = new FieldDto
        {
            Key = placement.Key,
            Order = order,
            Label = new LocalizedTextDto { Pt = attribute.Name },
            Help = new LocalizedTextDto
            {
                Pt = "Atributo publicado pela B3 para esta figura em DTpFigurasDadosDerivativo. "
                   + "O anexo do Manual de Operações não o descreve, portanto não há instrução de preenchimento.",
                En = "An attribute B3 publishes for this figure in DTpFigurasDadosDerivativo. The annex of "
                   + "the Manual de Operações does not describe it, so there is no filling instruction."
            },
            B3Field = attribute.Name,
            B3DataCode = attribute.FieldCode,
            Required = attribute.Mandatory,
            DataType = "string"
        };

        switch (attribute.DataType.ToUpperInvariant())
        {
            case "NUMERICO":
                // The percent sign survives normalization precisely so it can be read here: it
                // is what separates a rate from a count.
                field.DataType = normalized.Contains('%') ? "percent" : "decimal";
                field.Decimals = attribute.Decimals;
                field.Max = UpperBound(attribute.Length);
                break;

            case "DATA":
                field.DataType = "date";
                break;

            case "DOMINIO":
                field.DataType = "enum";
                field.Options = OptionsOf(attribute);
                break;

            default:
                if (attribute.Length > 0) field.MaxLength = attribute.Length;
                break;
        }

        return new DraftField
        {
            Source = new B3FigureField(attribute.FigureCode, attribute.Position, attribute.Name, string.Empty),
            NormalizedLabel = normalized,
            Placement = placement,
            Field = field,
            Path = $"{placement.Section}.{placement.Key}"
        };
    }

    /// <summary>The largest value a number of <paramref name="integerDigits"/> digits can hold.</summary>
    private static decimal? UpperBound(int integerDigits)
    {
        if (integerDigits is < 1 or > 18) return null;

        var bound = 1m;
        for (var i = 0; i < integerDigits; i++) bound *= 10m;
        return bound - 1m;
    }

    private static List<string> BuildExtends(List<DraftField> drafts, IReadOnlySet<string> inheritedSections)
    {
        var extends = new List<string>
        {
            "common/identification", "common/underlying", "common/remuneration", "common/settlement"
        };

        // A figure with barrier attributes gets the curated barrier block, so its own barrier
        // levels sit beside the direction, verification period and window the fragment defines.
        //
        // Inherited attributes count as much as drafted ones. A figure whose only barrier
        // attributes are the verification period and window is entirely served by the fragment
        // and drafts nothing into the section — and without this would end up not extending the
        // fragment either, unable to carry attributes B3 registers for it.
        if (drafts.Any(d => d.Placement.Section == "barriers") || inheritedSections.Contains("barriers"))
            extends.Insert(3, "common/barriers");

        return extends;
    }

    /// <summary>The section of a path like <c>barriers.verificationPeriod</c>.</summary>
    private static string SectionOf(string path)
    {
        var cut = path.IndexOf('.', StringComparison.Ordinal);
        return cut < 0 ? path : path[..cut];
    }

    private List<SectionDto> BuildSections(List<DraftField> drafts)
    {
        var sections = new List<SectionDto>();

        foreach (var group in drafts.GroupBy(d => d.Placement.Section).OrderBy(g => SectionOrder(g.Key)))
        {
            var meta = SectionMeta(group.Key);
            sections.Add(new SectionDto
            {
                Key = group.Key,
                Kind = "tab",
                // Left at zero for a section a fragment already provides: the compiler keeps the
                // fragment's own order rather than letting this file reshuffle the tabs.
                Order = meta is null ? 0 : meta.Value.Order,
                Label = meta is null ? null : new LocalizedTextDto { Pt = meta.Value.Pt, En = meta.Value.En },
                Fields = [.. group.Select(d => d.Field)]
            });
        }

        return sections;
    }

    /// <summary>
    /// Turns the conditions the annex states in prose into expressions.
    ///
    /// Only two forms are mechanical enough to be safe: a requirement that hangs on a listed
    /// domain value, and a date that must follow another. Everything else stays in the help text
    /// where a person reads it, because a rule that is subtly wrong blocks a valid registration.
    /// </summary>
    private List<RuleDto> BuildRules(B3Figure figure, List<DraftField> drafts)
    {
        var rules = new List<RuleDto>();
        var prefix = figure.Code.ToLowerInvariant();

        foreach (var draft in drafts)
        {
            if (draft.RequiredWhenValue is { } value && ResolveOptionCondition(value, drafts) is { } condition)
                draft.Field.RequiredWhen = condition;

            if (draft.PositiveOnly && draft.Field.DataType is "percent" or "decimal")
            {
                rules.Add(new RuleDto
                {
                    Id = $"{prefix}.{draft.Field.Key}-positive",
                    Targets = [draft.Path],
                    Assert = $"{draft.Path} > 0",
                    Message = new LocalizedTextDto
                    {
                        Pt = $"“{draft.Source.Label}” deve ser maior que zero.",
                        En = $"“{draft.Source.Label}” must be greater than zero."
                    },
                    Severity = "error",
                    Execution = "both",
                    Trigger = "change"
                });
            }

            if (draft.MustBeAfter is { } earlierLabel && ResolvePath(earlierLabel, drafts) is { } earlier)
            {
                rules.Add(new RuleDto
                {
                    Id = $"{prefix}.{draft.Field.Key}-after-{Vocabulary.Camel(Vocabulary.Normalize(earlierLabel))}",
                    Targets = [draft.Path],
                    Assert = $"{draft.Path} > {earlier}",
                    Message = new LocalizedTextDto
                    {
                        Pt = $"“{draft.Source.Label}” deve ser posterior a “{earlierLabel}”.",
                        En = $"“{draft.Source.Label}” must fall after “{earlierLabel}”."
                    },
                    Severity = "error",
                    Execution = "both",
                    Trigger = "change"
                });
            }
        }

        return rules;
    }

    /// <summary>
    /// "obrigatório, se indicado 'Janela de datas'" names a value, not a field. The field is
    /// whichever enum in this figure — its own or an inherited one — offers that value.
    /// </summary>
    private string? ResolveOptionCondition(string value, List<DraftField> drafts)
    {
        var wanted = Vocabulary.Normalize(value);

        foreach (var draft in drafts)
        {
            var match = draft.Field.Options.FirstOrDefault(o =>
                o.Label is not null && Vocabulary.Normalize(o.Label.Pt) == wanted);
            if (match is not null) return $"{draft.Path} == '{match.Code}'";
        }

        foreach (var (path, field) in _fragmentFields)
        {
            var match = field.Options.FirstOrDefault(o =>
                o.Label is not null && Vocabulary.Normalize(o.Label.Pt) == wanted);
            if (match is not null) return $"{path} == '{match.Code}'";
        }

        return null;
    }

    /// <summary>Resolves a field B3 refers to by its printed label.</summary>
    private string? ResolvePath(string label, List<DraftField> drafts)
    {
        var wanted = Vocabulary.Normalize(label);

        if (Vocabulary.CoveredBy(wanted) is { } covered) return covered;

        return drafts.FirstOrDefault(d => d.NormalizedLabel == wanted)?.Path;
    }

    private static Dictionary<string, FieldDto> IndexFragments(DomainFileSet domain)
    {
        var index = new Dictionary<string, FieldDto>(StringComparer.Ordinal);

        foreach (var fragment in domain.Fragments.Values)
            foreach (var section in fragment.Sections)
                foreach (var field in section.Fields.Concat(section.ItemFields))
                    index.TryAdd($"{section.Key}.{field.Key}", field);

        return index;
    }

    private static int SectionOrder(string key) => SectionMeta(key)?.Order ?? 35;

    private static (int Order, string Pt, string En)? SectionMeta(string key) => key switch
    {
        "assets" => (18, "Ativos", "Underlyings"),
        "payoff" => (30, "Payoff", "Payoff"),
        "observations" => (45, "Observações", "Observations"),
        "amortization" => (48, "Amortização", "Amortisation"),
        "credit" => (55, "Evento de crédito", "Credit event"),
        // barriers and remuneration come from a fragment; their labels stay with it.
        _ => null
    };

    private static readonly string[] UnderlyingClasses =
    [
        "ACOES", "ACOES INTERNACIONAIS", "INDICES", "INDICES INTERNACIONAIS", "TAXAS DE CAMBIO",
        "COMMODITIES", "JUROS", "JUROS INTERNACIONAIS", "TITULOS PUBLICOS", "TITULOS PRIVADOS", "CESTA"
    ];
}
