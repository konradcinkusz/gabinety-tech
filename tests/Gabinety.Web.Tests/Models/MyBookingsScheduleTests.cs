using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Bookings;

namespace Gabinety.Web.Tests.Models;

/// <summary>The plain C# of the list's sections and of the start in words (UX-030): no renderer, no clock but the one passed in.</summary>
public class MyBookingsScheduleTests
{
    // 22 November 2026 at 11:00 in Warsaw (10:00 UTC). In November Warsaw is UTC+1.
    static readonly DateTimeOffset Now = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    static DateTimeOffset Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    static BookingViewDto At(DateTimeOffset start, string status = "Confirmed", string title = "x") =>
        Sample.Booking(title, status) with { StartUtc = start, EndUtc = start.AddHours(1) };

    [Theory]
    [InlineData("2026-11-22T08:30:00Z", "dziś, 09:30")]
    [InlineData("2026-11-23T08:30:00Z", "jutro, 09:30")]
    [InlineData("2026-11-25T08:00:00Z", "za 3 dni, 09:00")]
    [InlineData("2026-11-28T08:00:00Z", "za 6 dni, 09:00")]
    [InlineData("2026-11-29T08:00:00Z", "niedziela, 29 listopada, 09:00")] // a week or more: the long date
    [InlineData("2026-12-01T12:00:00Z", "wtorek, 1 grudnia, 13:00")]
    public void The_start_is_worded_from_the_warsaw_calendar_day(string start, string expected) =>
        Assert.Equal(expected, MyBookingsSchedule.Relative(Now, DateTimeOffset.Parse(start, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void The_day_is_the_warsaw_day_not_the_utc_day_near_midnight()
    {
        // 23:30 UTC on 22 November is 00:30 on 23 November in Warsaw: a booking at 01:15 Warsaw on 23 November is "dziś" of that moment, not "jutro".
        var lateNow = Utc(11, 22, 23, 30);
        Assert.Equal("dziś, 01:15", MyBookingsSchedule.Relative(lateNow, Utc(11, 23, 0, 15)));
        Assert.Equal("jutro, 01:15", MyBookingsSchedule.Relative(Now, Utc(11, 23, 0, 15))); // from 11:00 on the 22nd it is tomorrow
    }

    [Fact]
    public void The_upcoming_are_the_future_ones_that_were_not_cancelled_or_expired_soonest_first()
    {
        var items = new[]
        {
            At(Utc(12, 5, 12), title: "Dalsza"),
            At(Utc(11, 30, 12), "PendingPayment", "Przed płatnością"),
            At(Utc(12, 2, 12), "CancelledByClient", "Anulowana"),
            At(Utc(12, 3, 12), "CancelledByPractitioner", "Odwołana"),
            At(Utc(12, 4, 12), "LateCancelledByClient", "Późno odwołana"),
            At(Utc(12, 6, 12), "Expired", "Wygasła"),
            At(Utc(11, 10, 12), "Completed", "Zakończona"),
        };
        var (upcoming, past) = MyBookingsSchedule.Split(items, Now);
        Assert.Equal(["Przed płatnością", "Dalsza"], upcoming.Select(b => b.OfferingTitle));
        Assert.Equal(["Wygasła", "Późno odwołana", "Odwołana", "Anulowana", "Zakończona"], past.Select(b => b.OfferingTitle));
    }

    [Theory]
    [InlineData("Completed")] [InlineData("NoShow")] [InlineData("Disputed")] [InlineData("DisputeResolved")] [InlineData("CancelledByClientNoRefund")]
    public void Only_a_booking_awaiting_payment_or_confirmed_is_upcoming_whatever_its_start_says(string status)
    {
        // seeded data made with a moved test clock has a completed booking that starts in the future of the real clock
        var (upcoming, past) = MyBookingsSchedule.Split([At(Now.AddDays(2), status, "Z przyszłością")], Now);
        Assert.Empty(upcoming);
        Assert.Single(past);
    }

    [Fact]
    public void A_booking_starting_exactly_now_is_past_and_nothing_is_lost_in_the_split()
    {
        var items = new[] { At(Now, title: "Teraz"), At(Now.AddMinutes(1), title: "Za minutę") };
        var (upcoming, past) = MyBookingsSchedule.Split(items, Now);
        Assert.Equal(["Za minutę"], upcoming.Select(b => b.OfferingTitle));
        Assert.Equal(["Teraz"], past.Select(b => b.OfferingTitle));
        Assert.Empty(MyBookingsSchedule.Split([], Now).Upcoming);
    }
}
