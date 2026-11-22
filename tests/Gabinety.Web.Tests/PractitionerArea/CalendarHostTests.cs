using System.Net;
using Gabinety.Web.Api;

namespace Gabinety.Web.Tests.PractitionerAreaTests;

/// <summary>
/// The practitioner's week view (<c>/gabinet-programisty/kalendarz</c>, DESIGN-CONTRACT 5.9, 5.10): the Warsaw week, the states that are drawn, the empty, failure and forbidden states,
/// and every page of the bookings. Through the real requests of the fake services.
/// </summary>
public class CalendarHostTests
{
    /// <summary>Warsaw time with its own offset: 25 October 2026 is the day clocks go back (UTC+2 before, UTC+1 after), and November is UTC+1.</summary>
    static DateTimeOffset Warsaw(int year, int month, int day, int hour, int minute, int offsetHours = 1) => new(year, month, day, hour, minute, 0, TimeSpan.FromHours(offsetHours));

    static BookingViewDto Booking(DateTimeOffset start, string status = "Confirmed", string? client = "Anna Nowak", int minutes = 60) =>
        new(Guid.NewGuid(), status, start, start.AddMinutes(minutes), minutes, "Przegląd testów", new MoneyDto(18000, "PLN"),
            new PractitionerRefDto(Guid.NewGuid(), "Demo Programista", "demo-programista"), client is null ? null : new ClientRefDto(client));

