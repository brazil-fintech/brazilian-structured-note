namespace Coe.Clearing;

/// <summary>Which side of a barrier fired (4.8.4).</summary>
public enum TriggerDirection
{
    /// <summary>0030 — knock-in.</summary>
    In,

    /// <summary>0031 — knock-out.</summary>
    Out
}

/// <summary>What an Atualização PU carries (4.8.5, seq 06). The numbers are B3's.</summary>
public enum PriceUpdateType
{
    InitialUnderlyingValue = 1,
    SimplifiedUnitPrice = 2,
    MaturityRemunerator = 3,
    FixingQuote = 4,
    KnockOutUnitPrice = 5,
    PathDependentFactor = 6,
    CashFlowUnderlyingValue = 7,
    ReferenceValue = 8,
    LookbackValues = 9,
    ScheduleRemunerator = 10
}

/// <summary>4.8.4 — a barrier firing on a certificate B3 does not calculate itself.</summary>
public sealed record TriggerRequest
{
    public required string ParticipantName { get; init; }
    public required string CoeCode { get; init; }
    public required string IssuerAccount { get; init; }
    public required DateOnly MaturityDate { get; init; }
    public TriggerDirection Direction { get; init; }
    public DateOnly OperationDate { get; init; }
    public DateOnly? UpdateDate { get; init; }

    /// <summary>Left out when B3 calculates the certificate itself.</summary>
    public DateOnly? TriggerDate { get; init; }

    /// <summary>Only for a knock-out rebate paid on the spot.</summary>
    public decimal? UnderlyingValue { get; init; }

    internal DateOnly Stamp => OperationDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : OperationDate;
    internal string Operation => Direction == TriggerDirection.In ? "0030" : "0031";
}

/// <summary>
/// 4.8.5 — one of the ten things an issuer updates on a live certificate. Which of the value
/// slots is read depends on <see cref="Type"/>; the layout shares each position between several
/// meanings, so they are named here for what they hold rather than for the position.
/// </summary>
public sealed record PriceUpdateRequest
{
    public required string ParticipantName { get; init; }
    public required string CoeCode { get; init; }
    public required string IssuerAccount { get; init; }
    public required PriceUpdateType Type { get; init; }
    public DateOnly OperationDate { get; init; }
    public DateOnly? UpdateDate { get; init; }

    /// <summary>Types 2 and 5.</summary>
    public decimal? UnitPrice { get; init; }

    /// <summary>Types 4 and 7.</summary>
    public decimal? UnderlyingValue { get; init; }

    /// <summary>Types 1, 8 and 9.</summary>
    public decimal? InitialUnderlyingValue { get; init; }

    /// <summary>An international underlying settled composite.</summary>
    public decimal? CurrencyQuote { get; init; }

    /// <summary>Type 3: the remunerator itself, or its correction factor on a schedule.</summary>
    public decimal? RemuneratorValue { get; init; }

    /// <summary>Type 6, or the interest factor of a VCP schedule.</summary>
    public decimal? PathDependentFactor { get; init; }

    /// <summary>Type 9.</summary>
    public DateOnly? LookbackReferenceDate { get; init; }

    /// <summary>Type 8.</summary>
    public decimal? LookbackInitialValue { get; init; }

    /// <summary>Required only for a basket, where the update names which component it is about.</summary>
    public string? UnderlyingCode { get; init; }

    internal DateOnly Stamp => OperationDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : OperationDate;
}

/// <summary>4.8.6 — the issuer's own mark for the day.</summary>
public sealed record MarkToMarketRequest
{
    public required string ParticipantName { get; init; }
    public required string InstrumentCode { get; init; }
    public required string IssuerAccount { get; init; }
    public required DateOnly ReferenceDate { get; init; }
    public required decimal Value { get; init; }

    /// <summary>True credits the holder; the layout writes it as <c>+</c> or <c>-</c>.</summary>
    public bool IsCredit { get; init; } = true;

    public DateOnly OperationDate { get; init; }

    internal DateOnly Stamp => OperationDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : OperationDate;
}

/// <summary>4.8.7 — the sensitivity analysis, as two scenarios of notional bounds.</summary>
public sealed record NotionalRequest
{
    public required string ParticipantName { get; init; }
    public required string InstrumentCode { get; init; }
    public required DateOnly ReferenceDate { get; init; }
    public decimal? MinimumScenario1 { get; init; }
    public decimal? MaximumScenario1 { get; init; }
    public decimal? MinimumScenario2 { get; init; }
    public decimal? MaximumScenario2 { get; init; }

