namespace Coe.Clearing;

/// <summary>Which side of the operation the participant launching it is on (4.8.2/4.8.3, seq 06).</summary>
public enum CoeSide
{
    /// <summary>01 — venda carteira própria.</summary>
    Sell = 1,

    /// <summary>02 — compra.</summary>
    Buy = 2
}

/// <summary>The operation codes the Lançamento de Operações layout lists at its end.</summary>
public static class CoeOperationCodes
{
    public const string DepositWithoutSettlement = "0001";
    public const string DepositWithSettlement = "0002";
    public const string EarlyRedemption = "0014";
    public const string TechnicalReserveLink = "0023";
    public const string CustodyBlock = "0025";
    public const string CustodyUnblock = "0026";
    public const string TechnicalReserveUnlinkAdvance = "0035";
    public const string OutrightTrade = "0052";
    public const string CancelDepositWithoutSettlement = "0101";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        DepositWithoutSettlement, DepositWithSettlement, EarlyRedemption, TechnicalReserveLink,
        CustodyBlock, CustodyUnblock, TechnicalReserveUnlinkAdvance, OutrightTrade,
        CancelDepositWithoutSettlement
    };
}

/// <summary>
/// One custody operation on a registered COE — a deposit, a withdrawal, an early redemption, a
/// block. None of it is on the certificate itself, so none of it comes from the booked instance.
/// </summary>
public sealed record CoeOperationRequest
{
    /// <summary>From <see cref="CoeOperationCodes"/>.</summary>
    public required string OperationCode { get; init; }

    public required string ParticipantName { get; init; }
    public required string IssuerAccount { get; init; }
    public required string HolderAccount { get; init; }
    public required long Quantity { get; init; }

    public DateOnly OperationDate { get; init; }
    public string? InstrumentCode { get; init; }
    public CoeSide Side { get; init; } = CoeSide.Sell;
    public string? MyNumber { get; init; }
    public decimal? UnitPrice { get; init; }

    /// <summary>0 none, 1 multilateral, 2 gross, 4 bilateral.</summary>
    public int SettlementModality { get; init; }

    public string? SettlementBank { get; init; }

    /// <summary>CPF/CNPJ of the beneficial owner behind the launching participant.</summary>
    public string? Document { get; init; }

    /// <summary><c>PF</c> or <c>PJ</c>; required once <see cref="Document"/> is given.</summary>
    public string? Nature { get; init; }

    /// <summary>Only for a block.</summary>
    public string? Reason { get; init; }

    /// <summary>1 judicial, 2 intervention, 3 at the participant's request, 4 other.</summary>
    public int? BlockType { get; init; }

    /// <summary><c>V</c> to link, <c>D</c> to unlink; only for a technical-reserve operation.</summary>
    public string? TechnicalReserve { get; init; }

    public DateOnly? OriginalOperationDate { get; init; }
    public string? OriginalOperationNumber { get; init; }

    /// <summary>Only for the D0 layout, where B3 requires the issuer's own code.</summary>
    public string? ExternalIdentifier { get; init; }

    internal DateOnly Stamp => OperationDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : OperationDate;
}

/// <summary>
/// The two custody-operation layouts, 4.8.2 and 4.8.3. They carry the same operation in two
/// shapes: the older one keyed on the Código IF, and the newer D0 deposit keyed on the issuer's
/// own identifier, which is what lets a deposit be launched the same day as the registration,
/// before B3 has issued a Código IF at all.
/// </summary>
public static class CetipOperationFiles
{
    private const string InstrumentType = "COE";
    private const string Action = "LCOP";

