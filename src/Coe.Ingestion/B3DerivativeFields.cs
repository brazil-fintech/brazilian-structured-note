using System.Globalization;
using System.Text;

namespace Coe.Ingestion;

/// <summary>One accepted value of a <c>DOMINIO</c> field: what B3 calls it, and the code it registers.</summary>
public sealed record B3DerivativeDomainValue(string Name, string? Code);

/// <summary>
/// One field of B3's derivative-data dictionary (<c>DTpTipoDadosDerivativo</c>) — the file the
/// <em>Registro COE</em> layout names for the "Identificador do Campo" of its variable-data
/// record.
/// </summary>
/// <param name="Code">The identifier written on the wire, e.g. <c>C0000032</c>.</param>
/// <param name="Name">B3's own field name, as the registration screen shows it.</param>
/// <param name="DataType"><c>NUMERICO</c>, <c>DATA</c>, <c>DOMINIO</c> or <c>TEXTO</c>.</param>
/// <param name="Length">Integer digits for a number, characters for text.</param>
/// <param name="Decimals">Decimal places for a number.</param>
/// <param name="Mandatory">Whether B3 requires it wherever the field appears.</param>
public sealed record B3DerivativeField(
    string Code, string Name, string DataType, int Length, int Decimals, bool Mandatory)
{
    public List<B3DerivativeDomainValue> DomainValues { get; } = [];

    public bool IsDomain => DataType.Equals("DOMINIO", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// One attribute of one figure, from <c>DTpFigurasDadosDerivativo</c>: the same field, with the
/// mandatory flag and the value list B3 applies to <em>this</em> figure, which can be narrower
/// than the dictionary's.
/// </summary>
public sealed record B3FigureAttribute(
    string Ordinal, string FieldCode, string Name,
    string DataType, int Length, int Decimals, bool Mandatory, int Position)
{
    /// <summary>
    /// The figure this attribute belongs to. Attached by <see cref="B3DerivativeFields.ResolveFigureCodes"/>
    /// once the catalogue is known, because two of the export's rows carry a name and no code.
    /// </summary>
    public string FigureCode { get; internal set; } = string.Empty;

    /// <summary>The values this figure accepts, with the codes taken from the dictionary.</summary>
    public List<B3DerivativeDomainValue> DomainValues { get; } = [];

    public bool IsDomain => DataType.Equals("DOMINIO", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The two exports that, together, say which attributes a COE figure registers and what each
/// one holds — published by B3 as data, at
/// <c>ftp://ftp.cetip.com.br/Public/AAAAMMDD_DTpTipoDadosDerivativo.txt</c> and
/// <c>…_DTpFigurasDadosDerivativo.txt</c>.
///
/// This is the association the platform previously had to read out of the prose annex of the
/// <em>Manual de Operações</em>. The exports carry it directly, for all 88 figures — including
/// the four the annex has no entry for — and carry with it the type, size, decimals, mandatory
/// flag and accepted values of every attribute, so a generated figure no longer depends on
/// parsing an instruction sentence.
///
/// It is a different dictionary from <c>DTpDadosEstrategia</c>, and the codes do not mean the
/// same thing in both: <c>C0000001</c> is "Strike 1(%)" here and "% Capital Protegido" there.
/// This is the one the registration file writes.
/// </summary>
public sealed class B3DerivativeFields
{
    public const string FieldsFile = "dados-derivativo.csv";
    public const string FigureFieldsFile = "figuras-dados-derivativo.csv";

    private readonly Dictionary<string, B3DerivativeField> _fields = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Keyed on the two-digit sequence B3 lists the figure under, which every row carries.</summary>
    private readonly Dictionary<string, List<B3FigureAttribute>> _byOrdinal = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<B3FigureAttribute>> _byFigure = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per figure, its attributes indexed on the normalised form of B3's own name.</summary>
    private readonly Dictionary<string, Dictionary<string, B3FigureAttribute>> _byFigureName =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The figure code read straight from the label column, where the row carries one.</summary>
    private readonly Dictionary<string, string> _codeByOrdinal = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, B3DerivativeField> Fields => _fields;
    public IReadOnlyList<string> Errors { get; private set; } = [];

    /// <summary>The export's own as-of stamp, from the first line.</summary>
    public string? AsOf { get; private set; }

    public static B3DerivativeFields Empty { get; } = new();

    public B3DerivativeField? Field(string code) => _fields.GetValueOrDefault(code);

    /// <summary>The attributes B3 registers for a figure, in the order the export lists them.</summary>
    public IReadOnlyList<B3FigureAttribute> ForFigure(string figureCode) =>
        _byFigure.TryGetValue(figureCode, out var fields) ? fields : [];

    public IReadOnlyCollection<string> FigureCodes => _byFigure.Keys;

    /// <summary>
    /// A figure's attributes keyed on <see cref="NormalizeName"/> of B3's field name, so a
    /// domain file that names the attribute the way B3 prints it can be matched to the code B3
    /// registers it under.
    /// </summary>
    public IReadOnlyDictionary<string, B3FigureAttribute> AttributesByName(string figureCode) =>
        _byFigureName.TryGetValue(figureCode, out var index)
            ? index
            : new Dictionary<string, B3FigureAttribute>(StringComparer.Ordinal);

    /// <summary>
    /// Reduces a field name to what two spellings of the same attribute have in common. Accents,
    /// case and punctuation — the "(%)" suffix above all — vary between the export, the manual's
    /// annex and the registration screen, while the words themselves do not:
    /// "Participação cenário de alta (%)" and "Participacao Cenario de Alta" both reduce to
    /// "participacao cenario de alta".
    /// </summary>
    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var decomposed = name.Normalize(NormalizationForm.FormD);
        var buffer = new StringBuilder(decomposed.Length);
        var lastWasSeparator = true;

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(c))
            {
                buffer.Append(char.ToLowerInvariant(c));
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                buffer.Append(' ');
                lastWasSeparator = true;
            }
        }

        return buffer.ToString().TrimEnd();
    }

    public static B3DerivativeFields Load(string directory)
    {
        var exports = new B3DerivativeFields();
        var errors = new List<string>();

        exports.LoadFields(Path.Combine(directory, FieldsFile), errors);
        exports.LoadFigureFields(Path.Combine(directory, FigureFieldsFile), errors);
        exports.Errors = errors;
        return exports;
    }

    /// <summary>
    /// Attaches the figure codes the catalogue knows, keyed on the sequence number both exports
    /// carry. Two catalogue rows publish a name and no code, and the same two rows here; the
    /// sequence is what ties them together, so those figures keep their attributes instead of
    /// silently losing them.
    /// </summary>
    public void ResolveFigureCodes(IReadOnlyDictionary<string, string> codeByOrdinal)
    {
        _byFigure.Clear();
        _byFigureName.Clear();

        foreach (var (ordinal, attributes) in _byOrdinal)
        {
            var code = codeByOrdinal.GetValueOrDefault(ordinal) ?? _codeByOrdinal.GetValueOrDefault(ordinal);
            if (code is null) continue;

            foreach (var attribute in attributes) attribute.FigureCode = code;
            _byFigure[code] = attributes;

            var byName = new Dictionary<string, B3FigureAttribute>(StringComparer.Ordinal);
            foreach (var attribute in attributes)
            {
                // No figure currently names two of its attributes alike once normalised, but
                // nothing in the export guarantees it; first wins rather than throwing.
                byName.TryAdd(NormalizeName(attribute.Name), attribute);
            }
            _byFigureName[code] = byName;
        }
    }

    // ----- parsing ----------------------------------------------------------------------

    private void LoadFields(string path, List<string> errors)
    {
        if (!File.Exists(path)) { errors.Add($"{FieldsFile} not found."); return; }

        var lines = File.ReadAllLines(path);
        if (lines.Length > 0 && lines[0].Length == 8 && lines[0].All(char.IsDigit)) AsOf = lines[0];

        // Line 1 is the export date and line 2 the header. One row per field, plus one extra
        // row per accepted value of a DOMINIO field.
        foreach (var line in lines.Skip(2))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(';');
            if (parts.Length < 6) continue;

            var code = parts[0].Trim();
            if (code.Length == 0) continue;

            if (!_fields.TryGetValue(code, out var field))
            {
                _fields[code] = field = new B3DerivativeField(
                    code, parts[1].Trim(), parts[2].Trim(),
                    ParseInt(parts[3]), ParseInt(parts[4]), IsYes(parts[5]));
            }

            if (parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6]))
            {
                var valueCode = parts.Length > 7 && !string.IsNullOrWhiteSpace(parts[7]) ? parts[7].Trim() : null;
                field.DomainValues.Add(new B3DerivativeDomainValue(parts[6].Trim(), valueCode));
            }
        }
    }

    private void LoadFigureFields(string path, List<string> errors)
    {
        if (!File.Exists(path)) { errors.Add($"{FigureFieldsFile} not found."); return; }

        foreach (var line in File.ReadAllLines(path).Skip(2))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(';');
            if (parts.Length < 9) continue;

            var ordinal = parts[0].Trim();
            var fieldCode = parts[3].Trim();
            if (ordinal.Length == 0 || fieldCode.Length == 0) continue;

            var (code, _) = B3Reference.SplitFigureLabel(parts[1]);
            if (code is not null) _codeByOrdinal[ordinal] = code;

            if (!_byOrdinal.TryGetValue(ordinal, out var attributes))
                _byOrdinal[ordinal] = attributes = [];

            var existing = attributes.FirstOrDefault(a => a.FieldCode == fieldCode);
            if (existing is null)
            {
                existing = new B3FigureAttribute(
                    Ordinal: ordinal,
                    FieldCode: fieldCode,
                    Name: parts[4].Trim(),
                    DataType: parts[5].Trim(),
                    Length: ParseInt(parts[6]),
                    Decimals: ParseInt(parts[7]),
                    Mandatory: IsYes(parts[8]),
                    Position: attributes.Count + 1);
                attributes.Add(existing);
            }

            // The last column is one accepted value; a DOMINIO field spans as many rows as it
            // has values. The code for each comes from the dictionary, which is the only export
            // that carries it.
            if (parts.Length > 9 && !string.IsNullOrWhiteSpace(parts[9]))
            {
                var name = parts[9].Trim();
                if (!existing.DomainValues.Any(v => v.Name == name))
                    existing.DomainValues.Add(new B3DerivativeDomainValue(name, ValueCode(fieldCode, name)));
            }
        }
    }

    private string? ValueCode(string fieldCode, string valueName) =>
        _fields.TryGetValue(fieldCode, out var field)
            ? field.DomainValues.FirstOrDefault(v => string.Equals(v.Name, valueName, StringComparison.OrdinalIgnoreCase))?.Code
            : null;

    private static bool IsYes(string value) =>
        value.Trim().Equals("S", StringComparison.OrdinalIgnoreCase) ||
        value.Trim().Equals("SIM", StringComparison.OrdinalIgnoreCase);

    private static int ParseInt(string value) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
}
