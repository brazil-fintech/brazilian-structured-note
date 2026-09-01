using System.Globalization;
using System.Text.RegularExpressions;
using Coe.Ingestion;

namespace Coe.DomainGen;

/// <summary>A rule the annex text states in prose and the generator can state as an expression.</summary>
public sealed record DraftRule(string Id, string Target, string Assert, string MessagePt, string Severity = "error");

/// <summary>
/// One interpreted annex row, before cross-field references are resolved. The conditions B3
/// writes as prose ("obrigatório, se indicado 'Janela de datas'") name a value, not a field, so
/// they can only be turned into an expression once every field of the figure is known.
/// </summary>
public sealed class DraftField
{
    public required B3FigureField Source { get; init; }
    public required string NormalizedLabel { get; init; }
    public required Placement Placement { get; init; }
    public required FieldDto Field { get; init; }

    /// <summary>Path this attribute ends up at, filled in once the section is known.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Domain value the requirement hangs on, e.g. "Janela de datas".</summary>
    public string? RequiredWhenValue { get; init; }

    /// <summary>Label of the field this one must come after, e.g. "Data Inicial para Fixing".</summary>
    public string? MustBeAfter { get; init; }

    /// <summary>True when the annex says the value must be above zero.</summary>
    public bool PositiveOnly { get; init; }
}

/// <summary>
/// Turns one annex row into an attribute.
///
/// B3 writes each row the same way — whether the field is mandatory, the numeric format, the
/// accepted domain, and the conditions — so the type, size, decimals and options are read out of
/// that text rather than guessed from the label. What cannot be expressed is kept verbatim as
/// the attribute's help, because the booking desk reads the same sentences.
/// </summary>
public static partial class FieldInterpreter
{
    /// <summary>
    /// Rows of the annex that are notes, section captions or extraction residue rather than
    /// fields. They carry no "Campo de preenchimento…" instruction and read as prose.
    /// </summary>
    public static bool IsField(B3FigureField row)
    {
        if (row.Label.Length is 0 or > 70) return false;
        if (row.Description.Length == 0) return false;

        var label = Vocabulary.Normalize(row.Label);
        if (label.Length == 0 || label.Split(' ').Length > 12) return false;

        // Captions that head a block of fields inside one figure's table.
        if (CaptionPattern().IsMatch(label)) return false;

        return MandatoryPattern().IsMatch(row.Description) || FormatPattern().IsMatch(row.Description);
    }

    public static DraftField Interpret(B3FigureField row, int order)
    {
        var normalized = Vocabulary.Normalize(row.Label);
        var placement = Vocabulary.Place(normalized);
        var text = row.Description;

        var options = ReadOptions(text);
        var (dataType, decimals, max, maxLength) = ReadFormat(text, normalized, options.Count > 0);

        var conditional = ConditionalMandatoryPattern().Match(text);
        var required = MandatoryPattern().IsMatch(text) && !conditional.Success;

        var field = new FieldDto
        {
            Key = placement.Key,
            Order = order,
            Label = new LocalizedTextDto { Pt = row.Label },
            Help = new LocalizedTextDto { Pt = text },
            DataType = dataType,
            B3Field = row.Label,
            Required = required,
            Decimals = decimals,
            MaxLength = maxLength,
            Max = max,
            Options = options,
            // The annex spells this out on almost every fixing field: a basket has no single
            // quotation, so the per-asset dates and quote types do not apply to it.
            VisibleWhen = BasketExclusionPattern().IsMatch(text) ? "underlying.assetClass != 'CESTA'" : null
        };

        if (dataType is "percent" or "decimal" && PositivePattern().IsMatch(text)) field.Min = 0;

        return new DraftField
        {
            Source = row,
            NormalizedLabel = normalized,
            Placement = placement,
            Field = field,
            RequiredWhenValue = conditional.Success ? conditional.Groups["value"].Value.Trim() : null,
            MustBeAfter = ReadMustBeAfter(text),
            PositiveOnly = PositivePattern().IsMatch(text)
        };
    }

