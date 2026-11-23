using Bunit;
using Gabinety.Web.Components.Pages.Booking;
using Gabinety.Web.Components.Pages.Bookings;
using Gabinety.Web.Tests.Pages;
using static Gabinety.Web.Tests.Pages.ClientAreaFixture;

namespace Gabinety.Web.Tests.Rendering;

/// <summary>UX-020: the step list, the change-time link and the free-cancellation line.</summary>
public class BookingStepsTests : BunitContext
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void One_step_is_current_the_earlier_ones_are_marked_done_for_assistive_technology(int current)
    {
        var cut = Render<BookingSteps>(p => p.Add(c => c.Current, current));
        Assert.Equal("Kroki rezerwacji", cut.Find("nav").GetAttribute("aria-label"));
        var items = cut.FindAll("nav > ol > li");
        Assert.Equal(["Termin", "Opis", "Płatność", "Gotowe"], items.Select(li => li.QuerySelector(".booking-steps__name")!.FirstChild!.TextContent));
        var currentItems = cut.FindAll("[aria-current]");
        Assert.Equal("step", Assert.Single(currentItems).GetAttribute("aria-current"));
        Assert.Equal(items[current].InnerHtml, currentItems[0].InnerHtml);
        var done = items.Where(li => li.TextContent.Contains("(ukończony)")).ToList();
        Assert.Equal(current, done.Count);
        Assert.All(done, li => Assert.NotNull(li.QuerySelector(".sr-only")));
        Assert.All(items.Take(current), li => Assert.Contains("booking-steps__item--done", li.ClassName));
    }

    [Fact]
    public void The_form_is_on_the_second_step_called_Opis() =>
        Assert.Contains("Opis", Render<BookingSteps>(p => p.Add(c => c.Current, 1)).Find("[aria-current=step]").TextContent);

    [Fact]
    public void The_summary_links_back_to_the_offer_in_the_picker_only_when_told_where()
    {
        var with = Render<ConsultationSummary>(p => p.Add(c => c.Offering, "Audyt").Add(c => c.When, "x").Add(c => c.PriceMinor, 100).Add(c => c.ChangeTimeHref, "/gabinet/demo-anna#oferta-abc"));
        var link = with.Find("a.btn.btn--ghost");
        Assert.Equal("Zmień termin", link.TextContent);
        Assert.Equal("/gabinet/demo-anna#oferta-abc", link.GetAttribute("href"));
        Assert.Empty(Render<ConsultationSummary>(p => p.Add(c => c.Offering, "Audyt").Add(c => c.When, "x").Add(c => c.PriceMinor, 100)).FindAll("a"));
    }
}

public class FreeCancellationTests
{
    static readonly DateTimeOffset Now = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_deadline_is_the_start_minus_the_window_written_in_warsaw_time() =>
        Assert.Equal("Bezpłatne anulowanie do poniedziałek, 7 grudnia, 12:00.", FreeCancellation.Line(new DateTimeOffset(2026, 12, 8, 11, 0, 0, TimeSpan.Zero), Now));

    [Fact]
    public void Inside_the_window_there_is_no_free_cancellation_and_the_line_says_so()
    {
        Assert.Null(FreeCancellation.Until(Now.AddHours(23), Now));
        Assert.Equal("Do konsultacji zostało mniej niż 24 godz.: rezygnacja nie da już zwrotu.", FreeCancellation.Line(Now.AddHours(23), Now));
        Assert.NotNull(FreeCancellation.Until(Now.AddHours(25), Now));
    }

    /// <summary>The web host cannot reference Billing; the test project can, so a change of the owner's constant fails here.</summary>
    [Fact]
    public void The_mirrored_window_equals_the_billing_constant() => Assert.Equal(Gabinety.Billing.RefundPolicy.FullRefundWindow, FreeCancellation.Window);
}

public class BookingFormUx020Tests
{
    [Fact]
    public async Task The_form_shows_the_steps_the_change_link_and_the_free_cancellation_line_with_the_hold_notice()
    {
        await using var f = await ClientAreaFixture.SignedIn();
        // 2099-12-07 is a Monday; a far-future start keeps the line independent of the real clock the host runs on.
        var page = await f.Browser.GetAsync(BookingUrl.Replace("2026-12-08", "2099-12-08"));
        Assert.Equal("Opis", page.One("nav[aria-label='Kroki rezerwacji'] [aria-current=step] .booking-steps__name")!.TextContent.Trim());
        var link = page.One(".booking-summary a.btn--ghost")!;
        Assert.Equal("Zmień termin", link.TextContent);
        Assert.Equal($"/gabinet/demo-anna-demowska#oferta-{OfferingId}", link.GetAttribute("href"));
        var notice = page.One(".booking-summary .callout--info")!.TextContent;
        Assert.Contains("Termin jest wstrzymany na 10 minut", notice);
        Assert.Contains("Bezpłatne anulowanie do poniedziałek, 7 grudnia, 12:00.", notice);
    }
}
