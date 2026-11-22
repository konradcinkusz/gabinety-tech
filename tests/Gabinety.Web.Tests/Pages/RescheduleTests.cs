using System.Globalization;
using System.Net;
using Gabinety.Web.Api;
using Gabinety.Web.Text;
using static Gabinety.Web.Tests.Pages.ClientAreaFixture;

namespace Gabinety.Web.Tests.Pages;

/// <summary>The move of a confirmed booking to another time (T-11.11): the entry on the detail, the picker of the same offer as form buttons, the refusals in words, and the forbidden page.</summary>
public class RescheduleTests
{
    static readonly string Detail_ = $"/moje/rezerwacje/{BookingId}";
    static readonly string Client = Detail_ + "?role=client";
    static readonly string Move = $"/moje/rezerwacje/{BookingId}/zmien-termin";

    // Two free times a few days ahead of the real clock, so the picker's windows (from today) and its horizon always offer them.
    static readonly DateTimeOffset Soon = new(DateTime.UtcNow.Date.AddDays(3).AddHours(10), TimeSpan.Zero);
    static readonly DateTimeOffset Later = Soon.AddHours(5);

    static string Utc(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>The fake Booking: slots for the picker, the detail for every read, <paramref name="move"/> for the posted move.</summary>
    static async Task<ClientAreaFixture> Open(BookingDetailDto? booking = null, Func<HttpRequestMessage, HttpResponseMessage>? move = null, bool slotsDown = false)
    {
        var f = await ClientAreaFixture.SignedIn();
        var detail = booking ?? Detail(reschedule: true);
        f.Web.Booking.Respond = r =>
        {
            if (r.Method == HttpMethod.Post) return (move ?? (_ => FakeBackend.Json(detail)))(r);
            if (r.RequestUri!.AbsolutePath.EndsWith("/slots", StringComparison.Ordinal))
                return slotsDown ? FakeBackend.Problem(HttpStatusCode.ServiceUnavailable, "dependency_unavailable") : FakeBackend.Json(new SlotsDto([new SlotDto(Soon, Soon.AddHours(1)), new SlotDto(Later, Later.AddHours(1))]));
            return FakeBackend.Json(detail);
        };
        return f;
    }

    // ---- the entry

    [Fact]
    public async Task The_detail_offers_the_move_only_while_the_service_says_canReschedule()
    {
        await using var on = await Open(Detail(reschedule: true));
        var link = (await on.Browser.GetAsync(Client)).One($"a[href='{Move}']");
        Assert.NotNull(link);
        Assert.Equal("Zmień termin", link.TextContent);
        Assert.Equal("btn btn--ghost", link.ClassName);

        await using var off = await Open(Detail(reschedule: false));
        Assert.Empty((await off.Browser.GetAsync(Client)).All("a[href$='/zmien-termin']"));
    }

    [Fact]
    public async Task A_completed_booking_has_no_entry_even_when_the_page_is_asked_for_directly()
    {
        await using var f = await Open(Detail("Completed", reschedule: false));
        Assert.Empty((await f.Browser.GetAsync(Client)).All("a[href$='/zmien-termin']"));
        var page = await f.Browser.GetAsync(Move);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.NotNull(page.One("[data-testid=reschedule-refused]"));
    }

    // ---- the page

    [Fact]
    public async Task The_page_shows_the_booking_and_the_free_times_of_its_own_offer_as_form_buttons()
    {
        await using var f = await Open();
        var page = await f.Browser.GetAsync(Move);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(["Zmień termin"], page.All("h1").Select(h => h.TextContent));
        Assert.Equal("noindex,nofollow", page.One("meta[name=robots]")!.GetAttribute("content"));
        Assert.Equal("Audyt kodu demo", page.TextOf("#booking-title"));
        Assert.Contains("Zmiana nie zmienia płatności ani zasad zwrotu.", page.TextOf("[data-testid=reschedule-policy]"));

        // every time is a submit button of the page's form, carrying the instant the service reads; none is a link to the booking flow
        var buttons = page.All("form[method=post] button[name='Model.StartUtc']");
        Assert.Equal([Utc(Soon), Utc(Later)], buttons.Select(b => b.GetAttribute("value")));
        Assert.Empty(page.All("a[href*='/rezerwacja/']"));
        Assert.Single(page.All("form[method=post] input[name=_handler][value='reschedule']"));
    }

    [Fact]
    public async Task A_visitor_is_sent_to_sign_in_before_the_move_page()
    {
        await using var visitor = await ClientAreaFixture.Visitor();
        var location = (await visitor.Browser.GetAsync(Move)).Location;
        Assert.NotNull(location);
        Assert.StartsWith("/logowanie", location);
        Assert.Contains("zmien-termin", location);
    }

    [Fact]
    public async Task A_chosen_time_posts_the_move_and_the_detail_shows_the_flash()
    {
        await using var f = await Open(move: _ => FakeBackend.Json(Detail(reschedule: false)));
        var page = await f.Post(Move, "reschedule", ("Model.StartUtc", Utc(Later)));
        Assert.Equal($"{Client}&done=rescheduled", page.Location);

        var post = f.Web.Booking.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal($"/api/v1/bookings/{BookingId}/reschedule", post.PathAndQuery);
        Assert.Contains($"\"startUtc\":\"{Utc(Later)}\"", post.Body);

        var done = await f.Browser.GetAsync(page.Location!);
        Assert.Equal("Termin został zmieniony.", done.One("[data-testid=done]")!.TextContent);
    }

    [Fact]
    public async Task A_value_that_is_not_an_instant_is_refused_here_and_nothing_is_sent()
    {
        await using var f = await Open();
        var page = await f.Post(Move, "reschedule", ("Model.StartUtc", "2026-12-08 10:00"));
        Assert.Equal("Wybierz termin z listy.", page.TextOf("[data-testid=reschedule-error] p"));
        Assert.DoesNotContain(f.Web.Booking.Requests, r => r.Method == HttpMethod.Post);
    }

    // ---- refusals, each in its own words, at the place of the choice

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "reschedule_not_allowed", "Tej rezerwacji nie można już przełożyć: zmiana terminu jest możliwa tylko raz i najpóźniej 24 godziny przed rozpoczęciem konsultacji.")]
    [InlineData(HttpStatusCode.Conflict, "slot_taken", "Ten termin został właśnie zajęty. Wybierz inny.")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "slot_unavailable", "Tego terminu nie ma już w ofercie. Wybierz inny.")]
    [InlineData(HttpStatusCode.Conflict, "invalid_transition", "To nie jest już możliwe: stan rezerwacji się zmienił. Odśwież stronę.")]
    public async Task A_refused_move_is_named_in_words_at_the_place_of_the_choice(HttpStatusCode status, string code, string text)
    {
        await using var f = await Open(move: _ => FakeBackend.Problem(status, code));
        var page = await f.Post(Move, "reschedule", ("Model.StartUtc", Utc(Later)));
        var alert = page.One("[data-testid=reschedule-error]")!;
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Equal(text, alert.QuerySelector("p")!.TextContent);
        Assert.DoesNotContain("SERVER-TITLE-MUST-NOT-APPEAR", page.Html);
        Assert.DoesNotContain(code, page.Text); // the code is never the words
        Assert.Equal(code == "invalid_transition", alert.QuerySelector("a") is not null); // only a changed state offers to read it again
        Assert.NotNull(page.One("form[method=post] button[name='Model.StartUtc']")); // the picker stays for another choice
    }

    [Fact]
    public async Task A_booking_inside_the_window_has_no_move_and_the_page_answers_with_the_refusal()
    {
        await using var f = await Open(Detail(reschedule: false));
        var page = await f.Browser.GetAsync(Move);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(BookingsText.Reschedule.Refused, page.TextOf("[data-testid=reschedule-refused] p"));
        Assert.Empty(page.All("form[method=post]"));
        Assert.Empty(page.All("button[name='Model.StartUtc']"));
        Assert.DoesNotContain(f.Web.Booking.Requests, r => r.PathAndQuery.Contains("/slots", StringComparison.Ordinal)); // no free time is read for a refused move
    }

    [Fact]
    public async Task A_refused_move_after_the_window_closed_says_so_on_the_same_page()
    {
        await using var f = await Open(move: _ => FakeBackend.Problem(HttpStatusCode.UnprocessableEntity, "reschedule_not_allowed"), booking: Detail(reschedule: true));
        var page = await f.Post(Move, "reschedule", ("Model.StartUtc", Utc(Later)));
        Assert.Equal(BookingsText.Reschedule.Refused, page.TextOf("[data-testid=reschedule-error] p"));
    }

    [Fact]
    public async Task A_picker_that_cannot_read_the_free_times_says_so_and_offers_no_button()
    {
        await using var f = await Open(slotsDown: true);
        var page = await f.Browser.GetAsync(Move);
        Assert.NotNull(page.One("[data-testid=slots-unavailable]"));
        Assert.Empty(page.All("button[name='Model.StartUtc']"));
    }

    [Fact]
    public async Task An_offer_the_profile_does_not_name_clearly_is_not_guessed()
    {
        await using var f = await Open(Detail(reschedule: true) with { OfferingTitle = "Inna oferta" });
        var page = await f.Browser.GetAsync(Move);
        Assert.Equal(BookingsText.Reschedule.NoOffering, page.TextOf("[data-testid=reschedule-no-offering] p"));
        Assert.Empty(page.All("form[method=post]"));
    }

    // ---- not the person's booking

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "forbidden")]
    [InlineData(HttpStatusCode.NotFound, "not_found")]
    public async Task Somebody_elses_booking_is_the_forbidden_page_and_nothing_of_it_is_shown(HttpStatusCode status, string code)
    {
        await using var f = await Open();
        f.Web.Booking.Respond = _ => FakeBackend.Problem(status, code);
        var page = await f.Browser.GetAsync(Move);
        Assert.Equal(HttpStatusCode.Forbidden, page.Status);
        Assert.NotNull(page.One("[data-testid=forbidden]"));
        Assert.Empty(page.All("form[method=post]"));
        Assert.DoesNotContain("Audyt kodu demo", page.Text);
    }

    [Fact]
    public async Task A_booking_that_is_a_bad_guid_is_not_a_route()
    {
        await using var f = await Open();
        Assert.Equal(HttpStatusCode.NotFound, (await f.Browser.GetAsync($"/moje/rezerwacje/nie-guid/zmien-termin")).Status);
    }
}
