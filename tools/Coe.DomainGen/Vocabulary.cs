using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Coe.DomainGen;

/// <summary>Where a generated attribute lands, and under what key.</summary>
/// <param name="Section">Section key: an existing tab, or one this generator creates.</param>
/// <param name="Key">Attribute key inside that section.</param>
public readonly record struct Placement(string Section, string Key);

/// <summary>
/// The bridge between B3's printed field names and this platform's attribute keys.
///
/// The annex names a field the way the registration screen labels it — "Participação cenário de
/// alta (%)", "Rebate KI (%)", "Strike 2(%)" — and repeats a concept with different casing and
/// spacing across figures. Everything here works on a normalized form of the label so those
/// variants collapse, and the ordered patterns turn the family of numbered variants
/// ("Strike 1", "Strike 2", …) into keys without an entry per figure.
/// </summary>
public static partial class Vocabulary
{
    /// <summary>
    /// Labels the common fragments already cover. A figure that lists one of these inherits the
    /// curated attribute instead of getting a second, shallower copy in its payoff tab.
    /// </summary>
    private static readonly Dictionary<string, string> Covered = new(StringComparer.Ordinal)
    {
        ["classe do ativo subjacente"] = "underlying.assetClass",
        ["ativo subjacente"] = "underlying.asset",
        ["valor inicial do ativo subjacente"] = "underlying.initialValue",
        ["valor inicial ativo subjacente"] = "underlying.initialValue",
        ["periodo de captura do ativo subjacente para liquidacao"] = "underlying.fixingWindow",
        ["periodo de captura para liquidacao"] = "underlying.fixingWindow",
        ["data para fixing"] = "underlying.fixingDate",
        ["data inicial para fixing"] = "underlying.fixingWindowStart",
        ["data final para fixing"] = "underlying.fixingWindowEnd",
        ["tipo de fixing para vencimento"] = "underlying.maturityFixingType",
        ["tipo de cotacao para liquidacao"] = "underlying.quoteType",
        ["lookback"] = "underlying.hasLookback",
        ["criterio de lookback"] = "underlying.lookbackCriterion",
        ["data inicial de lookback"] = "underlying.lookbackStart",
        ["data final de lookback"] = "underlying.lookbackEnd",
        ["parametrizacao de cestas"] = "underlying.basketType",
        ["variacao quanto"] = "underlying.quanto",
        ["protecao contra proventos"] = "underlying.dividendProtection",
        ["posicao do emissor no derivativo"] = "terms.issuerPosition",
        ["base aplicacao"] = "terms.baseApplication",
        ["tipo de regime"] = "terms.custodyRegime",
        ["entrega fisica"] = "terms.physicalDelivery",
        ["resgate antecipado"] = "terms.earlyRedemption",
        ["clausula de resgate pelo emissor"] = "terms.issuerCallClause",
        ["direcao da barreira"] = "barriers.barrierDirection",
        ["direcao barreira"] = "barriers.barrierDirection",
        ["periodo de verificacao de barreiras"] = "barriers.verificationPeriod",
        ["data inicial para verificacao"] = "barriers.verificationStart",
        ["data final para verificacao"] = "barriers.verificationEnd",
    };