    /// <summary>4.8.2 — Lançamento de Operações.</summary>
    public static CetipFile Operation(CoeOperationRequest request)
    {
        const string layout = "4.8.2 Lançamento de Operações";

        var lines = new List<string>
        {
            OperationHeader(layout, request, version: 10),
            new FixedWidthRecord(layout, "Registro da Operação")
                .Literal(1, 5, InstrumentType)
                .Literal(6, 6, "1")
                .Text(7, 10, request.OperationCode)
                .Text(11, 24, request.InstrumentCode)
                .Filler(25, 25)
                .Number(26, 27, (long)request.Side)
                .Number(28, 35, CetipRegistrationFiles.Digits(request.IssuerAccount))
                .Number(36, 45, CetipRegistrationFiles.Digits(request.MyNumber))
                // The Cetip operation number and the association number are B3's to assign.
                .Filler(46, 61)
                .Filler(62, 67)
                .Number(68, 75, CetipRegistrationFiles.Digits(request.HolderAccount))
                .Number(76, 89, request.Quantity)
                .Filler(90, 104)
                .Amount(105, 122, request.UnitPrice, 8)
                .Number(123, 123, request.SettlementModality)
                .Number(124, 131, CetipRegistrationFiles.Digits(request.SettlementBank))
                .Filler(132, 139)
                .Filler(140, 157)
                .Text(158, 158, request.TechnicalReserve)
                .Date(159, 166, request.OriginalOperationDate)
                .Text(167, 182, request.OriginalOperationNumber)
                .Filler(183, 183)
                .Filler(184, 191)
                .Filler(192, 192)
                .Number(193, 210, CetipRegistrationFiles.Digits(request.Document))
                .Text(211, 212, request.Nature)
                .Text(213, 412, request.Reason)
                .Filler(413, 420)
                .Filler(421, 438)
                .Filler(439, 456)
                .Filler(457, 457)
                .Filler(458, 458)
                .Filler(459, 460)
                .Number(461, 461, request.BlockType)
                .Filler(462, 469)
                .Filler(470, 470)
                .Literal(471, 471, "<")
                .Build(471)
        };

        return new CetipFile(layout, Action, CetipFile.Name(request.OperationCode, request.ParticipantName, request.Stamp), lines);
    }

    /// <summary>
    /// 4.8.3 — Lançamento de Depósito SEM FINANCEIRO em D0. Same operation, keyed on the
    /// issuer's own identifier: the Código IF field is sent blank because B3 has not issued one
    /// yet on the day of the registration.
    /// </summary>
    public static CetipFile DepositWithoutSettlement(CoeOperationRequest request)
    {
        const string layout = "4.8.3 Depósito sem financeiro em D0";

        var lines = new List<string>
        {
            OperationHeader(layout, request, version: 12),
            new FixedWidthRecord(layout, "Registro da Operação")
                .Literal(1, 5, InstrumentType)
                .Literal(6, 6, "1")
                .Text(7, 10, request.OperationCode)
                // "Em branco": this layout addresses the certificate by the Código Identificador
                // at the end of the record instead.
                .Filler(11, 24)
                .Filler(25, 25)
                .Number(26, 27, (long)request.Side)
                .Number(28, 35, CetipRegistrationFiles.Digits(request.IssuerAccount))
                .Number(36, 45, CetipRegistrationFiles.Digits(request.MyNumber))
                .Filler(46, 61)
                .Filler(62, 67)
                .Number(68, 75, CetipRegistrationFiles.Digits(request.HolderAccount))
                .Number(76, 89, request.Quantity)
                .Filler(90, 104)
                .Amount(105, 122, request.UnitPrice, 8)
                .Number(123, 123, request.SettlementModality)
                .Number(124, 131, CetipRegistrationFiles.Digits(request.SettlementBank))
                .Filler(132, 139)
                .Filler(140, 157)
                .Filler(158, 158)
                .Filler(159, 166)
                .Filler(167, 182)
                .Filler(183, 183)
                .Filler(184, 191)
                .Filler(192, 192)
                .Number(193, 210, CetipRegistrationFiles.Digits(request.Document))
                .Text(211, 212, request.Nature)
                .Text(213, 412, request.Reason)
                .Filler(413, 420)
                .Filler(421, 438)
                .Filler(439, 456)
                .Filler(457, 457)
                .Filler(458, 458)
                .Filler(459, 460)
                .Number(461, 461, request.BlockType)
                .Filler(462, 469)
                .Filler(470, 470)
                .Filler(471, 471)
                .Filler(472, 479)
                .Filler(480, 497)
                .Filler(498, 499)
                .Text(500, 519, request.ExternalIdentifier)
                .Literal(520, 520, "<")
                .Build(520)
        };

        return new CetipFile(layout, Action, CetipFile.Name(request.OperationCode, request.ParticipantName, request.Stamp), lines);
    }

    /// <summary>
    /// The custody-operation header. Unlike the registration files it carries a layout version
    /// and closes with the end-of-line delimiter.
    /// </summary>
    private static string OperationHeader(string layout, CoeOperationRequest request, int version) =>
        new FixedWidthRecord(layout, "Header")
            .Literal(1, 5, InstrumentType)
            .Literal(6, 6, "0")
            .Literal(7, 10, Action)
            .Text(11, 30, request.ParticipantName)
            .Date(31, 38, request.Stamp)
            .Number(39, 43, version)
            .Literal(44, 44, "<")
            .Build(44);
}
