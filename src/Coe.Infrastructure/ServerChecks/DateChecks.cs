using Coe.Core.Expressions;
using Coe.Core.Templates;
using Coe.Core.Validation;

namespace Coe.Infrastructure.ServerChecks;

/// <summary>
/// <c>businessDay</c> — the date at <c>args.path</c> must be a business day on
/// <c>args.calendar</c>. The browser has no holiday table, which is why the rules that use this
/// are marked <c>execution: server</c> and answered by the validate endpoint.
/// </summary>
public sealed class BusinessDayCheck(IBusinessCalendar calendar) : ServerCheckBase
{
    public override string Id => "businessDay";

    public override bool? Evaluate(TemplateRule rule, EvaluationContext ctx)
    {
        var date = Values.AsDate(Read(rule, ctx, "path"));
        if (date is null) return null;
        return calendar.IsBusinessDay(Arg(rule, "calendar") ?? BookingFacts.DefaultCalendar, date.Value);
    }
}

/// <summary>
/// <c>businessDaysBefore</c> — the date at <c>args.path</c> must sit between
/// <c>args.minimum</c> and <c>args.maximum</c> business days before <c>args.referencePath</c>.
/// Used for B3's "the capture window ends up to 5 business days before maturity".
/// </summary>
public sealed class BusinessDaysBeforeCheck(IBusinessCalendar calendar) : ServerCheckBase
{
    public override string Id => "businessDaysBefore";

    public override bool? Evaluate(TemplateRule rule, EvaluationContext ctx)
    {
        var date = Values.AsDate(Read(rule, ctx, "path"));
        var reference = Values.AsDate(Read(rule, ctx, "referencePath"));
        if (date is null || reference is null) return null;

        var distance = calendar.BusinessDaysBetween(Arg(rule, "calendar") ?? BookingFacts.DefaultCalendar, date.Value, reference.Value);
        var min = (int)(ArgNumber(rule, "minimum") ?? 0m);
        var max = (int)(ArgNumber(rule, "maximum") ?? int.MaxValue);
        return distance >= min && distance <= max;
    }
}

/// <summary>
/// <c>observationCountMatchesCalendar</c> — the registered number of range-accrual fixings must
/// match the business days in the observation window, within a 5% tolerance for schedules that
/// skip a day here or there.
/// </summary>
public sealed class ObservationCountCheck(IBusinessCalendar calendar) : ServerCheckBase
{
    public override string Id => "observationCountMatchesCalendar";

    public override bool? Evaluate(TemplateRule rule, EvaluationContext ctx)
    {
        var count = Values.AsNumber(Read(rule, ctx, "countPath"));
        var start = Values.AsDate(Read(rule, ctx, "startPath"));
        var end = Values.AsDate(Read(rule, ctx, "endPath"));
        if (count is null || start is null || end is null) return null;

        var expected = calendar.BusinessDaysBetween(Arg(rule, "calendar") ?? BookingFacts.DefaultCalendar, start.Value, end.Value);
        if (expected == 0) return null;

        var tolerance = Math.Max(1m, expected * 0.05m);
        return Math.Abs(count.Value - expected) <= tolerance;
    }
}

/// <summary>
/// <c>uniqueInstrumentCode</c> — no other asset may carry the same Código IF.
///
/// The answer is fetched once by the booking service before the pass starts and handed in as a
/// fact, rather than queried here. Checks run inside the synchronous engine, so a query at this
/// point would either block a thread or force the whole engine to become async — and it would
/// run once per evaluation instead of once per request. The database still has a filtered
/// unique index; this check exists to turn that collision into a message on the field.
/// </summary>
public sealed class UniqueInstrumentCodeCheck : ServerCheckBase
{
    public override string Id => "uniqueInstrumentCode";

    public override bool? Evaluate(TemplateRule rule, EvaluationContext ctx)
    {
        var code = Values.AsString(Read(rule, ctx, "path"));
        if (string.IsNullOrWhiteSpace(code)) return null;

        // Absent when the caller resolved no facts, e.g. a pass that never saw an instrument code.
        return ctx.ResolveVariable(BookingFacts.InstrumentCodeTaken) switch
        {
            bool taken => !taken,
            _ => null
        };
    }
}

/// <summary>
/// Names of the facts the booking service resolves before validating, so a server-side check can
/// stay a pure function of the instance plus these values.
/// </summary>
public static class BookingFacts
{
    public const string DefaultCalendar = "BRASIL";

    /// <summary>True when another asset already carries the instrument code on the instance.</summary>
    public const string InstrumentCodeTaken = "instrumentCodeTaken";
}
