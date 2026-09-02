using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Coe.Core.Expressions;

namespace Coe.Core.Templates;

/// <summary>
/// The compiled, versioned description of one B3 payoff figure: which attributes exist,
/// how they are laid out (a common block plus tabs), when each one is visible or required,
/// and every validation rule. Produced by the ingestion worker from a domain file, stored
/// in MSSQL, and served verbatim to the React client.
/// </summary>
public sealed record FigureTemplate
{
    /// <summary>Version of the template format itself, not of this figure's content.</summary>
    public string SchemaVersion { get; init; } = TemplateSchema.CurrentVersion;

    /// <summary>B3 figure code, e.g. <c>COE001005</c>.</summary>
    public required string FigureCode { get; init; }

    /// <summary>The registered figure name, e.g. "Call Spread".</summary>
    public required string FigureName { get; init; }

    /// <summary>The commercial/distribution name, e.g. "Call spread (capped call)".</summary>
    public string? CommercialName { get; init; }

    public LocalizedText? Description { get; init; }

    /// <summary>Monotonic version of this figure's template; a new one is issued on every content change.</summary>
    public int Version { get; init; }

    /// <summary>Modalities the figure may be registered under: <c>VNP</c>, <c>VNR</c>.</summary>
    public IReadOnlyList<string> Modalities { get; init; } = [];

    /// <summary>Underlying asset classes the figure accepts, from the B3 registration screen.</summary>
    public IReadOnlyList<string> UnderlyingClasses { get; init; } = [];

    /// <summary>Relative path of the domain file this was compiled from.</summary>
    public string? SourceFile { get; init; }

    /// <summary>SHA-256 of the domain file, used to detect content changes between ingestion runs.</summary>
    public string? SourceHash { get; init; }

    public DateTimeOffset CompiledAtUtc { get; init; }

    /// <summary>The common block first, then the tabs, in display order.</summary>
    public IReadOnlyList<TemplateSection> Sections { get; init; } = [];

    /// <summary>Cross-field validation rules. Field-local constraints live on the field itself.</summary>
    public IReadOnlyList<TemplateRule> Rules { get; init; } = [];

    public IEnumerable<TemplateField> AllFields() =>
        Sections.SelectMany(s => s.Fields.Concat(s.ItemFields));

    public TemplateField? FindField(string path) =>
        AllFields().FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.Ordinal));

    public TemplateSection? FindSection(string key) =>
        Sections.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal));
}

public static class TemplateSchema
{
    public const string CurrentVersion = "1.0";
}

/// <summary>PT-BR is the registration language; EN is carried for the international desk.</summary>
public sealed record LocalizedText(string Pt, string? En = null)
{
    public string For(string culture) =>
        culture.StartsWith("en", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(En) ? En! : Pt;

    public override string ToString() => Pt;
}

public enum SectionKind
{
    /// <summary>Rendered above the tab strip and always visible, whatever the figure.</summary>
    Common,

    /// <summary>Rendered as a tab: payoff, basket, cash flows, barriers, settlement…</summary>
    Tab
}

/// <summary>A block of attributes: the common header or one tab.</summary>
public sealed record TemplateSection
{
    public required string Key { get; init; }
    public required LocalizedText Label { get; init; }
    public SectionKind Kind { get; init; } = SectionKind.Tab;
    public int Order { get; init; }
    public LocalizedText? Help { get; init; }

    /// <summary>When set, the tab is only shown while this evaluates truthy.</summary>
    public Expr? VisibleWhen { get; init; }

    /// <summary>
    /// True for grid sections (cash flows, basket components). The section value is an array;
    /// each row is described by <see cref="ItemFields"/> and <see cref="Fields"/> is empty.
    /// </summary>
    public bool Repeating { get; init; }

    public int? MinItems { get; init; }
    public int? MaxItems { get; init; }

    /// <summary>Fields of a non-repeating section.</summary>
    public IReadOnlyList<TemplateField> Fields { get; init; } = [];

    /// <summary>Columns of a repeating section.</summary>
    public IReadOnlyList<TemplateField> ItemFields { get; init; } = [];
}

public enum FieldDataType
{
    String,
    Text,
    Integer,
    Decimal,
    /// <summary>Stored as the percentage number: 25 means 25%.</summary>
    Percent,
    Money,
    Date,
    Boolean,
    Enum,
    /// <summary>Multi-select enum; the value is an array of option codes.</summary>
    EnumSet
}

/// <summary>One bookable attribute.</summary>
public sealed record TemplateField
{
    /// <summary>Key within its section.</summary>
    public required string Key { get; init; }

    /// <summary>
    /// Absolute path in the instance document: <c>section.key</c> for a plain field,
    /// <c>section[].key</c> for a repeating-section column.
    /// </summary>
    public required string Path { get; init; }

    public required LocalizedText Label { get; init; }
    public required FieldDataType DataType { get; init; }

    /// <summary>The registered B3 field name this maps to, when there is one.</summary>
    public string? B3Field { get; init; }

    /// <summary>
    /// Code of this attribute in B3's strategy-field dictionary (<c>C0000368</c>), when it is
    /// known. Set it and the compiler checks the declared type, size and decimals against the
    /// published dictionary. Left unset where the mapping has not been established — the
    /// dictionary carries no figure association and repeats concept names across figures, so
    /// guessing it from a label would be worse than leaving it blank.
    /// </summary>
    public string? B3FieldCode { get; init; }

