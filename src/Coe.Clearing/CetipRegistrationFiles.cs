using System.Globalization;

namespace Coe.Clearing;

/// <summary>
/// The four upload files a booked COE produces on its own, from sections 4.8.1, 4.8.9, 4.8.10
/// and 4.8.12 of the <em>ENVIAR ARQUIVOS</em> manual: the registration itself, and the three
/// files that complete it where the registration leaves it pending.
///
/// Positions are the manual's, written out field by field so the layout can be read against the
/// page it came from. <see cref="FixedWidthRecord"/> refuses a field that does not start where
/// the manual says it does, which is what keeps a transcription slip from becoming a file B3
/// accepts and reads wrongly.
///
/// Two conventions run through all of them. A domain value is written as B3's own code from the
/// published domain export, padded to the width of the field — the platform never invents a
/// code. And a field whose context does not apply is left blank, whatever its declared type,
/// which is the manual's own instruction.
/// </summary>
public static class CetipRegistrationFiles
{
    private const string InstrumentType = "COE";

    /// <summary>4.8.1 — Registro COE. Header, fixed data, then one line per variable attribute.</summary>
    public static CetipFile Registration(ClearingRequest request)
    {
        const string layout = "4.8.1 Registro COE";
        const string operation = "0001";

        var reader = request.Reader();
        var variables = reader.VariableFields();

        var lines = new List<string> { Header(layout, operation, request) };
        lines.Add(RegistrationFixed(layout, operation, request, reader, variables.Count));

        foreach (var (field, value) in variables)
            lines.Add(RegistrationVariable(layout, operation, request, field.B3DataCode!, value));

        return new CetipFile(layout, operation, CetipFile.Name(operation, request.ParticipantName, request.Stamp), lines);
    }

    private static string RegistrationFixed(
        string layout, string operation, ClearingRequest request, InstanceReader reader, int variableCount)
    {
        var record = new FixedWidthRecord(layout, "Dados Fixos");

        record.Literal(1, 5, InstrumentType)
              .Literal(6, 6, "1")
              .Literal(7, 10, operation)
              .Number(11, 18, Digits(reader.Text("common.issuerAccount")))
              // "Tipo COE" is the sequence number of the figure, not its COE001005-style code.
              .Text(19, 20, request.FigureOrdinal.PadLeft(2, '0'))
              .Text(21, 120, reader.Text("common.commercialName"))
              .Date(121, 128, reader.Date("common.issueDate"))
              .Date(129, 136, reader.Date("common.maturityDate"))
              // Declared X(12) but holding a count; zero-padded, as every other quantity is.
              .Number(137, 148, reader.Integer("common.quantity"))
              .Amount(149, 168, reader.Number("common.unitIssuePrice"), 8)
              .Amount(169, 186, reader.Number("common.notional"), 2)
              .Date(187, 194, reader.Date("common.forwardIssueDate"))
              .Text(195, 206, reader.Text("common.isin"))
              .Text(207, 208, reader.PaddedDomainCode("common.modality", 2))
              .Amount(209, 215, reader.Number("common.guaranteedCapital"), 4)
              .Amount(216, 222, reader.Number("terms.baseApplication"), 4)
              .Text(223, 223, YesNo(reader.Flag("terms.cvmResolution8")))
              .Text(224, 225, reader.PaddedDomainCode("underlying.dividendProtection", 2))
              .Text(226, 226, reader.DomainCode("terms.issuerPosition"))
              .Text(227, 256, reader.Text("underlying.assetClass"))
              .Text(257, 266, Asset(reader))
              .Amount(267, 286, BasketAware(reader, "underlying.initialValue"), 8)
              .Text(287, 288, reader.PaddedDomainCode("remuneration.maturityRemunerator", 2))
              .Text(289, 588, reader.Text("remuneration.remuneratorDescription"))
              .Amount(589, 596, reader.Number("remuneration.floatingPercentage"), 4)
              .Amount(597, 605, reader.Number("remuneration.couponRate"), 4)
              .Text(606, 607, reader.PaddedDomainCode("remuneration.dayCountBasis", 2))
              .Text(608, 609, reader.PaddedDomainCode("terms.earlyRedemption", 2))
              .Text(610, 610, Quanto(reader))
              .Amount(611, 623, BasketAware(reader, "underlying.initialParity"), 8)
              .Number(624, 626, variableCount)
              .Text(627, 646, reader.Text("common.externalIdentifier"))
              .Number(647, 654, Digits(reader.Text("terms.registrarAccount")))
              .Text(655, 655, YesOrBlank(reader.Flag("terms.physicalDelivery")))
              .Text(656, 656, YesOrBlank(reader.Flag("remuneration.hasCashFlow")))
              .Date(657, 664, reader.Date("underlying.parityStartDate"))
              .Date(665, 672, reader.Date("underlying.parityFixingDate"))
              .Amount(673, 692, reader.Number("remuneration.initialRemuneratorQuote"), 8)
              .Date(693, 700, reader.Date("remuneration.remuneratorStartDate"))
              .Date(701, 708, reader.Date("remuneration.remuneratorFixingDate"))
              .Text(709, 709, YesOrBlank(reader.Flag("underlying.hasLookback")))
              .Text(710, 710, reader.Flag("underlying.hasLookback") ? reader.DomainCode("underlying.lookbackCriterion") : null)
              .Date(711, 718, reader.Date("underlying.lookbackStart"))
              .Date(719, 726, reader.Date("underlying.lookbackEnd"))
              .Text(727, 727, reader.DomainCode("terms.custodyRegime"))
              .Text(728, 728, reader.DomainCode("terms.extraordinaryPayment"))
              .Text(729, 1028, reader.Flag("terms.physicalDelivery") ? reader.Text("terms.physicalDeliveryDescription") : null)
              .Number(1029, 1036, Digits(reader.Text("deposit.beneficiaryAccount")))
              .Text(1037, 1054, reader.Text("deposit.beneficiaryDocument"))
              .Text(1055, 1056, reader.DomainCode("deposit.beneficiaryNature"))
              .Number(1057, 1066, Digits(reader.Text("deposit.myNumber")))
              .Amount(1067, 1086, reader.Number("deposit.depositUnitPrice"), 8)
              .Text(1087, 1087, reader.DomainCode("deposit.settlementModality"))
              .Number(1088, 1095, Digits(reader.Text("deposit.settlementBank")))
              // "Não enviar 0. Preencher com espaços em brancos" — a zero here is a real code.
              .Text(1096, 1100, reader.DomainCode("terms.earlyRedemptionCondition"))
              .Text(1101, 1102, reader.PaddedDomainCode("terms.functionality", 2))
              .Text(1103, 1103, YesOrBlank(reader.Flag("terms.issuerCallClause")));

        return record.Build(1103);
    }

