using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Coe.Infrastructure;

/// <summary>
/// The national business-day calendar behind every date rule. Backed by <c>ref.Holiday</c>;
/// the holiday set is small and changes once a year, so it is cached for the process.
/// </summary>
public interface IBusinessCalendar
{
    bool IsBusinessDay(string calendarCode, DateOnly date);

    /// <summary>Business days strictly between the two dates, negative when <paramref name="from"/> is later.</summary>
    int BusinessDaysBetween(string calendarCode, DateOnly from, DateOnly to);
}

public sealed class BusinessCalendar(IServiceProvider services, IMemoryCache cache) : IBusinessCalendar
{
    private const string CacheKeyPrefix = "holidays:";
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(12);

    public bool IsBusinessDay(string calendarCode, DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) &&
        !Holidays(calendarCode).Contains(date);

    public int BusinessDaysBetween(string calendarCode, DateOnly from, DateOnly to)
    {
        if (from == to) return 0;
        var sign = to > from ? 1 : -1;
        var (start, end) = to > from ? (from, to) : (to, from);

        var holidays = Holidays(calendarCode);
        var count = 0;
        for (var d = start.AddDays(1); d <= end; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holidays.Contains(d))
                count++;

        return count * sign;
    }

    private HashSet<DateOnly> Holidays(string calendarCode) =>
        cache.GetOrCreate(CacheKeyPrefix + calendarCode, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoeDbContext>();
            return db.Holidays
                .AsNoTracking()
                .Where(h => h.CalendarCode == calendarCode)
                .Select(h => h.HolidayDate)
                .ToHashSet();
        })!;
}
