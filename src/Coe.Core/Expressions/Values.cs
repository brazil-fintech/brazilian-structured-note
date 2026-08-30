using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Coe.Core.Expressions;

/// <summary>
/// Coercion rules for expression values. The canonical runtime types are
/// <c>null</c>, <see cref="bool"/>, <see cref="decimal"/>, <see cref="string"/>,
/// <see cref="DateOnly"/>, <see cref="JsonArray"/> and <see cref="JsonObject"/>.
/// The TypeScript evaluator implements the same rules.
/// </summary>
public static class Values
{
    public static object? FromJson(JsonNode? node)
    {
        if (node is null) return null;
        if (node is JsonArray or JsonObject) return node;
        if (node is not JsonValue value) return null;

        // A JsonValue is backed either by a JsonElement — anything parsed from a request
        // payload or a stored template — or by the CLR value it was constructed from, for a
        // node assembled in code: a computed attribute, a field default, a caller building an
        // instance by hand. TryGetValue answers only for the exact backing type and does no
        // numeric widening, so both shapes have to be unpacked explicitly. Getting this wrong
        // throws on the very first `values["common"]["quantity"] = 1000`.
        if (value.TryGetValue<JsonElement>(out var element)) return FromElement(element);

        if (value.TryGetValue<bool>(out var b)) return b;
        if (value.TryGetValue<string>(out var s)) return AsDateOrString(s);

        if (value.TryGetValue<decimal>(out var dec)) return dec;
        if (value.TryGetValue<int>(out var i)) return (decimal)i;
        if (value.TryGetValue<long>(out var l)) return (decimal)l;
        if (value.TryGetValue<double>(out var dbl)) return FromDouble(dbl);
        if (value.TryGetValue<float>(out var f)) return FromDouble(f);
        if (value.TryGetValue<short>(out var sh)) return (decimal)sh;
        if (value.TryGetValue<byte>(out var by)) return (decimal)by;
        if (value.TryGetValue<uint>(out var ui)) return (decimal)ui;
        if (value.TryGetValue<ulong>(out var ul)) return (decimal)ul;

        if (value.TryGetValue<DateOnly>(out var dateOnly)) return dateOnly;
        if (value.TryGetValue<DateTime>(out var dt)) return DateOnly.FromDateTime(dt);
        if (value.TryGetValue<DateTimeOffset>(out var dto)) return DateOnly.FromDateTime(dto.UtcDateTime);

        return null;
    }

    private static object? FromElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => element.TryGetDecimal(out var dec) ? dec : FromDouble(element.GetDouble()),
        JsonValueKind.String => AsDateOrString(element.GetString() ?? string.Empty),
        _ => null
    };

    /// <summary>
    /// A payload can carry a magnitude no decimal can hold. That is a value we cannot reason
    /// about, which the engine treats as "cannot tell yet" — never as a crash mid-keystroke.
    /// </summary>
    private static decimal? FromDouble(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return null;
        if (value > (double)decimal.MaxValue || value < (double)decimal.MinValue) return null;
        return (decimal)value;
    }

    private static object AsDateOrString(string s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : s;

    /// <summary>Empty strings count as absent, so "required" and null-checks agree with the UI.</summary>
    public static bool IsAbsent(object? v) => v is null || (v is string s && s.Length == 0);

    public static bool? AsBool(object? v) => v switch
    {
        null => null,
        bool b => b,
        decimal d => d != 0m,
        string s when bool.TryParse(s, out var b) => b,
        _ => null
    };

    /// <summary>Truthiness used by <c>and</c>/<c>or</c>/<c>not</c> and by rule guards: null is false.</summary>
    public static bool Truthy(object? v) => AsBool(v) ?? false;

    public static decimal? AsNumber(object? v) => v switch
    {
        null => null,
        decimal d => d,
        bool b => b ? 1m : 0m,
        DateOnly date => date.DayNumber,
        string s when decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null
    };

    public static DateOnly? AsDate(object? v) => v switch
    {
        DateOnly d => d,
        string s when DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) => d,
        DateTime dt => DateOnly.FromDateTime(dt),
        _ => null
    };

    public static string? AsString(object? v) => v switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => v.ToString()
    };

    public static IReadOnlyList<JsonNode?> AsList(object? v) => v switch
    {
        JsonArray arr => arr.ToList(),
        null => Array.Empty<JsonNode?>(),
        _ => Array.Empty<JsonNode?>()
    };

    /// <summary>
    /// Ordering comparison. Returns null when the operands are not comparable, which makes the
    /// surrounding comparison evaluate to null (and therefore a rule guard to false) rather than throw.
    /// </summary>
    public static int? Compare(object? a, object? b)
    {
        if (a is null || b is null) return null;
        if (a is DateOnly || b is DateOnly)
        {
            var da = AsDate(a);
            var db = AsDate(b);
            return da is null || db is null ? null : da.Value.CompareTo(db.Value);
        }
        if (a is string sa && b is string sb) return string.CompareOrdinal(sa, sb);
        var na = AsNumber(a);
        var nb = AsNumber(b);
        return na is null || nb is null ? null : na.Value.CompareTo(nb.Value);
    }

    public static bool Equal(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a is bool ba && b is bool bb) return ba == bb;
        if (a is bool || b is bool)
        {
            var xa = AsBool(a);
            var xb = AsBool(b);
            return xa is not null && xb is not null && xa == xb;
        }
        var cmp = Compare(a, b);
        if (cmp is not null) return cmp == 0;
        return string.Equals(AsString(a), AsString(b), StringComparison.Ordinal);
    }
}
