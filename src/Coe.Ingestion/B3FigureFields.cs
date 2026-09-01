using System.Text.RegularExpressions;

namespace Coe.Ingestion;

/// <summary>One row of the figure-field annex: an attribute B3 registers for a figure.</summary>
/// <param name="FigureCode">The figure the attribute belongs to.</param>
/// <param name="Ordinal">Position in the annex table, which is the order of the registration screen.</param>
/// <param name="Label">B3's own field name, as printed.</param>
/// <param name="Description">The full instruction text: mandatory flag, format, domain and conditions.</param>
public sealed record B3FigureField(string FigureCode, int Ordinal, string Label, string Description);

/// <summary>
/// The "Anexo — Descrição dos campos das figuras" of B3's <em>Manual de Operações — COE</em>,
/// extracted to <c>reference/b3/campos-figuras.csv</c> by <c>tools/b3-annex/extract.py</c>.
///
/// This is the only published source that says which attributes belong to which figure. The
/// strategy-field dictionary (<c>DTpDadosEstrategia</c>) is a flat catalogue of 5,503 attributes
/// with no figure association and its own naming, so the mapping cannot be recovered from the
/// CSV exports alone — which is why the annex is extracted once, by hand, and committed.
/// </summary>
public sealed partial class B3FigureFields
{
    public const string FieldsFile = "campos-figuras.csv";

    private readonly Dictionary<string, List<B3FigureField>> _byFigure = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _titles = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> FigureCodes => _byFigure.Keys;
    public IReadOnlyList<string> Errors { get; private set; } = [];

    public static B3FigureFields Empty { get; } = new();

    public IReadOnlyList<B3FigureField> Fields(string figureCode) =>
        _byFigure.TryGetValue(figureCode, out var fields) ? fields : [];

    /// <summary>The annex's own heading for the figure, e.g. "COE001005 - Call Spread".</summary>
    public string? Title(string figureCode) => _titles.GetValueOrDefault(figureCode);

    public static B3FigureFields Load(string directory)
    {
        var annex = new B3FigureFields();
        var path = Path.Combine(directory, FieldsFile);
        if (!File.Exists(path))
        {
            annex.Errors = [$"{FieldsFile} not found in {directory}."];
            return annex;
        }

        var errors = new List<string>();
        foreach (var line in File.ReadAllLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Descriptions carry the quotes B3 puts around domain values, so the file is real
            // CSV with quoted fields rather than the plain semicolon split the exports use.
            var parts = SplitCsv(line);
            if (parts.Count < 5)
            {
                errors.Add($"{FieldsFile}: row '{line[..Math.Min(60, line.Length)]}' has too few columns.");
                continue;
            }

            var code = parts[0].Trim();
            if (!int.TryParse(parts[2], out var ordinal)) continue;

            _ = annex._titles.TryAdd(code, parts[1].Trim());
            if (!annex._byFigure.TryGetValue(code, out var list)) annex._byFigure[code] = list = [];
            list.Add(new B3FigureField(code, ordinal, parts[3].Trim(), parts[4].Trim()));
        }

        annex.Errors = errors;
        return annex;
    }

    /// <summary>
    /// The code the annex gives a figure the catalogue export names without one. Two rows of
    /// <c>DTpFiguras</c> read "COE de Crédito – CDS com Amortização" and its TRS twin with no
    /// code at all; the annex heads the same figures "COE001085" and "COE001086".
    /// </summary>
    public string? CodeForName(string name)
    {
        var wanted = Normalize(name);
        if (wanted.Length == 0) return null;

        foreach (var (code, title) in _titles)
        {
            var normalized = Normalize(title);
            // The annex title is the code followed by the same name.
            if (normalized.EndsWith(wanted, StringComparison.Ordinal)) return code;
        }

        return null;
    }

    private static string Normalize(string text) =>
        WhitespacePattern().Replace(text.Replace('–', '-').Replace('—', '-'), " ").Trim().ToUpperInvariant();

    /// <summary>Minimal RFC 4180 reader: the annex file is written by the extraction script.</summary>
    private static List<string> SplitCsv(string line)
    {
        var parts = new List<string>();
        var value = new System.Text.StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c != '"') { value.Append(c); continue; }
                if (i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; continue; }
                quoted = false;
            }
            else if (c == '"') quoted = true;
            else if (c == ';') { parts.Add(value.ToString()); value.Clear(); }
            else value.Append(c);
        }

        parts.Add(value.ToString());
        return parts;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
