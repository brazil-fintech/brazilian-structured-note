using System.Globalization;
using System.Text.Json.Nodes;
using Coe.Core.Expressions;
using Coe.Core.Templates;

namespace Coe.Clearing;

/// <summary>
/// Reads a booked instance the way the upload files need it: by template path, in the shape the
/// layout's field expects, and — for an enum — as the code B3 registers rather than the code the
/// platform stores.
///
/// That last translation is the whole point of holding the template alongside the values. The
/// instance says <c>"BEST_OF"</c> because that is a name a person can read in a form and a rule
/// can be written against; B3's file wants <c>1</c>, which lives on the option as its
/// <see cref="FieldOption.B3Code"/>. Nothing else in the platform needs to know that, and
/// nothing else does.
/// </summary>
public sealed class InstanceReader
{
    private readonly FigureTemplate _template;
    private readonly JsonObject _values;

    public InstanceReader(FigureTemplate template, JsonObject values)
    {
        _template = template;
        _values = values;
    }

    public FigureTemplate Template => _template;

    /// <summary>The node at <c>section.key</c>, or null when the section or the attribute is absent.</summary>
    public JsonNode? Node(string path)
    {
        var cut = path.IndexOf('.', StringComparison.Ordinal);
        if (cut < 0) return _values[path];

        return _values[path[..cut]] is JsonObject section ? section[path[(cut + 1)..]] : null;
    }

    public string? Text(string path) => AsText(Node(path));

    public decimal? Number(string path) => Values.AsNumber(Values.FromJson(Node(path)));

    public long? Integer(string path) => Number(path) is { } n ? (long)decimal.Truncate(n) : null;

    public DateOnly? Date(string path) => Values.AsDate(Values.FromJson(Node(path)));

    /// <summary>True only for a stored <c>true</c>; a missing boolean is not a yes.</summary>
    public bool Flag(string path) => Values.AsBool(Values.FromJson(Node(path))) == true;

    /// <summary>
    /// The code B3 registers for the option selected at <paramref name="path"/>. Null when
    /// nothing is selected, and null — rather than the platform's own code — when the option
    /// carries no <c>b3Code</c>, because writing a code B3 does not know is worse than leaving
    /// the field blank for someone to notice.
    /// </summary>
    public string? DomainCode(string path) => DomainCode(_template.FindField(path), Text(path));

    /// <summary>The same, for a column of a repeating section.</summary>
    internal static string? DomainCode(TemplateField? field, string? selected)
    {
        if (field is null || string.IsNullOrEmpty(selected)) return null;

        var option = field.Options.FirstOrDefault(o => string.Equals(o.Code, selected, StringComparison.Ordinal));
        return option?.B3Code;
    }

    /// <summary>
    /// A domain code padded to the width the layout writes it in. B3 publishes its domains with
    /// bare codes — <c>1</c> for a watermark functionality — and writes them zero-padded to the
    /// field, <c>01</c>. The published code is what the domain files carry and what the compiler
    /// checks; the padding belongs to the layout, so it is applied here.
    /// </summary>
    public string? PaddedDomainCode(string path, int width) => Pad(DomainCode(path), width);

    internal static string? Pad(string? code, int width) =>
        code is null ? null : code.Length >= width ? code : code.PadLeft(width, '0');

    /// <summary>The rows of a repeating section, empty when it holds nothing.</summary>
    public IReadOnlyList<RowReader> Rows(string sectionKey)
    {
        if (_values[sectionKey] is not JsonArray array) return [];

        var rows = new List<RowReader>(array.Count);
        foreach (var node in array)
            if (node is JsonObject row)
                rows.Add(new RowReader(_template, sectionKey, row));

        return rows;
    }

    /// <summary>
    /// The attributes that go into the variable-data record of the Registro COE: every field
    /// that carries one of B3's data codes, holds a value, and sits in a section that does not
    /// repeat — the record has no row index, so a repeating column cannot be addressed by it and
    /// belongs to the cash-flow or basket file instead.
    /// </summary>
    public IReadOnlyList<(TemplateField Field, string Value)> VariableFields()
    {
        var written = new List<(TemplateField, string)>();

        foreach (var section in _template.Sections)
        {
            if (section.Repeating) continue;

            foreach (var field in section.Fields)
            {
                if (field.B3DataCode is not { } code || code.Length == 0) continue;

                var value = FormatVariable(field);
                if (value is not null) written.Add((field, value));
            }
        }

        return written;
    }

    /// <summary>
    /// One variable-data value, in the form its dictionary type takes: a domain field writes the
    /// identifier of the value, a date writes AAAAMMDD, and a number writes its digits without a
    /// separator, as everywhere else in these files.
    /// </summary>
    private string? FormatVariable(TemplateField field)
    {
        var node = Node(field.Path);
        if (node is null) return null;

        switch (field.DataType)
        {
            case FieldDataType.Enum:
            case FieldDataType.EnumSet:
                return DomainCode(field.Path);

            case FieldDataType.Boolean:
                // A boolean in the platform is a two-value domain at B3; where the template
                // spells the two options out, they win, and otherwise S/N is B3's own shorthand.
                return DomainCode(field.Path) ?? (Flag(field.Path) ? "S" : "N");

            case FieldDataType.Date:
                return Date(field.Path)?.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            case FieldDataType.Integer:
                return Integer(field.Path)?.ToString(CultureInfo.InvariantCulture);

            case FieldDataType.Decimal:
            case FieldDataType.Percent:
            case FieldDataType.Money:
                if (Number(field.Path) is not { } number) return null;
                var decimals = field.Decimals ?? 0;
                var scaled = decimal.Round(number, decimals, MidpointRounding.AwayFromZero);
                return decimals == 0
                    ? decimal.Truncate(scaled).ToString(CultureInfo.InvariantCulture)
                    : scaled.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

            default:
                var text = Text(field.Path);
                return string.IsNullOrEmpty(text) ? null : text;
        }
    }

    /// <summary>
    /// The stored value as text. A structure (a repeating section, an object) has no scalar
    /// form and reads as absent, which is what a fixed-width field needs.
    /// </summary>
    internal static string? AsText(JsonNode? node) =>
        node is JsonValue value ? Values.AsString(Values.FromJson(value)) : null;
}

/// <summary>One row of a repeating section, read the same way as the instance around it.</summary>
public sealed class RowReader(FigureTemplate template, string sectionKey, JsonObject row)
{
    public JsonNode? Node(string key) => row[key];

    public string? Text(string key) => InstanceReader.AsText(row[key]);

    public decimal? Number(string key) => Values.AsNumber(Values.FromJson(row[key]));

    public long? Integer(string key) => Number(key) is { } n ? (long)decimal.Truncate(n) : null;

    public DateOnly? Date(string key) => Values.AsDate(Values.FromJson(row[key]));

    public bool Flag(string key) => Values.AsBool(Values.FromJson(row[key])) == true;

    public string? DomainCode(string key) =>
        InstanceReader.DomainCode(template.FindField($"{sectionKey}[].{key}"), Text(key));

    public string? PaddedDomainCode(string key, int width) => InstanceReader.Pad(DomainCode(key), width);
}
