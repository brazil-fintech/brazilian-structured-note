using System.Collections.Concurrent;
using Coe.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Coe.Infrastructure;

/// <summary>
/// The national business-day calendar behind every date rule.
///
/// Lookups are synchronous by design: they run inside the validation engine, which is pure CPU
/// and called on every keystroke. The I/O is hoisted out to <see cref="EnsureLoadedAsync"/>,
/// which the booking service awaits once before validating. A holiday table is a few hundred
/// rows that change once a year, so it is cached for the process.
/// </summary>
public interface IBusinessCalendar
{
    /// <summary>Loads and caches the calendar. Await this before entering a synchronous validation pass.</summary>
    ValueTask EnsureLoadedAsync(string calendarCode, CancellationToken ct = default);

    bool IsBusinessDay(string calendarCode, DateOnly date);

    /// <summary>Business days strictly after <paramref name="from"/> through <paramref name="to"/>; negative when reversed.</summary>
    int BusinessDaysBetween(string calendarCode, DateOnly from, DateOnly to);
}

public sealed class BusinessCalendar(
    ISqlConnectionFactory connections,
    SqlConnectionOptions options,
    ILogger<BusinessCalendar> logger) : IBusinessCalendar
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(12);

    private readonly SqlRetryPolicy _retry = new(options.MaxRetries, logger);
    private readonly ConcurrentDictionary<string, CachedCalendar> _calendars = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private sealed record CachedCalendar(HashSet<DateOnly> Holidays, DateTimeOffset LoadedUtc)
    {
        public bool IsFresh => DateTimeOffset.UtcNow - LoadedUtc < CacheFor;
    }

    public async ValueTask EnsureLoadedAsync(string calendarCode, CancellationToken ct = default)
    {
        if (_calendars.TryGetValue(calendarCode, out var cached) && cached.IsFresh) return;

        // One loader at a time: on a cold start every in-flight request wants the same calendar,
        // and letting them all query would turn a cache miss into a thundering herd.
        await _loadLock.WaitAsync(ct);
        try
        {
            if (_calendars.TryGetValue(calendarCode, out cached) && cached.IsFresh) return;

            var holidays = await LoadAsync(calendarCode, ct);
            _calendars[calendarCode] = new CachedCalendar(holidays, DateTimeOffset.UtcNow);
            logger.LogInformation("Loaded {Count} holiday(s) for calendar {Calendar}", holidays.Count, calendarCode);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private Task<HashSet<DateOnly>> LoadAsync(string calendarCode, CancellationToken ct) =>
        _retry.ExecuteAsync("calendar.load", async token =>
        {
            await using var connection = await connections.OpenAsync(token);
            await using var command = new SqlCommand(
                "SELECT HolidayDate FROM ref.Holiday WHERE CalendarCode = @calendar", connection);
            command.NVarChar("@calendar", calendarCode, 20);

            await using var reader = await command.ExecuteReaderAsync(token);
            var holidays = new HashSet<DateOnly>();
            while (await reader.ReadAsync(token)) holidays.Add(reader.GetDateOnly(0));
            return holidays;
        }, ct);

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

    /// <summary>
    /// Weekends still apply when the table has not been loaded, but holidays cannot be known.
    /// Silently treating them as business days would pass a booking the server would later
    /// reject, so callers are expected to have awaited <see cref="EnsureLoadedAsync"/>.
    /// </summary>
    private HashSet<DateOnly> Holidays(string calendarCode)
    {
        if (_calendars.TryGetValue(calendarCode, out var cached)) return cached.Holidays;

        logger.LogWarning("Calendar {Calendar} was queried before it was loaded; holidays are not being applied", calendarCode);
        return [];
    }
}
