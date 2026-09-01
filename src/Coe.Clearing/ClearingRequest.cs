using System.Text.Json.Nodes;
using Coe.Core.Templates;

namespace Coe.Clearing;

/// <summary>
/// Everything the upload files need that is not in the booked instance itself.
/// </summary>
/// <param name="Template">
/// The template the asset was booked against. It is what turns a stored option code into the
/// code B3 registers, and what says which attributes belong in the variable-data record.
/// </param>
/// <param name="Values">The booked instance.</param>
/// <param name="ParticipantName">
/// "Nome Simplificado do Participante": the issuer's short name at B3, which every header
/// carries. It is in the <c>mnemonicos_cetip</c> export, against the institution's account.
/// </param>
/// <param name="FigureOrdinal">
/// "Tipo COE": the two-digit sequence B3 lists the figure under in <c>DTpFiguras</c>, not the
/// COE001005-style code. Registration is the only file that carries it.
/// </param>
/// <param name="FileDate">The date the header is stamped with; the operation date.</param>
/// <param name="InstrumentCode">
/// Código IF, once B3 has issued one. The cash-flow, basket and fixing-date files take either
/// this or the issuer's own identifier, and B3 requires whichever the other is missing.
/// </param>
public sealed record ClearingRequest(
    FigureTemplate Template,
    JsonObject Values,
    string ParticipantName,
    string FigureOrdinal = "",
    DateOnly FileDate = default,
    string? InstrumentCode = null)
{
    /// <summary>The header date, defaulting to today where the caller did not name one.</summary>
    public DateOnly Stamp => FileDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : FileDate;

    internal InstanceReader Reader() => new(Template, Values);

    /// <summary>Código IF from the request, falling back to the one booked on the instance.</summary>
    internal string? ResolvedInstrumentCode(InstanceReader reader) =>
        string.IsNullOrWhiteSpace(InstrumentCode) ? reader.Text("common.instrumentCode") : InstrumentCode;
}
