using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Coe.Ingestion;

/// <summary>
/// The authored shape of a domain file — one per B3 figure under <c>domain/figures/</c>, plus
/// reusable fragments under <c>domain/common/</c>. Conditions and rules are written as infix
/// strings here; <see cref="TemplateCompiler"/> parses them into the AST that ships in the
/// template. Nothing in this namespace ever reaches the browser.
/// </summary>
public sealed class DomainFile
{
    public string? FigureCode { get; set; }
    public string? FigureName { get; set; }
    public string? CommercialName { get; set; }
    public LocalizedTextDto? Description { get; set; }
    public List<string> Modalities { get; set; } = [];
    public List<string> UnderlyingClasses { get; set; } = [];

    /// <summary>Fragment ids merged in before this file's own sections, e.g. <c>common/identification</c>.</summary>
    public List<string> Extends { get; set; } = [];

    /// <summary>Fragment section keys this figure does not use.</summary>
    public List<string> RemoveSections { get; set; } = [];

    public List<SectionDto> Sections { get; set; } = [];
    public List<RuleDto> Rules { get; set; } = [];

    [JsonIgnore] public string SourcePath { get; set; } = string.Empty;
    [JsonIgnore] public string SourceHash { get; set; } = string.Empty;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

public sealed class LocalizedTextDto
{
    public string Pt { get; set; } = string.Empty;
    public string? En { get; set; }
}

public sealed class SectionDto
{
    public string Key { get; set; } = string.Empty;
    public LocalizedTextDto? Label { get; set; }
    public LocalizedTextDto? Help { get; set; }

    /// <summary><c>common</c> (always visible, above the tabs) or <c>tab</c>.</summary>
    public string Kind { get; set; } = "tab";

    public int Order { get; set; }
    public string? VisibleWhen { get; set; }
    public bool Repeating { get; set; }
    public int? MinItems { get; set; }
    public int? MaxItems { get; set; }
    public List<FieldDto> Fields { get; set; } = [];
    public List<FieldDto> ItemFields { get; set; } = [];
}

public sealed class FieldDto
{
    public string Key { get; set; } = string.Empty;
    public LocalizedTextDto? Label { get; set; }
    public LocalizedTextDto? Help { get; set; }
    public string DataType { get; set; } = "string";
    public string? B3Field { get; set; }

    /// <summary>Code in B3's strategy-field dictionary, checked against reference/b3/dados-estrategia.csv.</summary>
    public string? B3FieldCode { get; set; }

    /// <summary>B3 domain the options come from, checked against reference/b3/dominios-derivativos.csv.</summary>
    public string? B3Domain { get; set; }

    public string? Symbol { get; set; }
    public string? Unit { get; set; }
    public int? Decimals { get; set; }
    public int? MaxLength { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public JsonNode? Default { get; set; }
    public bool Required { get; set; }
    public string? RequiredWhen { get; set; }
    public string? VisibleWhen { get; set; }
    public string? EnabledWhen { get; set; }
    public string? Computed { get; set; }
    public string? OptionSource { get; set; }
    public List<OptionDto> Options { get; set; } = [];
    public int Order { get; set; }
    public bool InGrid { get; set; }
}

public sealed class OptionDto
{
    public string Code { get; set; } = string.Empty;

    /// <summary>The code B3 registers for this option.</summary>
    public string? B3Code { get; set; }

    public LocalizedTextDto? Label { get; set; }
    public LocalizedTextDto? Help { get; set; }
    public string? VisibleWhen { get; set; }
}

public sealed class RuleDto
{
    public string Id { get; set; } = string.Empty;
    public List<string> Targets { get; set; } = [];
    public string? When { get; set; }
    public string? Assert { get; set; }
    public string? ServerCheck { get; set; }
    public Dictionary<string, JsonNode?> Args { get; set; } = [];
    public LocalizedTextDto? Message { get; set; }

    /// <summary><c>error</c> | <c>warning</c> | <c>info</c>.</summary>
    public string Severity { get; set; } = "error";

    /// <summary><c>client</c> | <c>server</c> | <c>both</c>.</summary>
    public string Execution { get; set; } = "both";

    /// <summary><c>change</c> | <c>submit</c> | <c>both</c>.</summary>
    public string Trigger { get; set; } = "change";

    /// <summary>Section key when the rule is evaluated once per row.</summary>
    public string? ForEachSection { get; set; }
}
