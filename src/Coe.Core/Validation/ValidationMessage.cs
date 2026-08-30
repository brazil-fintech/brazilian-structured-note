using Coe.Core.Templates;

namespace Coe.Core.Validation;

/// <summary>Where the check came from — shown in the UI so a user can tell a hint from a rule.</summary>
public enum ValidationOrigin
{
    /// <summary>A required/range/length/enum constraint declared on the field itself.</summary>
    Field,

    /// <summary>A cross-field rule from the template.</summary>
    Rule,

    /// <summary>A server-side check that cannot run in the browser.</summary>
    ServerCheck
}

/// <summary>
/// One finding. <see cref="Path"/> is the concrete instance path — including the row index
/// for repeating sections — so the client can pin the message to the exact input.
/// </summary>
public sealed record ValidationMessage
{
    public required string Path { get; init; }
    public required string Message { get; init; }
    public RuleSeverity Severity { get; init; } = RuleSeverity.Error;
    public ValidationOrigin Origin { get; init; } = ValidationOrigin.Rule;
    public string? RuleId { get; init; }
    public string? Section { get; init; }

    public static ValidationMessage Error(string path, string message, string? ruleId = null) =>
        new() { Path = path, Message = message, Severity = RuleSeverity.Error, RuleId = ruleId };

    public static ValidationMessage Warning(string path, string message, string? ruleId = null) =>
        new() { Path = path, Message = message, Severity = RuleSeverity.Warning, RuleId = ruleId };
}

/// <summary>The outcome of a validation pass.</summary>
public sealed record ValidationResult
{
    public IReadOnlyList<ValidationMessage> Messages { get; init; } = [];

    /// <summary>Paths that were evaluated, so the client can clear stale messages for them.</summary>
    public IReadOnlyList<string> EvaluatedPaths { get; init; } = [];

    public bool IsValid => Messages.All(m => m.Severity != RuleSeverity.Error);

    public static readonly ValidationResult Empty = new();
}

/// <summary>How much of the instance to check.</summary>
public enum ValidationScope
{
    /// <summary>As-you-type: only what depends on the paths the user just changed.</summary>
    Field,

    /// <summary>Everything currently fillable, but without the "you must fill this in" noise.</summary>
    Form,

    /// <summary>The full gate before persisting. Always run by the API on save.</summary>
    Submit
}