    /// <summary>Ordered label patterns; the first match wins.</summary>
    private static readonly (Regex Pattern, string Section, string Template)[] Patterns =
    [
        // --- payoff legs ---------------------------------------------------------------
        (Rx(@"^strike (\d+) ?%?$"), "payoff", "strike$1"),
        (Rx(@"^strike (ki|ko) (\d+) ?%?$"), "payoff", "strike$1$2"),
        (Rx(@"^quantidade (\d+)$"), "payoff", "quantity$1"),
        (Rx(@"^quantidade$"), "payoff", "quantity"),
        (Rx(@"^% alocacao (\d+) ?%?$"), "payoff", "allocation$1"),
        (Rx(@"^participacao cenario de alta ?(\d*) ?%$"), "payoff", "upsideParticipation$1"),
        (Rx(@"^participacao cenario de baixa ?(\d*) ?%$"), "payoff", "downsideParticipation$1"),
        (Rx(@"^participacao (\d+) ?%$"), "payoff", "participation$1"),
        (Rx(@"^participacao indexador ?(\d*) ?%$"), "payoff", "indexParticipation$1"),
        (Rx(@"^limitador cenario de alta ?(\d*) ?%$"), "payoff", "upsideCap$1"),
        (Rx(@"^limitador cenario de baixa ?(\d*) ?%$"), "payoff", "downsideCap$1"),
        (Rx(@"^limitador de alta global ?%$"), "payoff", "globalUpsideCap"),
        (Rx(@"^limitador de baixa global ?%$"), "payoff", "globalDownsideCap"),
        (Rx(@"^vertice de alta ?%$"), "payoff", "upperVertex"),
        (Rx(@"^vertice de baixa ?%$"), "payoff", "lowerVertex"),
        (Rx(@"^remuneracao (dentro|acima) ?%$"), "payoff", "insideCoupon"),
        (Rx(@"^remuneracao (fora|abaixo) ?%$"), "payoff", "outsideCoupon"),
        (Rx(@"^remuneracao flutuante dentro ?%$"), "payoff", "insideFloatingPercentage"),
        (Rx(@"^remuneracao flutuante fora ?%$"), "payoff", "outsideFloatingPercentage"),
        (Rx(@"^quantidade de camadas$"), "payoff", "layerCount"),
        (Rx(@"^quantidade minima de ativos acima do strike$"), "payoff", "minimumAssetsAboveStrike"),

        // Numbered repeats of the settlement attributes the underlying block carries once: a
        // figure with two legs captures a fixing per leg, so these belong to the figure.
        (Rx(@"^periodo de captura do ativo subjacente para liquidacao (\d+)$"), "payoff", "fixingWindow$1"),
        (Rx(@"^data para fixing (\d+)$"), "payoff", "fixingDate$1"),
        (Rx(@"^data inicial para fixing (\d+)$"), "payoff", "fixingWindowStart$1"),
        (Rx(@"^data final para fixing (\d+)$"), "payoff", "fixingWindowEnd$1"),
        (Rx(@"^tipo de cotacao para liquidacao (\d+)$"), "payoff", "quoteType$1"),
        (Rx(@"^tipo de cotacao para liquidacao( do)? ativo$"), "payoff", "assetQuoteType"),
        (Rx(@"^tipo de fixing para vencimento (\d+)$"), "payoff", "maturityFixingType$1"),
        (Rx(@"^fixing para pagamentos intermediarios$"), "payoff", "intermediateFixing"),
        (Rx(@"^tipo verificacao dos indexadores$"), "payoff", "indexVerificationType"),

        // --- barriers ------------------------------------------------------------------
        (Rx(@"^barreira (ki|ko) ?(\d*) ?%?$"), "barriers", "$1Barrier$2"),
        (Rx(@"^barreira kiko ?%?$"), "barriers", "kikoBarrier"),
        (Rx(@"^barreira (no )?cenario de alta ?(\d*) ?%$"), "barriers", "upsideBarrier$2"),
        (Rx(@"^barreira (no )?cenario de baixa ?(\d*) ?%$"), "barriers", "downsideBarrier$2"),
        (Rx(@"^barreira de alta ?%$"), "barriers", "upsideBarrier"),
        (Rx(@"^barreira de baixa ?%$"), "barriers", "downsideBarrier"),
        (Rx(@"^barreira superior ?%$"), "barriers", "upperBarrier"),
        (Rx(@"^barreira inferior ?%$"), "barriers", "lowerBarrier"),
        (Rx(@"^barreira ?(\d*) ?%$"), "barriers", "barrier$1"),
        (Rx(@"^fator barreira movel (superior|inferior)$"), "barriers", "movingBarrierFactor$1"),
        (Rx(@"^rebate (ki|ko) ?(\d*) ?%$"), "barriers", "$1Rebate$2"),
        (Rx(@"^rebate (no )?cenario de alta ?(\d*) ?%$"), "barriers", "upsideRebate$2"),
        (Rx(@"^rebate (no )?cenario de baixa ?(\d*) ?%$"), "barriers", "downsideRebate$2"),
        (Rx(@"^rebate ?(\d*) ?%$"), "barriers", "rebate$1"),
        (Rx(@"^periodo de verificacao de barreiras? ?(\d*)$"), "barriers", "verificationPeriod$1"),
        (Rx(@"^tipo de cotacao para verificacao de barreiras? (no )?cenario de alta$"), "barriers", "upsideVerificationQuoteType"),
        (Rx(@"^tipo de cotacao para verificacao de barreiras? (no )?cenario de baixa$"), "barriers", "downsideVerificationQuoteType"),
        (Rx(@"^tipo de cotacao para verificacao$"), "barriers", "verificationQuoteType"),
        (Rx(@"^data (inicial|final) para verificacao ?(\d+)$"), "barriers", "verification$1$2"),
        (Rx(@"^tem ko condicional$"), "barriers", "hasConditionalKnockOut"),

        // --- observation schedule ------------------------------------------------------
        (Rx(@"^data de observacao (\d+)$"), "observations", "observationDate$1"),
        (Rx(@"^data de verificacao (inicial|final)$"), "observations", "verification$1"),

        // --- amortization --------------------------------------------------------------
        (Rx(@"^amortizacao (\d+) ?%$"), "amortization", "amortization$1"),
        (Rx(@"^data de amortizacao (\d+)$"), "amortization", "amortizationDate$1"),

        // --- additional remuneration ---------------------------------------------------
        (Rx(@"^remuneracao adicional$"), "remuneration", "additionalRemunerator"),
        (Rx(@"^cupom remunerador adicional ?%?$"), "remuneration", "additionalCouponRate"),
        (Rx(@"^remunerador flutuante adicional ?%$"), "remuneration", "additionalFloatingPercentage"),
        (Rx(@"^base remunerador adicional$"), "remuneration", "additionalDayCountBasis"),
        (Rx(@"^cupom remunerador camada (\d+) ?%$"), "remuneration", "layerCouponRate$1"),
        (Rx(@"^remunerador flutuante camada (\d+) ?%$"), "remuneration", "layerFloatingPercentage$1"),
        (Rx(@"^cupom periodo (\d+) ?%$"), "remuneration", "periodCouponRate$1"),
        (Rx(@"^base periodo (\d+)$"), "remuneration", "periodDayCountBasis$1"),
        (Rx(@"^deslocamento do accrual do di$"), "remuneration", "diAccrualOffset"),

        // --- multi-underlying ----------------------------------------------------------
        (Rx(@"^ativo subjacente (\d+)$"), "assets", "asset$1"),
        (Rx(@"^classe do ativo subjacente (\d+)$"), "assets", "assetClass$1"),
        (Rx(@"^valor inicial (do )?ativo subjacente (\d+)$"), "assets", "initialValue$2"),
        (Rx(@"^ativo subjacente periodo (\d+)$"), "assets", "periodAsset$1"),
        (Rx(@"^tipo de cesta da (call|put)( ki| ko)?$"), "assets", "basketType$1"),
        (Rx(@"^data da troca do ativo subjacente$"), "assets", "assetSwitchDate"),

        // --- credit figures ------------------------------------------------------------
        (Rx(@"^falencia ou similar$"), "credit", "bankruptcy"),
        (Rx(@"^falha de pagamento$"), "credit", "failureToPay"),
        (Rx(@"^reestruturacao$"), "credit", "restructuring"),
        (Rx(@"^repudio ou moratoria$"), "credit", "repudiationMoratorium"),
        (Rx(@"^intervencao estatal$"), "credit", "governmentIntervention"),
        (Rx(@"^descumprimento de obrigacoes$"), "credit", "obligationDefault"),
        (Rx(@"^vencimento antecipado de obrigacoes$"), "credit", "obligationAcceleration"),
        (Rx(@"^entidade de referencia$"), "credit", "referenceEntity"),
        (Rx(@"^obrigacao de referencia$"), "credit", "referenceObligation"),
        (Rx(@"^quantidade de obrigacoes$"), "credit", "obligationCount"),
        (Rx(@"^agente de calculo$"), "credit", "calculationAgent"),
        (Rx(@"^determinacao de ocorrencia de evento$"), "credit", "eventDetermination"),
        (Rx(@"^condicoes para liquidacao$"), "credit", "settlementConditions"),
        (Rx(@"^recovery value$"), "credit", "recoveryValue"),
        (Rx(@"^periodo de pagamento$"), "credit", "paymentPeriod"),
    ];