    [Fact]
    public async Task A_booking_on_a_monday_in_warsaw_is_in_the_monday_column_across_the_autumn_change_of_time()
    {
        // 26 October 2026, 00:30 in Warsaw is 25 October 23:30 UTC: a Sunday in UTC, a Monday in the calendar. The Sunday evening before is the week before.
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w =>
        {
            w.HaveProfile();
            w.Bookings.Add(Booking(Warsaw(2026, 10, 26, 0, 30)));
            w.Bookings.Add(Booking(Warsaw(2026, 10, 26, 9, 0), client: "Ewa Kowalska"));
            w.Bookings.Add(Booking(Warsaw(2026, 10, 25, 23, 45)));
        });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/kalendarz?tydzien=2026-10-26");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        var monday = page.One("li.week__day[data-day='2026-10-26']")!;
        Assert.Equal(["00:30–01:30", "09:00–10:00"], monday.QuerySelectorAll("[data-testid=calendar-booking] .week__time").Select(t => t.TextContent));
        Assert.Equal(2, page.All("[data-testid=calendar-booking]").Count); // the Sunday evening is not in this week
        Assert.Contains("from=2026-10-26&to=2026-11-01", web.Booking.Requests.Single().PathAndQuery);
    }

    [Fact]
    public async Task The_week_is_the_monday_of_the_day_asked_for_with_seven_days_and_links_a_week_back_and_forward()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w =>
        {
            w.HaveProfile();
            w.Bookings.Add(Booking(Warsaw(2026, 11, 25, 10, 0))); // a week with a consultation: the seven columns (an empty week is the empty state, tested below)
        });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/kalendarz?tydzien=2026-11-26");
        Assert.Equal("Kalendarz", page.TextOf("h1"));
        Assert.Single(page.All("h1"));
        Assert.Equal("23–29 listopada 2026", page.TextOf("[data-testid=week-range]"));
        Assert.Equal(["2026-11-23", "2026-11-24", "2026-11-25", "2026-11-26", "2026-11-27", "2026-11-28", "2026-11-29"], page.All("li.week__day").Select(d => d.GetAttribute("data-day")));
        Assert.Equal("Kalendarz", page.TextOf("nav.tabs a[aria-current=page]"));
        Assert.Equal("/gabinet-programisty/kalendarz?tydzien=2026-11-16", page.One("a[rel=prev]")!.GetAttribute("href"));
        Assert.Equal("/gabinet-programisty/kalendarz?tydzien=2026-11-30", page.One("a[rel=next]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task Only_the_states_a_practitioner_plans_around_are_drawn_each_as_a_card_with_its_badge_and_a_details_link()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w =>
        {
            w.HaveProfile();
            w.Bookings.Add(Booking(Warsaw(2026, 11, 23, 9, 0), "Confirmed"));
            w.Bookings.Add(Booking(Warsaw(2026, 11, 23, 10, 0), "Completed", client: null));
            w.Bookings.Add(Booking(Warsaw(2026, 11, 23, 11, 0), "Disputed"));
            w.Bookings.Add(Booking(Warsaw(2026, 11, 23, 12, 0), "ClientNoShow"));
            foreach (var (hour, status) in new[] { (13, "PendingPayment"), (14, "CancelledByClient"), (15, "LateCancelledByClient"), (16, "Expired"), (17, "CancelledByPractitioner"), (18, "Resolved") })
                w.Bookings.Add(Booking(Warsaw(2026, 11, 23, hour, 0), status));
        });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/kalendarz?tydzien=2026-11-23");
        var cards = page.All("[data-testid=calendar-booking]");
        Assert.Equal(["Potwierdzona", "Zakończona", "Spór w toku", "Klient nie stawił się"], cards.Select(c => c.QuerySelector(".badge")!.TextContent));
        Assert.Equal(world.Bookings.Take(4).Select(b => $"/moje/rezerwacje/{b.Id}?role=practitioner"), cards.Select(c => c.QuerySelector("a")!.GetAttribute("href")));
        Assert.Contains("Anna Nowak", cards[0].TextContent);
        Assert.Contains("Klient", cards[1].TextContent); // no name from the service: the neutral party label
        Assert.Equal("Szczegóły", cards[0].QuerySelector("a")!.TextContent);
    }

    [Fact]
    public async Task A_week_without_a_drawn_consultation_is_the_empty_state_and_the_week_links_still_work()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w =>
        {
            w.HaveProfile();
            w.Bookings.Add(Booking(Warsaw(2026, 11, 24, 10, 0), "PendingPayment"));
        });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/kalendarz?tydzien=2026-11-23");
        Assert.Equal("Brak konsultacji w tym tygodniu", page.TextOf("[data-testid=calendar-empty] h2"));
        Assert.Empty(page.All("ol.week"));
        Assert.NotNull(page.One("a[rel=next]"));
    }

    [Fact]
    public async Task An_account_without_a_practitioner_profile_gets_the_forbidden_notice_with_403_and_no_booking_is_asked_for()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn();
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/kalendarz");
        Assert.Equal(HttpStatusCode.Forbidden, page.Status);
        Assert.Equal("Brak dostępu", page.TextOf("[data-testid=forbidden] h2"));
        Assert.DoesNotContain(web.Booking.Requests, r => r.PathAndQuery.StartsWith("/api/v1/me/bookings"));
    }

    [Fact]
    public async Task A_booking_service_that_is_down_is_a_soft_callout_with_a_retry_and_the_week_links_still_render()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w =>
        {
            w.HaveProfile();
            w.BookingsFail = true;
        });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/kalendarz?tydzien=2026-11-23");
        Assert.Equal(HttpStatusCode.OK, page.Status); // the failure is a callout in the content, not an error page
        Assert.Equal("Ta część serwisu jest chwilowo niedostępna. Spróbuj ponownie za chwilę.", page.TextOf("[data-testid=unavailable] p"));
        Assert.Equal("/gabinet-programisty/kalendarz?tydzien=2026-11-23", page.One("[data-testid=unavailable] a")!.GetAttribute("href"));
        Assert.NotNull(page.One("a[rel=next]"));
    }

    [Fact]
    public async Task Every_page_of_the_bookings_is_read_so_no_consultation_of_the_week_is_missing()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w =>
        {
            w.HaveProfile();
            w.BookingPageSize = 2;
            for (var day = 23; day <= 27; day++) w.Bookings.Add(Booking(Warsaw(2026, 11, day, 10, 0)));
        });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/kalendarz?tydzien=2026-11-23");
        Assert.Equal(5, page.All("[data-testid=calendar-booking]").Count);
        var calls = web.Booking.Requests.Where(r => r.PathAndQuery.StartsWith("/api/v1/me/bookings")).Select(r => r.PathAndQuery).ToList();
        Assert.Equal(3, calls.Count); // pages of 2, 2 and 1
        Assert.Contains(calls, c => c.EndsWith("&cursor=4"));
    }

    [Fact]
    public async Task A_week_that_is_not_a_date_shows_the_current_week_and_does_not_fail()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty/kalendarz?tydzien=nie-data");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.StartsWith("/gabinet-programisty/kalendarz?tydzien=", page.One("a[rel=prev]")!.GetAttribute("href"));
        Assert.Single(page.All("[data-testid=calendar-empty]"));
    }
}
