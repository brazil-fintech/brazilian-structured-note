using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Coe.Ingestion;

namespace Coe.DomainGen;

/// <summary>What one figure's generation produced, for the run report.</summary>
public sealed record GeneratedFigure(
    string Code, string Name, string RelativePath, int Fields, int Inherited, int Skipped, int Rules);

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
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var order = 0;

        foreach (var row in rows.OrderBy(r => r.Ordinal))
        {
            if (!FieldInterpreter.IsField(row)) { skipped++; continue; }

            // An attribute a common fragment already carries is inherited, not restated: the
            // curated version has the labels, defaults and rules this generator cannot infer.
            if (Vocabulary.CoveredBy(Vocabulary.Normalize(row.Label)) is not null) { inherited++; continue; }

            order += 10;
            var draft = FieldInterpreter.Interpret(row, order);
            if (!seen.Add($"{draft.Placement.Section}.{draft.Placement.Key}")) { skipped++; continue; }

            draft.Path = $"{draft.Placement.Section}.{draft.Placement.Key}";
            drafts.Add(draft);
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
            Extends = [.. BuildExtends(drafts)],
            Sections = sections,
            Rules = rules
        };

        var fieldCount = sections.Sum(s => s.Fields.Count);
        return (file, new GeneratedFigure(figure.Code, figure.Name, string.Empty, fieldCount, inherited, skipped, rules.Count));
    }

    private static List<string> BuildExtends(List<DraftField> drafts)
    {
        var extends = new List<string>
        {
            "common/identification", "common/underlying", "common/remuneration", "common/settlement"
        };

        // A figure with barrier attributes gets the curated barrier block, so its own barrier
        // levels sit beside the direction, verification period and window the fragment defines.
        if (drafts.Any(d => d.Placement.Section == "barriers")) extends.Insert(3, "common/barriers");

        return extends;
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