    /// <summary>The risk factor the scenarios move, related to the underlying.</summary>
    public string? RiskFactor { get; init; }

    public DateOnly OperationDate { get; init; }

    internal DateOnly Stamp => OperationDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : OperationDate;
}

/// <summary>4.8.8 — what is actually delivered on a certificate registered for physical delivery.</summary>
public sealed record PhysicalDeliveryRequest
{
    public required string ParticipantName { get; init; }
    public required string InstrumentCode { get; init; }
    public required string IssuerAccount { get; init; }
    public required decimal DeliveryValue { get; init; }
    public required decimal FinancialValue { get; init; }
    public DateOnly OperationDate { get; init; }

    internal DateOnly Stamp => OperationDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : OperationDate;
}

/// <summary>4.8.11 — the factor of an extraordinary payment the registration admitted.</summary>
public sealed record ExtraordinaryPaymentRequest
{
    public required string ParticipantName { get; init; }
    public required string InstrumentCode { get; init; }
    public required string IssuerAccount { get; init; }
    public required decimal Factor { get; init; }
    public DateOnly OperationDate { get; init; }

    internal DateOnly Stamp => OperationDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : OperationDate;
}

/// <summary>
/// The six files an issuer sends over the life of a certificate rather than at its registration:
/// a barrier firing, a price or quotation the certificate needs and B3 cannot capture, the
/// issuer's own mark, the sensitivity analysis, a physical delivery and an extraordinary payment.
///
/// None of them come out of the booked instance. They are events, and the values in them are
/// known on the day, so each takes what it needs and nothing more.
/// </summary>
public static class CetipLifecycleFiles
{
    private const string InstrumentType = "COE";

    /// <summary>4.8.4 — Disparo Trigger COE.</summary>
    public static CetipFile Trigger(TriggerRequest request)
    {
        const string layout = "4.8.4 Disparo Trigger COE";
        var operation = request.Operation;

        var lines = new List<string>
        {
            CompactHeader(layout, operation, request.ParticipantName, request.Stamp),
            new FixedWidthRecord(layout, "Registro da Operação")
                .Literal(1, 5, InstrumentType)
                .Literal(6, 6, "1")
                .Literal(7, 10, operation)
                .Text(11, 21, request.CoeCode)
                .Text(22, 31, request.IssuerAccount)
                .Date(32, 39, request.MaturityDate)
                .Literal(40, 40, request.Direction == TriggerDirection.In ? "1" : "2")
                .Date(41, 48, request.UpdateDate)
                .Date(49, 56, request.TriggerDate)
                .Amount(57, 76, request.UnderlyingValue, 8)
                .Build(76)
        };

        return new CetipFile(layout, operation, CetipFile.Name(operation, request.ParticipantName, request.Stamp), lines);
    }

    /// <summary>4.8.5 — Atualização PU.</summary>
    public static CetipFile PriceUpdate(PriceUpdateRequest request)
    {
        const string layout = "4.8.5 Atualização PU";
        const string operation = "0810";

        var lines = new List<string>
        {
            CompactHeader(layout, operation, request.ParticipantName, request.Stamp),
            new FixedWidthRecord(layout, "Dados Fixos")
                .Literal(1, 5, InstrumentType)
                .Literal(6, 6, "1")
                .Literal(7, 10, operation)
                .Text(11, 21, request.CoeCode)
                .Text(22, 31, request.IssuerAccount)
                .Text(32, 33, ((int)request.Type).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(2, '0'))
                .Date(34, 41, request.UpdateDate)
                .Amount(42, 61, request.UnitPrice, 8)
                .Amount(62, 81, request.UnderlyingValue, 8)
                .Amount(82, 101, request.InitialUnderlyingValue, 8)
                .Amount(102, 113, request.CurrencyQuote, 6)
                .Amount(114, 133, request.RemuneratorValue, 8)
                .Amount(134, 153, request.PathDependentFactor, 8)
                .Date(154, 161, request.LookbackReferenceDate)
                .Amount(162, 181, request.LookbackInitialValue, 8)
                .Text(182, 191, request.UnderlyingCode)
                .Build(191)
        };

        return new CetipFile(layout, operation, CetipFile.Name(operation, request.ParticipantName, request.Stamp), lines);
    }