    private static string RegistrationVariable(
        string layout, string operation, ClearingRequest request, string fieldCode, string value) =>
        new FixedWidthRecord(layout, "Dados Variáveis")
            .Literal(1, 5, InstrumentType)
            .Literal(6, 6, "2")
            .Literal(7, 10, operation)
            .Date(11, 18, request.Stamp)
            .Text(19, 26, fieldCode)
            .Text(27, 326, value)
            .Build(326);

    /// <summary>4.8.9 — Registro Fluxo de Caixa (FLUX). Completes a registration left pending on its schedule.</summary>
    public static CetipFile CashFlow(ClearingRequest request)
    {
        const string layout = "4.8.9 Registro Fluxo de Caixa";
        const string operation = "FLUX";

        var reader = request.Reader();
        var events = reader.Rows("cashflows");

        var lines = new List<string>
        {
            Header(layout, operation, request),
            new FixedWidthRecord(layout, "Dados Fixos")
                .Literal(1, 5, InstrumentType)
                .Literal(6, 6, "1")
                .Literal(7, 10, operation)
                .Number(11, 18, Digits(reader.Text("common.issuerAccount")))
                .Text(19, 29, request.ResolvedInstrumentCode(reader))
                .Text(30, 49, reader.Text("common.externalIdentifier"))
                .Text(50, 51, reader.PaddedDomainCode("remuneration.couponBarrierCondition", 2))
                .Text(52, 53, reader.PaddedDomainCode("remuneration.callBarrierCondition", 2))
                .Number(54, 56, events.Count)
                .Text(57, 57, YesNo(reader.Flag("remuneration.flowCouponMemory")))
                .Text(58, 59, reader.PaddedDomainCode("remuneration.flowRemunerator", 2))
                .Text(60, 61, reader.PaddedDomainCode("remuneration.flowBasis", 2))
                .Build(61)
        };

        foreach (var e in events)
        {
            lines.Add(new FixedWidthRecord(layout, "Eventos Fluxo de Caixa")
                .Literal(1, 5, InstrumentType)
                .Literal(6, 6, "2")
                .Literal(7, 10, operation)
                .Date(11, 18, e.Date("paymentDate"))
                .Amount(19, 26, e.Number("flowRate"), 4)
                .Amount(27, 35, e.Number("flowSpread"), 4)
                .Amount(36, 47, e.Number("callBarrier"), 8)
                .Amount(48, 59, e.Number("couponBarrier"), 8)
                .Date(60, 67, e.Date("fixingDate"))
                .Date(68, 75, e.Date("fixingDate2"))
                .Text(76, 77, e.PaddedDomainCode("fixingType", 2))
                .Amount(78, 89, e.Number("couponBarrier2"), 8)
                .Amount(90, 101, e.Number("callBarrier2"), 8)
                .Build(101));
        }

        return new CetipFile(layout, operation, CetipFile.Name(operation, request.ParticipantName, request.Stamp), lines);
    }

