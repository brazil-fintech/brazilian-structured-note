using Coe.Core.Expressions;
using Coe.Core.Templates;
using Coe.Core.Validation;
using Microsoft.EntityFrameworkCore;

namespace Coe.Infrastructure.ServerChecks;

/// <summary>
/// <c>businessDay</c> — the date at <c>args.path</c> must be a business day on
/// <c>args.calendar</c>. The browser has no holiday table, which is exactly why the rules
/// that use this are marked <c>execution: server</c> and answered by the validate endpoint.
/// </summary>
public sealed class BusinessDayCheck(IBusinessCalendar calendar) : ServerCheckBase
{
    public override string Id => "businessDay";

    public override bool? Evaluate(TemplateRule rule, EvaluationContext ctx)
    {
        var date = Values.AsDate(Read(rule, ctx, "path"));
        if (date is null) return null;
        return calendar.IsBusinessDay(Arg(rule, "calendar") ?? "BRASIL", date.Value);
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

        var distance = calendar.BusinessDaysBetween(Arg(rule, "calendar") ?? "BRASIL", date.Value, reference.Value);
        var min = (int)(ArgNumber(rule, "minimum") ?? 0m);
        var max = (int)(ArgNumber(rule, "maximum") ?? int.MaxValue);
        return distance >= min && distance <= max;
    }
}

/// <summary>
/// <c>observationCountMatchesCalendar</c> — the registered number of range-accrual fixings
/// must match the business days in the observation window, within a 5% tolerance for
/// schedules that skip a day here or there.
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

        var expected = calendar.BusinessDaysBetween(Arg(rule, "calendar") ?? "BRASIL", start.Value, end.Value);
        if (expected == 0) return null;

        var tolerance = Math.Max(1m, expected * 0.05m);
        return Math.Abs(count.Value - expected) <= tolerance;
    }
}

/// <summary>
/// <c>uniqueInstrumentCode</c> — no other asset may carry the same Código IF. The database has
/// a filtered unique index for the same invariant; this check turns the collision into a
/// message on the field instead of a failed insert.
/// </summary>
public sealed class UniqueInstrumentCodeCheck(CoeDbContext db, ICurrentAssetContext current) : ServerCheckBase
{
    public override string Id => "uniqueInstrumentCode";

    public override bool? Evaluate(TemplateRule rule, EvaluationContext ctx)
    {
        var code = Values.AsString(Read(rule, ctx, "path"));
        if (string.IsNullOrWhiteSpace(code)) return null;

        var editingId = current.AssetId;
        return !db.Assets.AsNoTracking()
            .Any(a => a.InstrumentCode == code && (editingId == null || a.Id != editingId));
    }
}

/// <summary>
/// Carries the id of the asset currently being validated, so uniqueness checks do not flag an
/// asset against its own stored row.
/// </summary>
public interface ICurrentAssetContext
{
    Guid? AssetId { get; set; }
}

public sealed class CurrentAssetContext : ICurrentAssetContext
{
    public Guid? AssetId { get; set; }
}
