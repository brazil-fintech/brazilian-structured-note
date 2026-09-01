using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coe.Core.Templates;

/// <summary>
/// The single serializer configuration for everything that crosses the wire or lands in the
/// database. The React client parses templates with exactly these conventions, so any change
/// here is a breaking change to <c>web/src/engine</c> as well.
/// </summary>
public static class TemplateJson
{
    public static readonly JsonSerializerOptions Options = Create(indented: false);
    public static readonly JsonSerializerOptions Indented = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        WriteIndented = indented,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize(FigureTemplate template, bool indented = false) =>
        JsonSerializer.Serialize(template, indented ? Indented : Options);

    public static FigureTemplate Deserialize(string json) =>
        JsonSerializer.Deserialize<FigureTemplate>(json, Options)
        ?? throw new InvalidOperationException("Template JSON deserialized to null.");
}