    /// <summary>4.8.10 — RegistroCestas (CEST). The components of a basket underlying.</summary>
    public static CetipFile Basket(ClearingRequest request)
    {
        const string layout = "4.8.10 RegistroCestas";
        const string operation = "CEST";

        var reader = request.Reader();
        var components = reader.Rows("basket");

        var lines = new List<string>
        {
            Header(layout, operation, request),
            new FixedWidthRecord(layout, "Dados Fixos")
                .Literal(1, 5, InstrumentType)
                .Literal(6, 6, "1")
                .Literal(7, 10, operation)
                .Number(11, 18, Digits(reader.Text("common.issuerAccount")))
                .Text(19, 29, request.ResolvedInstrumentCode(reader))
                .Text(30, 49, reader.Text("common.externalIdentifier"))
                .Text(50, 51, reader.PaddedDomainCode("underlying.basketType", 2))
                .Number(52, 53, components.Count)
                .Text(54, 56, reader.PaddedDomainCode("underlying.basketParityCurrency", 3))
                .Amount(57, 69, reader.Number("underlying.basketInitialParity"), 8)
                .Date(70, 77, reader.Date("underlying.basketParityFixingDate"))
                .Build(77)
        };

        // Worst of and Best of register no weights: B3 requires the field empty, not zero.
        var weighted = reader.Text("underlying.basketType") is not ("WORST_OF" or "BEST_OF");

        foreach (var c in components)
        {
            lines.Add(new FixedWidthRecord(layout, "Ativos da Cesta")
                .Literal(1, 5, InstrumentType)
                .Literal(6, 6, "2")
                .Literal(7, 10, operation)
                .Text(11, 20, c.Text("component"))
                .Text(21, 22, c.PaddedDomainCode("componentQuoteType", 2))
                .Amount(23, 42, c.Number("componentInitialValue"), 8)
                .Date(43, 50, c.Date("componentFixingDate"))
                .Amount(51, 58, weighted ? c.Number("weight") : null, 4)
                .Build(58));
        }

        return new CetipFile(layout, operation, CetipFile.Name(operation, request.ParticipantName, request.Stamp), lines);
    }

    /// <summary>4.8.12 — Registro Datas Fixing (DTFX). The schedule a "Mais Datas" capture period leaves pending.</summary>
    public static CetipFile FixingDates(ClearingRequest request)
    {
        const string layout = "4.8.12 Registro Datas Fixing";
        const string operation = "DTFX";

        var reader = request.Reader();
        var dates = reader.Rows("fixingDates");

        var lines = new List<string>
        {
            Header(layout, operation, request),
            new FixedWidthRecord(layout, "Dados Fixos")
                .Literal(1, 5, InstrumentType)
                .Literal(6, 6, "1")
                .Literal(7, 10, operation)
                .Number(11, 18, Digits(reader.Text("common.issuerAccount")))
                .Text(19, 29, request.ResolvedInstrumentCode(reader))
                // The manual prints this field as 29-31, which overlaps the Código IF ending at
                // 29. Eleven characters from 19 end at 29, so the count starts at 30.
                .Number(30, 32, dates.Count)
                .Build(32)
        };

        foreach (var d in dates)
        {
            lines.Add(new FixedWidthRecord(layout, "Datas Fixing")
                .Literal(1, 5, InstrumentType)
                .Literal(6, 6, "2")
                .Literal(7, 10, operation)
                .Date(11, 18, d.Date("fixingDate"))
                .Build(18));
        }

        return new CetipFile(layout, operation, CetipFile.Name(operation, request.ParticipantName, request.Stamp), lines);
    }

    // ----- shared pieces ------------------------------------------------------------------

    /// <summary>
    /// The header every COE layout in 4.8 opens with: instrument type, line type 0, the
    /// operation, the issuer's short name and the operation date.
    /// </summary>
    internal static string Header(string layout, string operation, ClearingRequest request) =>
        new FixedWidthRecord(layout, "Header")
            .Literal(1, 5, InstrumentType)
            .Literal(6, 6, "0")
            .Literal(7, 10, operation)
            .Text(11, 30, request.ParticipantName)
            .Date(31, 38, request.Stamp)
            .Build(38);

    /// <summary>A basket registers neither an initial value nor a parity here; the CEST file carries both.</summary>
    private static decimal? BasketAware(InstanceReader reader, string path) =>
        IsBasket(reader) ? null : reader.Number(path);

    private static bool IsBasket(InstanceReader reader) =>
        string.Equals(reader.Text("underlying.assetClass"), "CESTA", StringComparison.OrdinalIgnoreCase);

    /// <summary>"Quando a ClasseAtivoSubjacente é 'CESTA', o AtivoSubjacente deve ser 'CESTA' também."</summary>
    private static string? Asset(InstanceReader reader) =>
        IsBasket(reader) ? "CESTA" : reader.Text("underlying.asset");

    /// <summary>Quanto is only registered for the classes that have a currency to vary.</summary>
    private static string? Quanto(InstanceReader reader) =>
        reader.Node("underlying.quanto") is null ? null : YesNo(reader.Flag("underlying.quanto"));

    private static string YesNo(bool value) => value ? "S" : "N";

    /// <summary>For the fields whose domain is {S} alone: yes, or nothing at all.</summary>
    private static string? YesOrBlank(bool value) => value ? "S" : null;

    /// <summary>
    /// The digits of an account or reference number. B3 prints accounts as "12345.40-9" on
    /// screen and registers them as digits, so a value typed either way lands the same.
    /// </summary>
    internal static long? Digits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        return digits.Length > 0 && long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
