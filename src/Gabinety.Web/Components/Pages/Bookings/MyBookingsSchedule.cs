using System.Globalization;
using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Shared;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Bookings;

/// <summary>
/// The two sections of the client's list and the words for the start of the next consultation (UX-030). Pure: the clock is passed in, so the split and the wording are unit-tested
/// without a page. Times are Europe/Warsaw (CONTRACTS 0), the same zone <see cref="BookingFormat"/> uses.
/// </summary>
public static class MyBookingsSchedule
{
    /// <summary>Statuses that end a booking before its start, so it is never "upcoming" whatever its start time says.</summary>
    static readonly string[] Ended = ["CancelledByClient", "LateCancelledByClient", "CancelledByPractitioner", "Expired"];

    public static bool IsUpcoming(BookingViewDto booking, DateTimeOffset now) => booking.StartUtc > now && !Ended.Contains(booking.Status);

    /// <summary>Upcoming: the soonest first. Everything else: the newest first.</summary>
    public static (List<BookingViewDto> Upcoming, List<BookingViewDto> Past) Split(IEnumerable<BookingViewDto> items, DateTimeOffset now)
    {
        var list = items.ToList();
        return (list.Where(b => IsUpcoming(b, now)).OrderBy(b => b.StartUtc).ToList(),
            list.Where(b => !IsUpcoming(b, now)).OrderByDescending(b => b.StartUtc).ToList());
    }

    /// <summary>
    /// <c>dziś, 09:30</c> today, <c>jutro, 09:30</c> tomorrow, <c>za 3 dni, 09:30</c> within the week, otherwise the long date <c>wtorek, 1 grudnia, 09:30</c>.
    /// The day is counted in the Warsaw calendar, not as 24-hour spans, so a time change does not move a booking to another day.
    /// </summary>
    public static string Relative(DateTimeOffset now, DateTimeOffset startUtc)
    {
        var start = TimeZoneInfo.ConvertTime(startUtc, BookingFormat.Warsaw);
        var day = DateOnly.FromDateTime(start.DateTime);
        var days = day.DayNumber - PlFormat.TodayWarsaw(now).DayNumber;
        var time = start.ToString("HH:mm", CultureInfo.InvariantCulture);
        return days switch
        {
            0 => $"{Pl.Bookings.Today}, {time}",
            1 => $"{Pl.Bookings.Tomorrow}, {time}",
            >= 2 and <= 6 => $"{string.Format(CultureInfo.InvariantCulture, Pl.Bookings.InDays, days)}, {time}",
            _ => $"{PlFormat.DayHeading(day)}, {time}",
        };
    }
}