    /// <summary>4.8.6 — Registro de Marcação a Mercado.</summary>
    public static CetipFile MarkToMarket(MarkToMarketRequest request)
    {
        const string layout = "4.8.6 Marcação a Mercado";
        const string operation = "0475";

        var lines = new List<string>
        {
            CompactHeader(layout, operation, request.ParticipantName, request.Stamp),
            new FixedWidthRecord(layout, "Dados Fixos")
                .Literal(1, 5, InstrumentType)
                .Literal(6, 6, "1")
                .Literal(7, 10, operation)
                .Text(11, 21, request.InstrumentCode)
                .Number(22, 29, CetipRegistrationFiles.Digits(request.IssuerAccount))
                .Date(30, 37, request.ReferenceDate)
                .Amount(38, 55, request.Value, 8)
                .Literal(56, 56, request.IsCredit ? "+" : "-")
                .Build(56)
        };

        return new CetipFile(layout, operation, CetipFile.Name(operation, request.ParticipantName, request.Stamp), lines);
    }

    /// <summary>4.8.7 — Registro de Notional.</summary>
    public static CetipFile Notional(NotionalRequest request)
    {
        const string layout = "4.8.7 Registro de Notional";
        const string operation = "0848";

        var lines = new List<string>
        {
            CompactHeader(layout, operation, request.ParticipantName, request.Stamp),
            new FixedWidthRecord(layout, "Registro")
                .Literal(1, 5, InstrumentType)
                .Literal(6, 6, "1")
                .Literal(7, 10, operation)
                .Text(11, 21, request.InstrumentCode)
                .Amount(22, 41, request.MinimumScenario1, 8)
                .Amount(42, 61, request.MaximumScenario1, 8)
                .Amount(62, 81, request.MinimumScenario2, 8)
                .Amount(82, 101, request.MaximumScenario2, 8)
                .Text(102, 141, request.RiskFactor)
                .Date(142, 149, request.ReferenceDate)
                .Build(149)
        };

        return new CetipFile(layout, operation, CetipFile.Name(operation, request.ParticipantName, request.Stamp), lines);
    }

    /// <summary>4.8.8 — Indicação de Entrega Física.</summary>
    public static CetipFile PhysicalDelivery(PhysicalDeliveryRequest request)
    {
        const string layout = "4.8.8 Indicação de Entrega Física";
        const string operation = "0855";

        var lines = new List<string>
        {
            VersionedHeader(layout, operation, request.ParticipantName, request.Stamp),
            new FixedWidthRecord(layout, "Registro da Operação")
                .Literal(1, 5, InstrumentType)
                .Literal(6, 6, "1")
                .Literal(7, 10, operation)
                .Text(11, 18, request.IssuerAccount)
                .Text(19, 32, request.InstrumentCode)
                .Amount(33, 49, request.DeliveryValue, 2)
                .Amount(50, 66, request.FinancialValue, 2)
                .Build(66)
        };

        return new CetipFile(layout, operation, CetipFile.Name(operation, request.ParticipantName, request.Stamp), lines);
    }

    /// <summary>4.8.11 — Indicação de Pagamento Extraordinário.</summary>
    public static CetipFile ExtraordinaryPayment(ExtraordinaryPaymentRequest request)
    {
        const string layout = "4.8.11 Pagamento Extraordinário";
        const string operation = "0808";

        var lines = new List<string>
        {
            VersionedHeader(layout, operation, request.ParticipantName, request.Stamp),
            new FixedWidthRecord(layout, "Body")
                .Literal(1, 5, InstrumentType)
                .Literal(6, 6, "1")
                .Literal(7, 10, operation)
                .Text(11, 18, request.IssuerAccount)
                .Text(19, 32, request.InstrumentCode)
                .Amount(33, 52, request.Factor, 8)
                .Build(52)
        };

        return new CetipFile(layout, operation, CetipFile.Name(operation, request.ParticipantName, request.Stamp), lines);
    }

    /// <summary>The header of the layouts that carry no version field: five fields, 38 characters.</summary>
    private static string CompactHeader(string layout, string operation, string participant, DateOnly date) =>
        new FixedWidthRecord(layout, "Header")
            .Literal(1, 5, InstrumentType)
            .Literal(6, 6, "0")
            .Literal(7, 10, operation)
            .Text(11, 30, participant)
            .Date(31, 38, date)
            .Build(38);

    /// <summary>The header of the layouts that close with a layout version: 43 characters.</summary>
    private static string VersionedHeader(string layout, string operation, string participant, DateOnly date) =>
        new FixedWidthRecord(layout, "Header")
            .Literal(1, 5, InstrumentType)
            .Literal(6, 6, "0")
            .Literal(7, 10, operation)
            .Text(11, 30, participant)
            .Date(31, 38, date)
            .Number(39, 43, 1)
            .Build(43);
}
