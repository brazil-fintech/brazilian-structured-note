using System.Text.RegularExpressions;
using Coe.Core.Templates;

namespace Coe.Core.Validation;

/// <summary>
/// Path arithmetic for instance documents.
///
/// Templates address repeating-section columns generically (<c>cashflows[].amount</c>);
/// messages and change notifications address a concrete row (<c>cashflows[2].amount</c>).
/// </summary>
public static partial class Instance
{
    [GeneratedRegex(@"\[\d+\]", RegexOptions.CultureInvariant)]
    private static partial Regex IndexPattern();

    /// <summary>Concrete path of a field, given the row prefix when inside a repeating section.</summary>
    public static string PathFor(TemplateField field, string? prefix) =>
        prefix is null ? field.Path : $"{prefix}.{field.Key}";

    /// <summary>Replaces every row index with <c>[]</c> so a concrete path can be matched against a template path.</summary>
    public static string Normalize(string path) => IndexPattern().Replace(path, "[]");

    /// <summary>
    /// Rewrites a rule target into a concrete instance path. <paramref name="prefix"/> is the
    /// current row (<c>cashflows[2]</c>) when the rule runs per row, otherwise null.
    /// </summary>
    public static string Resolve(string target, string? prefix)
    {
        if (string.IsNullOrEmpty(target)) return prefix ?? string.Empty;
        if (prefix is null) return target;

        var bracket = prefix.IndexOf('[');
        var sectionKey = bracket < 0 ? prefix : prefix[..bracket];

        var generic = sectionKey + "[]";
        if (target.StartsWith(generic, StringComparison.Ordinal))
            return prefix + target[generic.Length..];

        // A bare column name inside a row rule refers to that row's column.
        return target.Contains('.', StringComparison.Ordinal) ? target : $"{prefix}.{target}";
    }

    /// <summary>Section key of a path: <c>payoff.cap</c> and <c>cashflows[2].amount</c> both yield their section.</summary>
    public static string SectionOf(string path)
    {
        var cut = path.IndexOfAny(new[] { '.', '[' });
        return cut < 0 ? path : path[..cut];
    }
}
