using System.Globalization;
using System.Text.RegularExpressions;

namespace Coe.Ingestion;

/// <summary>One row of B3's figure catalogue (<c>DTpFiguras</c>).</summary>
/// <param name="Ordinal">The two-digit sequence B3 lists the figure under.</param>
/// <param name="Code">The figure code, e.g. <c>COE001005</c>.</param>
/// <param name="Name">The registered name, e.g. "Call Spread".</param>
/// <param name="Calculated">Whether B3 calculates the settlement for this figure.</param>
public sealed record B3Figure(string Ordinal, string Code, string Name, bool Calculated);

/// <summary>One value of a registration domain (<c>Dominios</c>).</summary>
public sealed record B3DomainValue(
    string DomainType, string Name, string? Description, string Code, bool Enabled, string? InstrumentType);

/// <summary>One attribute of B3's strategy-field dictionary (<c>DTpDadosEstrategia</c>).</summary>
public sealed record B3StrategyField(
    string Code, string Name, string DataType, int Length, int Decimals, bool Mandatory)
{
    /// <summary>Accepted values, for a <c>DOMINIO</c> field.</summary>
    public List<string> DomainValues { get; } = [];
}

/// <summary>One row of the underlying-asset master (<c>Ativos Subjacentes</c>).</summary>
public sealed record B3Underlying(
    string AssetClass, string Code, string Exchange, string ValuationIndex,
    string Currency, string? Ticker, bool Calculated, string InstrumentType);

/// <summary>
/// B3's published reference data, read from the exports committed under <c>reference/b3/</c>.
///
/// These are the authority for what a registration may contain: which figures exist, which
/// domain values a field may carry, which underlyings are eligible. The compiler checks the
/// domain files against them, so a figure renamed by B3 or an option code that no longer exists
/// fails ingestion instead of being discovered when a registration is rejected.
///
/// Refreshing is dropping in a newer export: the files are the interface, not a migration.
/// </summary>
public sealed partial class B3Reference
{
    public const string FiguresFile = "figuras.csv";
    public const string DomainsFile = "dominios-derivativos.csv";
    public const string StrategyFieldsFile = "dados-estrategia.csv";
    public const string UnderlyingsFile = "ativos-subjacentes.csv";

    /// <summary>Instrument-type marker B3 uses for COE rows.</summary>
    public const string CoeInstrumentType = "COE";

    private readonly Dictionary<string, B3Figure> _figuresByCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<B3DomainValue>> _domains = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, B3StrategyField> _strategyFields = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<B3Figure> Figures => _figuresByCode.Values;
    public IReadOnlyDictionary<string, List<B3DomainValue>> Domains => _domains;
    public IReadOnlyDictionary<string, B3StrategyField> StrategyFields => _strategyFields;
    public IReadOnlyList<B3Underlying> Underlyings { get; private set; } = [];
    public IReadOnlyList<string> Errors { get; private set; } = [];

    /// <summary>The export's own as-of stamp, when the file carries one.</summary>
    public string? AsOf { get; private set; }

    /// <summary>The per-figure attribute lists from the Manual de Operações annex.</summary>
    public B3FigureFields FigureFields { get; private set; } = B3FigureFields.Empty;

    /// <summary>
    /// B3's derivative-data dictionary and its per-figure attribute lists. This is the
    /// authority for what a figure registers and what the variable-data record of the
    /// <em>Registro COE</em> file carries; the annex above is the prose that explains it.
    /// </summary>
    public B3DerivativeFields DerivativeFields { get; private set; } = B3DerivativeFields.Empty;

    public static B3Reference Empty { get; } = new();

    public B3Figure? Figure(string code) => _figuresByCode.GetValueOrDefault(code);

    public IReadOnlyList<B3DomainValue> Domain(string domainType) =>
        _domains.TryGetValue(domainType, out var values) ? values : [];

    public B3StrategyField? StrategyField(string code) => _strategyFields.GetValueOrDefault(code);