    /// <summary>The path of the curated attribute that already covers this label, if any.</summary>
    public static string? CoveredBy(string normalizedLabel) => Covered.GetValueOrDefault(normalizedLabel);

    /// <summary>Section and key for a label the common fragments do not cover.</summary>
    public static Placement Place(string normalizedLabel)
    {
        foreach (var (pattern, section, template) in Patterns)
        {
            var match = pattern.Match(normalizedLabel);
            if (!match.Success) continue;

            var key = pattern.Replace(normalizedLabel, template);
            return new Placement(section, Camel(key));
        }

        return new Placement("payoff", Camel(normalizedLabel));
    }

    /// <summary>
    /// B3's label, reduced so that "Strike 1 (%)", "Strike 1(%)" and "STRIKE 1 %" are one thing.
    /// Accents go, punctuation becomes a space, and the percent sign survives as a word because
    /// it is what distinguishes a rate from a count ("Participação 1 (%)" vs "Quantidade 1").
    /// </summary>
    public static string Normalize(string label)
    {
        var text = label.Replace("(%)", " % ").ToLowerInvariant();
        var stripped = new StringBuilder(text.Length);

        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            stripped.Append(char.IsLetterOrDigit(ch) || ch == '%' ? ch : ' ');
        }

        return WhitespacePattern().Replace(stripped.ToString(), " ").Trim();
    }

    /// <summary>Turns a normalized label or key template into camelCase.</summary>
    public static string Camel(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder();

        foreach (var word in words)
        {
            var cleaned = new string(word.Where(char.IsLetterOrDigit).ToArray());
            if (cleaned.Length == 0) continue;

            if (builder.Length == 0) builder.Append(char.ToLowerInvariant(cleaned[0])).Append(cleaned[1..]);
            else builder.Append(char.ToUpperInvariant(cleaned[0])).Append(cleaned[1..]);
        }

        var key = builder.ToString();
        return key.Length == 0 ? "campo" : char.IsDigit(key[0]) ? "f" + key : key;
    }

    /// <summary>An option code: the label upper-cased with underscores, e.g. "Janela de Datas" -> JANELA_DE_DATAS.</summary>
    public static string OptionCode(string label)
    {
        var normalized = Normalize(label).Replace("%", "pct");
        var code = normalized.Replace(' ', '_').ToUpperInvariant();
        return code.Length == 0 ? "OPCAO" : char.IsDigit(code[0]) ? "V" + code : code;
    }

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
