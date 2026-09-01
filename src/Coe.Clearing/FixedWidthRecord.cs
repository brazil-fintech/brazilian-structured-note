using System.Globalization;
using System.Text;

namespace Coe.Clearing;

/// <summary>A value that cannot be written into the field the layout gives it.</summary>
public sealed class ClearingFormatException(string message) : Exception(message);

/// <summary>
/// Builds one line of a CETIP upload file against the positions the <em>ENVIAR ARQUIVOS</em>
/// layouts declare.
///
/// Every field is written with the position the manual prints for it, and the builder refuses a
/// write that does not start exactly where the last one ended. A layout is fifty numbered rows
/// of "from" and "to", and the failure mode of transcribing one by hand is an off-by-one that
/// shifts every field after it — a file B3 rejects with a message about the wrong field, or
/// worse, accepts with a number read out of the middle of two others. Stating the positions and
/// checking them is what turns that class of mistake into an exception on the first field.
///
/// Values follow the manual's conventions: text is left-aligned and space-padded, numbers are
/// right-aligned and zero-padded, amounts carry an implied decimal point with no separator, and
/// dates are AAAAMMDD. A field with nothing to say is spaces, whatever its type — "os campos
/// cujo contexto não se aplica, mesmo que numéricos, deverão ser preenchidos com brancos".
/// </summary>
public sealed class FixedWidthRecord(string layout, string record)
{
    private readonly StringBuilder _buffer = new();

    /// <summary>X(n): left-aligned, padded with spaces.</summary>
    public FixedWidthRecord Text(int from, int to, string? value)
    {
        var width = Width(from, to);
        var text = value ?? string.Empty;

        if (text.Length > width)
            throw new ClearingFormatException(
                $"{Where(from)}: '{Preview(text)}' is {text.Length} characters and the field holds {width}.");

        _buffer.Append(text.PadRight(width));
        return this;
    }

    /// <summary>
    /// A constant the layout prints in its "Conteúdo" column, e.g. <c>COE</c> or <c>0001</c>.
    /// Padded like any other text field — the instrument type is <c>COE</c> in an X(5) — but a
    /// constant that does not fit is a transcription error rather than a value to reject.
    /// </summary>
    public FixedWidthRecord Literal(int from, int to, string value)
    {
        var width = Width(from, to);
        if (value.Length > width)
            throw new ClearingFormatException(
                $"{Where(from)}: the constant '{value}' is {value.Length} characters and the field is {width}.");

        _buffer.Append(value.PadRight(width));
        return this;
    }

    /// <summary>9(n): right-aligned, padded with zeroes. Null is left blank.</summary>
    public FixedWidthRecord Number(int from, int to, long? value)
    {
        var width = Width(from, to);
        if (value is not { } number) return Blank(width);

        if (number < 0)
            throw new ClearingFormatException($"{Where(from)}: {number} is negative and the layout carries no sign.");

        var text = number.ToString(CultureInfo.InvariantCulture);
        if (text.Length > width)
            throw new ClearingFormatException($"{Where(from)}: {number} needs {text.Length} digits and the field holds {width}.");

        _buffer.Append(text.PadLeft(width, '0'));
        return this;
    }

    /// <summary>
    /// 9(i),9(d): the decimal point is implied by the layout and never written — 12 integer
    /// digits and 8 decimals writes 1.5 as <c>00000000000150000000</c>.
    /// </summary>
    public FixedWidthRecord Amount(int from, int to, decimal? value, int decimals)
    {
        var width = Width(from, to);
        if (value is not { } amount) return Blank(width);

        if (amount < 0)
            throw new ClearingFormatException($"{Where(from)}: {amount} is negative and the layout carries no sign.");
        if (decimals < 0 || decimals > width)
            throw new ClearingFormatException($"{Where(from)}: {decimals} decimals do not fit a field of {width}.");

        // Round rather than truncate: a value the desk booked to more precision than B3
        // registers should land on the nearest registrable number, not systematically below it.
        var scaled = decimal.Round(amount, decimals, MidpointRounding.AwayFromZero) * Power(decimals);
        var text = decimal.Truncate(scaled).ToString(CultureInfo.InvariantCulture);

        if (text.Length > width)
            throw new ClearingFormatException(
                $"{Where(from)}: {amount} needs {text.Length} digits at {decimals} decimal place(s) "
                + $"and the field holds {width}.");

        _buffer.Append(text.PadLeft(width, '0'));
        return this;
    }

    /// <summary>AAAAMMDD. Null is left blank.</summary>
    public FixedWidthRecord Date(int from, int to, DateOnly? value)
    {
        var width = Width(from, to);
        if (width != 8)
            throw new ClearingFormatException($"{Where(from)}: a date field is 8 characters, not {width}.");

        return value is { } date
            ? Literal(from, to, date.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
            : Blank(width);
    }

    /// <summary>A field the layout reserves, or one whose context does not apply.</summary>
    public FixedWidthRecord Filler(int from, int to) => Blank(Width(from, to));

    /// <summary>
    /// The finished line. <paramref name="length"/> is the record size the layout states, and is
    /// checked: a record that comes out short means a field was forgotten, not that the file is
    /// merely a little different.
    /// </summary>
    public string Build(int length)
    {
        if (_buffer.Length != length)
            throw new ClearingFormatException(
                $"{layout}/{record}: the record came out {_buffer.Length} characters and the layout declares {length}.");

        return _buffer.ToString();
    }

    private FixedWidthRecord Blank(int width)
    {
        _buffer.Append(' ', width);
        return this;
    }

    private int Width(int from, int to)
    {
        if (_buffer.Length != from - 1)
            throw new ClearingFormatException(
                $"{layout}/{record}: the field at {from} would start at {_buffer.Length + 1}. "
                + "A position in the layout was transcribed wrongly, or a field before it was skipped.");

        if (to < from)
            throw new ClearingFormatException($"{Where(from)}: the field ends at {to}, before it starts.");

        return to - from + 1;
    }

    private string Where(int from) => $"{layout}/{record} at position {from}";

    private static string Preview(string text) => text.Length <= 40 ? text : text[..37] + "...";

    private static decimal Power(int decimals)
    {
        var value = 1m;
        for (var i = 0; i < decimals; i++) value *= 10m;
        return value;
    }
}
