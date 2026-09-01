namespace Coe.Ingestion.Cetip;

/// <summary>
/// One file CETIP publishes under <c>ftp://ftp.cetip.com.br/Public</c>, and where the platform
/// keeps it.
/// </summary>
/// <param name="Name">Logical name, used in configuration, logs and the manifest.</param>
/// <param name="RemoteNames">
/// The base names to look for, without the date prefix or the extension, best first. Several
/// are listed where CETIP and the manuals disagree on punctuation — the *Manual de Operações*
/// links <c>Dominios_COE.txt</c> for a file the directory carries as <c>DominiosCOE.txt</c>.
/// </param>
/// <param name="LocalFile">File name under <c>reference/b3/</c> the download is written to.</param>
/// <param name="Consumed">
/// Whether the platform reads this export. The public area also carries participant and fund
/// registers that a COE desk does not need; they are fetched only when named explicitly.
/// </param>
public sealed record CetipExport(string Name, IReadOnlyList<string> RemoteNames, string LocalFile, bool Consumed = true)
{
    public CetipExport(string name, string remoteName, string localFile, bool consumed = true)
        : this(name, [remoteName], localFile, consumed) { }
}

/// <summary>
/// The catalogue of public CETIP exports, and the file each one lands in.
///
/// The names are B3's, dated <c>AAAAMMDD_</c> at the front — the same files the *ENVIAR
/// ARQUIVOS* layouts point registrants at for the domains their fields accept.
/// </summary>
public static class CetipPublicFiles
{
    /// <summary>The figure catalogue: which payoff figures exist and which B3 settles itself.</summary>
    public static readonly CetipExport Figures =
        new("figuras", "DTpFiguras", "figuras.csv");

    /// <summary>
    /// The dictionary of the variable-data fields of a COE registration — the file the
    /// *Registro COE* layout names for its "Identificador do Campo". Carries each field's type,
    /// size, decimals, mandatory flag, and the identifier of every value a DOMINIO field takes.
    /// </summary>
    public static readonly CetipExport DerivativeFields =
        new("dados-derivativo", "DTpTipoDadosDerivativo", "dados-derivativo.csv");

    /// <summary>
    /// Which of those fields belong to which figure. This is the association the *Manual de
    /// Operações* annex only describes in prose, published by B3 as data.
    /// </summary>
    public static readonly CetipExport FigureFields =
        new("figuras-dados-derivativo", "DTpFigurasDadosDerivativo", "figuras-dados-derivativo.csv");

    /// <summary>The strategy-field dictionary, a separate and larger catalogue.</summary>
    public static readonly CetipExport StrategyFields =
        new("dados-estrategia", "DTpDadosEstrategia", "dados-estrategia.csv");

    /// <summary>The COE-scoped registration domains: remunerators, basket types, redemption conditions.</summary>
    public static readonly CetipExport CoeDomains =
        new("dominios-coe", ["DominiosCOE", "Dominios_COE"], "dominios-coe.csv");

    /// <summary>The same domains across every derivative instrument type.</summary>
    public static readonly CetipExport DerivativeDomains =
        new("dominios-derivativos", "Dominios_DERIVATIVOS_COE", "dominios-derivativos.csv");

    /// <summary>The underlying-asset master.</summary>
    public static readonly CetipExport Underlyings =
        new("ativos-subjacentes", ["Ativos_Subjacentes", "Ativos Subjacentes"], "ativos-subjacentes.csv");

    /// <summary>
    /// Curve, currency and feeder qualifications — where the *Registro COE* layout sends
    /// registrants for the code of the "Condição Específica Resgate" field.
    /// </summary>
    public static readonly CetipExport Qualifications =
        new("curvas-moedas-feeder", "Cadastro_Curvas_Moedas_Feeder_Dominios", "curvas-moedas-feeder.csv");

    /// <summary>
    /// Participant mnemonics: the "Nome Simplificado do Participante" every upload header
    /// carries, against the institution's account and CNPJ.
    /// </summary>
    public static readonly CetipExport Participants =
        new("mnemonicos", "mnemonicos_cetip", "mnemonicos.csv");

    /// <summary>The full institution register — ~50 MB, and nothing in booking a COE reads it.</summary>
    public static readonly CetipExport Institutions =
        new("cadastro-instituicoes", "cadastro_instituicoes", "cadastro-instituicoes.txt", Consumed: false);

    /// <summary>Funds enabled for trading; carried for completeness, not read by the platform.</summary>
    public static readonly CetipExport TradingEnabled =
        new("habilitados-negociacao", "habilitados_negociacao", "habilitados-negociacao.txt", Consumed: false);

    public static readonly IReadOnlyList<CetipExport> All =
    [
        Figures, DerivativeFields, FigureFields, StrategyFields,
        CoeDomains, DerivativeDomains, Underlyings, Qualifications, Participants,
        Institutions, TradingEnabled
    ];

    /// <summary>The exports the platform actually reads — what a sync fetches by default.</summary>
    public static IEnumerable<CetipExport> Default => All.Where(e => e.Consumed);

    public static CetipExport? ByName(string name) =>
        All.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
}