    /// <summary>The attributes B3 registers for a figure, from <c>DTpFigurasDadosDerivativo</c>.</summary>
    public IReadOnlyList<B3FigureAttribute> FigureAttributes(string figureCode) =>
        DerivativeFields.ForFigure(figureCode);

    /// <summary>One field of the derivative-data dictionary, by its <c>C…</c> identifier.</summary>
    public B3DerivativeField? DerivativeField(string code) => DerivativeFields.Field(code);

    /// <summary>A figure's attributes, keyed on the normalised form of B3's own field name.</summary>
    public IReadOnlyDictionary<string, B3FigureAttribute> FigureAttributesByName(string figureCode) =>
        DerivativeFields.AttributesByName(figureCode);

    /// <summary>
    /// The attribute of <paramref name="figureCode"/> whose name reduces to the same words as
    /// <paramref name="name"/>, when exactly one does. Weaker than a name match, and used only
    /// after one fails.
    /// </summary>
    public B3FigureAttribute? FigureAttributeLike(string figureCode, string? name) =>
        DerivativeFields.AttributeBySignature(figureCode, B3DerivativeFields.SignatureOf(name));

    /// <summary>
    /// The numbered run of attributes a figure registers under one concept, in order — what a
    /// repeating section's rows map onto.
    /// </summary>
    public IReadOnlyList<B3FigureAttribute> FigureAttributeSeries(string figureCode, string concept) =>
        DerivativeFields.SeriesFor(figureCode, concept);

    /// <summary>
    /// Reads whatever is present. A missing directory is not an error: the platform still
    /// compiles and runs, it simply cannot cross-check against B3's catalogue.
    /// </summary>
    public static B3Reference Load(string directory)
    {
        var reference = new B3Reference();
        var errors = new List<string>();

        if (!Directory.Exists(directory))
        {
            reference.Errors = [$"B3 reference directory not found: {directory}"];
            return reference;
        }

        // The annex is loaded first: it carries the codes for the catalogue rows B3 exports
        // with a name and no code.
        reference.FigureFields = B3FigureFields.Load(directory);
        reference.LoadFigures(Path.Combine(directory, FiguresFile), errors);
        reference.LoadDomains(Path.Combine(directory, DomainsFile), errors);
        reference.LoadStrategyFields(Path.Combine(directory, StrategyFieldsFile), errors);
        reference.LoadUnderlyings(Path.Combine(directory, UnderlyingsFile), errors);

        // Last: the per-figure attribute lists are keyed on the sequence number, so the
        // catalogue has to be in place to turn that into a figure code. The sequences are
        // unique in every export published so far and nothing guarantees they stay that way;
        // a downloaded file must not be able to throw here, so a repeat is reported and the
        // first row keeps the sequence.
        var codeByOrdinal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var figure in reference._figuresByCode.Values)
        {
            if (figure.Ordinal.Length == 0) continue;
            if (!codeByOrdinal.TryAdd(figure.Ordinal, figure.Code))
                errors.Add($"{FiguresFile}: sequence '{figure.Ordinal}' is used by more than one figure.");
        }

        reference.DerivativeFields = B3DerivativeFields.Load(directory);
        reference.DerivativeFields.ResolveFigureCodes(codeByOrdinal);
        errors.AddRange(reference.DerivativeFields.Errors);