    /// <summary>
    /// "Formato: Numérico percentual com 4 inteiros e 8 decimais" carries the type, the range and
    /// the precision in one sentence; "DD/MM/AAAA" marks a date; a listed domain makes it an enum.
    /// </summary>
    private static (string DataType, int? Decimals, decimal? Max, int? MaxLength) ReadFormat(
        string text, string normalizedLabel, bool hasOptions)
    {
        if (DatePattern().IsMatch(text)) return ("date", null, null, null);

        var numeric = NumericPattern().Match(text);
        if (numeric.Success)
        {
            var integers = int.Parse(numeric.Groups["int"].Value, CultureInfo.InvariantCulture);
            var decimals = int.Parse(numeric.Groups["dec"].Value, CultureInfo.InvariantCulture);
            var percent = numeric.Groups["percent"].Success || normalizedLabel.EndsWith('%');

            // 4 integer places is a value below 10,000 — B3 states the width, not the bound.
            var max = integers is > 0 and <= 18
                ? (decimal)Math.Pow(10, integers) - 1
                : (decimal?)null;

            return (percent ? "percent" : "decimal", decimals, max, null);
        }

        if (hasOptions)
        {
            return IsYesNo(text) ? ("boolean", null, null, null) : ("enum", null, null, null);
        }

        var alphanumeric = AlphanumericPattern().Match(text);
        if (alphanumeric.Success)
            return ("string", null, null, int.Parse(alphanumeric.Groups["len"].Value, CultureInfo.InvariantCulture));

        return ("string", null, null, 100);
    }

    /// <summary>Reads "Campo com as opções: Data Única, Janela de Datas e Mais Datas."</summary>
    private static List<OptionDto> ReadOptions(string text)
    {
        var match = OptionsPattern().Match(text);
        if (!match.Success) return [];

        var list = match.Groups["list"].Value;
        var labels = SeparatorPattern().Split(list)
            .Select(part => part.Trim().Trim('"', '“', '”', '\'', '.', ';'))
            .Where(part => part.Length is > 0 and <= 60)
            .ToList();

        if (labels.Count is 0 or > 12) return [];

        var options = new List<OptionDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var label in labels)
        {
            var code = Vocabulary.OptionCode(label);
            if (!seen.Add(code)) continue;
            options.Add(new OptionDto { Code = code, Label = new LocalizedTextDto { Pt = label } });
        }

        return options;
    }

    private static bool IsYesNo(string text)
    {
        var options = ReadOptions(text);
        return options.Count == 2 && options.All(o => o.Code is "SIM" or "NAO");
    }

    /// <summary>Reads 'deve ser posterior à indicada no campo "Data Inicial para Fixing"'.</summary>
    private static string? ReadMustBeAfter(string text)
    {
        var match = AfterPattern().Match(text);
        return match.Success ? match.Groups["field"].Value.Trim() : null;
    }

    [GeneratedRegex(@"preenchimento obrigat[óo]rio|campo obrigat[óo]rio|é de preenchimento", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MandatoryPattern();

    // "obrigatório, se indicado "Janela de datas"" — the value the requirement hangs on.
    [GeneratedRegex(@"obrigat[óo]rio,?\s*(se|quando|caso)[^.""“]*[""“](?<value>[^""”]+)[""”]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConditionalMandatoryPattern();

    [GeneratedRegex(@"Formato:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FormatPattern();

    [GeneratedRegex(@"DD/MM/(AAAA|YYYY)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"num[ée]rico\s*(?<percent>percentual)?\s*com\s*(?<int>\d+)\s*inteiros?\s*e\s*(?<dec>\d+)\s*decima", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumericPattern();

    [GeneratedRegex(@"alfanum[ée]rico[^.\d]*(?<len>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AlphanumericPattern();

    [GeneratedRegex(@"Campo com (as op[çc][õo]es|os dom[íi]nios|as op[çc][õo]es de dom[íi]nio):\s*(?<list>[^.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OptionsPattern();

    [GeneratedRegex(@",\s*|\s+e\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorPattern();

    [GeneratedRegex(@"maior que 0|maior que zero", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PositivePattern();

    [GeneratedRegex(@"posterior [àa] (indicada )?no campo [""“](?<field>[^""”]+)[""”]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AfterPattern();

    [GeneratedRegex(@"N[ãa]o preencher se a [""“]?Classe do Ativo Subjacente[""”]? for igual a [""“]?CESTA", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BasketExclusionPattern();

    [GeneratedRegex(@"^(dados|informacoes|informacao|campos|nome do campo|dominios|observacao|para essa figura|para esta figura|destacamos|especificas|instrucoes|\d+ )", RegexOptions.CultureInvariant)]
    private static partial Regex CaptionPattern();
}
