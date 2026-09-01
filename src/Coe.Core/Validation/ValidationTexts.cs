namespace Coe.Core.Validation;

/// <summary>
/// Messages for the constraints declared on a field. Rule messages come from the template
/// itself; these are the generic ones the engine produces. Mirrored in
/// <c>web/src/engine/texts.ts</c> so client and server word things the same way.
/// </summary>
public static class ValidationTexts
{
    private static bool En(string culture) => culture.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    public static string Required(string c) => En(c) ? "{0} is required." : "{0} é obrigatório.";
    public static string NotANumber(string c) => En(c) ? "{0} must be a number." : "{0} deve ser numérico.";
    public static string NotAnInteger(string c) => En(c) ? "{0} must be a whole number." : "{0} deve ser um número inteiro.";
    public static string NotADate(string c) => En(c) ? "{0} must be a valid date." : "{0} deve ser uma data válida.";
    public static string NotABoolean(string c) => En(c) ? "{0} must be yes or no." : "{0} deve ser Sim ou Não.";
    public static string NotAnOption(string c) => En(c) ? "{0}: '{1}' is not an accepted option." : "{0}: '{1}' não é uma opção aceita.";
    public static string Min(string c) => En(c) ? "{0} must be at least {1}." : "{0} deve ser no mínimo {1}.";
    public static string Max(string c) => En(c) ? "{0} must be at most {1}." : "{0} deve ser no máximo {1}.";
    public static string Decimals(string c) => En(c) ? "{0} is registered with {1} decimal places." : "{0} é registrado com {1} casas decimais.";
    public static string MaxLength(string c) => En(c) ? "{0} accepts at most {1} characters." : "{0} aceita no máximo {1} caracteres.";
    public static string MinItems(string c) => En(c) ? "at least {0} row(s) required." : "informe ao menos {0} linha(s).";
    public static string MaxItems(string c) => En(c) ? "at most {0} row(s) allowed." : "no máximo {0} linha(s).";
}