        reference.Errors = errors;
        return reference;
    }

    // ----- parsing -------------------------------------------------------------------

    private void LoadFigures(string path, List<string> errors)
    {
        if (!File.Exists(path)) { errors.Add($"{FiguresFile} not found."); return; }

        // Line 1 is the export date, line 2 the header; the code column carries
        // "COE001005 - Call Spread" rather than a code on its own.
        var lines = File.ReadAllLines(path);
        if (lines.Length > 0 && lines[0].Length == 8 && lines[0].All(char.IsDigit)) AsOf = lines[0];

        foreach (var line in lines.Skip(2))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(';');
            if (parts.Length < 3) continue;

            var (code, name) = SplitFigureLabel(parts[1]);
            if (code is null)
            {
                // Two rows carry only a name. The manual's field annex heads the same figures
                // with a code, so the code is recovered from there rather than invented here —
                // and rather than dropping figures B3 does publish.
                code = FigureFields.CodeForName(name);
                if (code is null)
                {
                    errors.Add($"{FiguresFile}: row '{parts[1]}' has no figure code and was skipped.");
                    continue;
                }
            }

            _figuresByCode[code] = new B3Figure(parts[0], code, name, IsYes(parts[2]));
        }
    }

    /// <summary>
    /// Splits B3's label column into a code and a name. The separator is not consistent in the
    /// published export — most rows read "COE001005 - Call Spread", but COE001060 has no space
    /// before the hyphen, COE001087 has no hyphen at all, and two rows carry only a name. So the
    /// code is matched as a token rather than found by splitting on punctuation.
    /// </summary>
    public static (string? Code, string Name) SplitFigureLabel(string label)
    {
        var text = label.Trim();
        var match = FigureCodePattern().Match(text);
        if (!match.Success) return (null, text);

        var code = match.Groups[1].Value;
        var name = text[match.Length..].Trim();
        return (code, name.Length == 0 ? code : name);
    }

    // "COE001005" followed by an optional hyphen, en dash or em dash, in any spacing.
    [GeneratedRegex(@"^(COE\d{6})\s*[-\u2013\u2014]?\s*", RegexOptions.CultureInvariant)]
    private static partial Regex FigureCodePattern();

    private void LoadDomains(string path, List<string> errors)
    {
        if (!File.Exists(path)) { errors.Add($"{DomainsFile} not found."); return; }

        foreach (var line in File.ReadAllLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(';');
            if (parts.Length < 6) continue;

            var value = new B3DomainValue(
                DomainType: parts[0].Trim(),
                Name: parts[1].Trim(),
                Description: string.IsNullOrWhiteSpace(parts[2]) ? null : parts[2].Trim(),
                Code: parts[3].Trim(),
                Enabled: IsYes(parts[4]),
                InstrumentType: string.IsNullOrWhiteSpace(parts[5]) ? null : parts[5].Trim());

            if (!_domains.TryGetValue(value.DomainType, out var list))
                _domains[value.DomainType] = list = [];
            list.Add(value);
        }
    }

    private void LoadStrategyFields(string path, List<string> errors)
    {
        if (!File.Exists(path)) { errors.Add($"{StrategyFieldsFile} not found."); return; }

        // One row per field, plus one extra row per accepted value for DOMINIO fields.
        foreach (var line in File.ReadAllLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(';');
            if (parts.Length < 6) continue;

            var code = parts[0].Trim();
            if (!_strategyFields.TryGetValue(code, out var field))
            {
                _strategyFields[code] = field = new B3StrategyField(
                    code,
                    parts[1].Trim(),
                    parts[2].Trim(),
                    ParseInt(parts[3]),
                    ParseInt(parts[4]),
                    IsYes(parts[5]));
            }

            if (parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6]))
                field.DomainValues.Add(parts[6].Trim());
        }
    }

    private void LoadUnderlyings(string path, List<string> errors)
    {
        if (!File.Exists(path)) { errors.Add($"{UnderlyingsFile} not found."); return; }

        var underlyings = new List<B3Underlying>();
        foreach (var line in File.ReadAllLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(';');
            if (parts.Length < 16) continue;

            underlyings.Add(new B3Underlying(
                AssetClass: parts[0].Trim(),
                Code: parts[1].Trim(),
                Exchange: parts[2].Trim(),
                ValuationIndex: parts[3].Trim(),
                Currency: parts[8].Trim(),
                Ticker: string.IsNullOrWhiteSpace(parts[14]) ? null : parts[14].Trim(),
                Calculated: IsYes(parts[10]),
                InstrumentType: parts[15].Trim()));
        }
        Underlyings = underlyings;
    }

    private static bool IsYes(string value)
    {
        var text = value.Trim();
        return text.Equals("S", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("SIM", StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseInt(string value) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
}
