using System.Net;
using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.PractitionerArea;

namespace Gabinety.Web.Tests.PractitionerAreaTests;

/// <summary>The earnings card of the approved dashboard (T-11.17), through the real host with the stateful fake of the services. The figures are the fake's, formatted as money.</summary>
public class EarningsHostTests
{
    const string Nbsp = " ";

    static IEnumerable<string> Requests(WebFactory web) => web.Booking.Requests.Select(r => r.PathAndQuery).Where(p => p.StartsWith("/api/v1/me/earnings", StringComparison.Ordinal));

    [Fact]
    public async Task The_approved_dashboard_shows_the_four_figures_of_the_month_as_money_and_the_count()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.CompleteProfile(); });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty");
        var card = page.One("[data-testid=earnings]")!;
        Assert.Equal("Zarobki w tym miesiącu", card.QuerySelector("h2")!.TextContent);
        Assert.Equal($"1{Nbsp}234,56{Nbsp}zł", page.TextOf("[data-testid=earnings-paid-out]"));
        Assert.Equal($"49,50{Nbsp}zł", page.TextOf("[data-testid=earnings-pending]"));
        Assert.Equal($"180,00{Nbsp}zł", page.TextOf("[data-testid=earnings-disputed]"));
        Assert.Equal($"37,50{Nbsp}zł", page.TextOf("[data-testid=earnings-fees]"));
        Assert.Contains("Rezerwacje w okresie: 7", card.TextContent);
        Assert.Equal(["Wypłacone", "Oczekujące na wypłatę", "W sporze", "Opłaty platformy"], page.All("[data-testid=earnings-figures] dt").Select(d => d.TextContent));
        Assert.Null(page.One("[data-testid=earnings-failed]"));
        Assert.Equal("/gabinet-programisty/kalendarz", card.QuerySelector("p:last-child a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task Without_a_month_the_service_is_asked_for_its_default_month_and_the_bearer_is_sent()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.CompleteProfile(); });
        await using var _ = web;
        await browser.GetAsync("/gabinet-programisty");
        Assert.Equal(["/api/v1/me/earnings"], Requests(web)); // no dates: the service answers the current Warsaw month
        Assert.StartsWith("Bearer ", web.Booking.Requests.Single(r => r.PathAndQuery.StartsWith("/api/v1/me/earnings", StringComparison.Ordinal)).Authorization);
    }

    [Fact]
    public async Task A_month_asked_for_is_sent_as_its_first_and_last_day_and_shown_with_its_period_and_the_neighbouring_months()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.CompleteProfile(); });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty?miesiac=2026-10");
        Assert.Equal(["/api/v1/me/earnings?from=2026-10-01&to=2026-10-31"], Requests(web));
        Assert.Equal("Okres: 1 października 2026 – 31 października 2026", page.TextOf("[data-testid=earnings-period]"));
        Assert.Equal("Zarobki", page.One("[data-testid=earnings] h2")!.TextContent);
        Assert.Equal("/gabinet-programisty?miesiac=2026-09", page.One("[data-testid=earnings] a[rel=prev]")!.GetAttribute("href"));
        Assert.Equal("/gabinet-programisty?miesiac=2026-11", page.One("[data-testid=earnings] a[rel=next]")!.GetAttribute("href"));
        Assert.Equal("/gabinet-programisty", page.One("[data-testid=earnings] nav a:not([rel])")!.GetAttribute("href"));
    }

    [Theory]
    [InlineData("2099-01")] [InlineData("2026-13")] [InlineData("nonsense")] [InlineData("2026-1")]
    public async Task A_month_that_is_not_one_the_card_can_show_falls_back_to_the_default_month(string asked)
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.CompleteProfile(); });
        await using var _ = web;
        var page = await browser.GetAsync($"/gabinet-programisty?miesiac={asked}");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(["/api/v1/me/earnings"], Requests(web));
        Assert.Equal("Zarobki w tym miesiącu", page.TextOf("[data-testid=earnings] h2"));
    }

    [Fact]
    public async Task A_failing_earnings_service_is_a_soft_callout_and_the_consultations_region_and_the_rest_of_the_page_still_work()
    {
        var (web, browser, world) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.CompleteProfile(); w.EarningsFail = true; });
        await using var _ = web;
        world.Bookings.Add(new(Guid.NewGuid(), "Confirmed", DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow.AddDays(2).AddHours(1), 60, "Audyt kodu", new MoneyDto(18000, "PLN"),
            new PractitionerRefDto(Guid.NewGuid(), "Demo Programista", "demo-programista"), new ClientRefDto("Demo Klient")));
        var page = await browser.GetAsync("/gabinet-programisty");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        var failed = page.One("[data-testid=earnings-failed]")!;
        Assert.Contains("callout--warning", failed.ClassName);
        Assert.Contains("Nie możemy teraz pokazać zarobków.", failed.TextContent);
        Assert.Null(page.One("[data-testid=earnings-figures]"));
        Assert.Equal("Panel programisty", page.TextOf("h1"));
        Assert.Single(page.All("[data-testid=upcoming] [data-booking]")); // the consultations still list
        Assert.NotNull(page.One("[data-testid=public-profile]"));
        Assert.DoesNotContain("SERVER-", page.Html);
    }

    [Fact]
    public async Task An_unreachable_booking_service_takes_the_card_and_the_consultations_down_and_not_the_page()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => { w.HaveProfile("Approved"); w.CompleteProfile(); w.BookingDown = true; });
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.NotNull(page.One("[data-testid=earnings-failed]"));
        Assert.NotNull(page.One("[data-testid=upcoming-failed]"));
        Assert.NotNull(page.One("[data-testid=public-profile]"));
    }

    [Fact]
    public async Task A_draft_does_not_ask_for_earnings_and_shows_no_card()
    {
        var (web, browser, _) = await PractitionerBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var page = await browser.GetAsync("/gabinet-programisty");
        Assert.Empty(Requests(web));
        Assert.Null(page.One("[data-testid=earnings]"));
    }
}