    /// <summary>
    /// Identifier of this attribute in B3's derivative-data dictionary
    /// (<c>DTpTipoDadosDerivativo</c>), e.g. <c>C0000032</c>. This is the code the
    /// "Identificador do Campo" of the Registro COE variable-data record carries, so an
    /// attribute with one can be written to B3 and an attribute without one cannot.
    /// </summary>
    public string? B3DataCode { get; init; }

    /// <summary>
    /// The B3 domain this field's options come from (<c>TIPO CESTA</c>). When set, every option
    /// must carry a <see cref="FieldOption.B3Code"/> that exists and is enabled in that domain.
    /// </summary>
    public string? B3Domain { get; init; }

    /// <summary>The formula symbol used in the payoff documentation (Part, Cap, K, H…).</summary>
    public string? Symbol { get; init; }

    public LocalizedText? Help { get; init; }
    public string? Unit { get; init; }
    public int? Decimals { get; init; }
    public int? MaxLength { get; init; }
    public decimal? Min { get; init; }
    public decimal? Max { get; init; }
    public JsonNode? Default { get; init; }

    /// <summary>Always required, regardless of other values.</summary>
    public bool Required { get; init; }

    /// <summary>Required only while this evaluates truthy (in addition to <see cref="Required"/>).</summary>
    public Expr? RequiredWhen { get; init; }

    /// <summary>Hidden while this evaluates falsy. Hidden fields are neither required nor validated.</summary>
    public Expr? VisibleWhen { get; init; }

    /// <summary>Read-only while this evaluates falsy.</summary>
    public Expr? EnabledWhen { get; init; }

    /// <summary>
    /// Derived value (VFE = quantity × unit price, and similar). The client shows it read-only
    /// and recomputes it as its inputs change; the API recomputes it again before saving, so a
    /// tampered payload cannot store a value that disagrees with its inputs.
    /// </summary>
    public Expr? Computed { get; init; }

    /// <summary>Options for <see cref="FieldDataType.Enum"/> / <see cref="FieldDataType.EnumSet"/>.</summary>
    public IReadOnlyList<FieldOption> Options { get; init; } = [];

    /// <summary>Reference list resolved by the API (underlyings, calendars, issuers…).</summary>
    public string? OptionSource { get; init; }

    /// <summary>Field paths whose change should re-run this field's server-side checks.</summary>
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    public int Order { get; init; }

    /// <summary>Show this field in the asset list grid.</summary>
    public bool InGrid { get; init; }
}

public sealed record FieldOption(string Code, LocalizedText Label)
{
    public LocalizedText? Help { get; init; }

    /// <summary>
    /// The code B3 registers for this option. The platform stores the mnemonic <see cref="Code"/>
    /// in the instance — it is stable, readable, and what the rules are written against — while
    /// this is what a registration file must carry. Keeping the two separate means a B3 code
    /// change is a reference-data update, not a rewrite of every rule that names the option.
    /// </summary>
    public string? B3Code { get; init; }

    /// <summary>Option only offered while this evaluates truthy.</summary>
    public Expr? VisibleWhen { get; init; }
}

public enum RuleSeverity
{
    /// <summary>Blocks submission.</summary>
    Error,

    /// <summary>Surfaced next to the attribute but does not block submission.</summary>
    Warning,

    Info
}

/// <summary>Where a rule can run. The API always runs everything on submit regardless.</summary>
[Flags]
public enum RuleExecution
{
    Client = 1,
    Server = 2,
    Both = Client | Server
}

/// <summary>When the client should evaluate a rule.</summary>
public enum RuleTrigger
{
    /// <summary>As the user types/changes a dependency.</summary>
    Change,

    /// <summary>Only when the form is submitted.</summary>
    Submit,

    Both
}

/// <summary>A cross-field validation rule.</summary>
public sealed record TemplateRule
{
    public required string Id { get; init; }

    /// <summary>Attributes the message is attached to in the UI. Empty means form-level.</summary>
    public IReadOnlyList<string> Targets { get; init; } = [];

    /// <summary>Optional guard: the rule is skipped unless this is truthy.</summary>
    public Expr? When { get; init; }

    /// <summary>The condition that must hold. A null result counts as "cannot tell yet" and is skipped.</summary>
    public Expr? Assert { get; init; }

    /// <summary>
    /// A check that cannot be expressed in the AST and is implemented server-side by id
    /// (business-day calendars, code uniqueness, reference-data lookups). Mutually exclusive
    /// with <see cref="Assert"/>.
    /// </summary>
    public string? ServerCheck { get; init; }

    /// <summary>Arguments passed to <see cref="ServerCheck"/>.</summary>
    public IReadOnlyDictionary<string, JsonNode?> Args { get; init; } = new Dictionary<string, JsonNode?>();

    public required LocalizedText Message { get; init; }
    public RuleSeverity Severity { get; init; } = RuleSeverity.Error;
    public RuleExecution Execution { get; init; } = RuleExecution.Both;
    public RuleTrigger Trigger { get; init; } = RuleTrigger.Change;

    /// <summary>
    /// Section key when the rule is evaluated once per row of a repeating section;
    /// null for a whole-instance rule.
    /// </summary>
    public string? ForEachSection { get; init; }

    /// <summary>Every field path read by <see cref="When"/> and <see cref="Assert"/>, computed at compile time.</summary>
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    [JsonIgnore]
    public bool RunsOnClient => Execution.HasFlag(RuleExecution.Client) && ServerCheck is null;

    [JsonIgnore]
    public bool RunsOnServer => Execution.HasFlag(RuleExecution.Server) || ServerCheck is not null;
}
