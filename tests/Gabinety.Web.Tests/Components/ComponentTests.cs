using Bunit;
using Bunit.TestDoubles;
using Gabinety.Web.Api;
using Gabinety.Web.Auth;
using Gabinety.Web.Components.Layout;
using Gabinety.Web.Components.Pages.Bookings;
using Gabinety.Web.Components.Pages.Practitioners;
using Gabinety.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Web.Tests.Rendering;

/// <summary>Markup, test hooks, state transitions and error rendering with bUnit; the typed clients are wired to a fake handler (BLAZOR-WEB-APP section 13). The roots (render mode) are not visible here: RenderModeTests reads them from disk.</summary>
public class PresentationalComponentTests : BunitContext
{
    [Fact]
    public void A_card_shows_the_practitioner_with_the_hooks_the_journey_uses()
    {
        var cut = Render<PractitionerCard>(p => p.Add(c => c.Practitioner, Sample.Card(badges: ["email", "approved"], rating: new RatingDto(4.5, 12), isNew: false)));
        Assert.Equal("practitioner-card", cut.Find("article").GetAttribute("data-testid"));
        var link = cut.Find("h2 a");
        Assert.Equal(("Anna Demowska", "/gabinet/demo-anna-demowska"), (link.TextContent, link.GetAttribute("href")));
        Assert.Equal("Audyty kodu .NET i konsultacje architektury", cut.Find("p").TextContent);
        Assert.Equal(["Zweryfikowany", "★ 4,5 (12)"], cut.FindAll("li .badge").Select(b => b.TextContent)); // P-LIST-2: one badge, the checks are on the profile
        Assert.Contains("badge--success", cut.FindAll("li .badge")[0].ClassName);
        Assert.Equal("Ocena 4,5 (12)", cut.FindAll("li .badge")[1].GetAttribute("aria-label"));
        Assert.Equal("od 250,00 zł 30 min, 60 min", System.Text.RegularExpressions.Regex.Replace(cut.Find("p.cluster").TextContent.Replace('\u00A0', ' '), @"\s+", " "));
    }

    [Fact]
    public void A_new_practitioner_is_marked_new_and_a_rating_below_the_threshold_is_not_shown()
    {
        var cut = Render<PractitionerCard>(p => p.Add(c => c.Practitioner, Sample.Card(badges: [], rating: new RatingDto(5.0, 2), isNew: true)));
        Assert.Equal(["Nowy"], cut.FindAll("li .badge").Select(b => b.TextContent));
    }

    [Fact]
    public void Text_from_the_directory_is_text_not_markup()
    {
        var cut = Render<PractitionerCard>(p => p.Add(c => c.Practitioner, Sample.Card(name: "<b>Pogrubiony</b>", headline: "<img src=x onerror=alert(1)>")));
        Assert.Empty(cut.FindAll("b"));
        Assert.Empty(cut.FindAll("img"));
        Assert.Equal("<b>Pogrubiony</b>", cut.Find("h2 a").TextContent);
    }

    [Theory]
    [InlineData(AlertVariant.Danger, "alert", "callout--danger")]
    [InlineData(AlertVariant.Warning, "status", "callout--warning")]
    [InlineData(AlertVariant.Info, "status", "callout--info")]
    [InlineData(AlertVariant.Success, "status", "callout--success")]
    public void An_alert_announces_itself_only_when_it_is_an_error(AlertVariant variant, string role, string cssClass)
    {
        var cut = Render<Alert>(p => p.Add(a => a.Variant, variant).Add(a => a.TestId, "x").AddChildContent("Tekst"));
        var div = cut.Find("div");
        Assert.Equal(role, div.GetAttribute("role"));
        Assert.Contains(cssClass, div.ClassName);
        Assert.Equal("x", div.GetAttribute("data-testid"));
    }

    [Fact]
    public void The_filters_mark_the_current_choice_and_carry_the_other_filters_in_the_search_form()
    {
        var state = new ListState("audyt-kodu", Duration: 60, MaxPriceMinor: 30000, Q: "kod");
        var view = new ListView(state, Sample.Taxonomy, Sample.Sitemap(5, true, "/programisci/audyt-kodu"));
        var cut = Render<FilterNav>(p => p.Add(f => f.State, state).Add(f => f.Groups, view.Filters()));
        Assert.Equal("Filtry", cut.Find("nav").GetAttribute("aria-label"));
        Assert.Equal(["Problem", "Technologia", "Czas trwania", "Cena do"], cut.FindAll("h2").Select(h => h.TextContent));
        var current = cut.FindAll("a[aria-current=true]").Select(a => a.TextContent).ToList();
        Assert.Equal(["Audyt kodu", "Wszystkie", "60 min", "do 300,00 zł"], current);
        var form = cut.Find("form[role=search]");
        Assert.Equal(("get", "/programisci/audyt-kodu"), (form.GetAttribute("method"), form.GetAttribute("action")));
        Assert.Equal("60", form.QuerySelector("input[name=duration]")!.GetAttribute("value"));
        Assert.Equal("30000", form.QuerySelector("input[name=maxPriceMinor]")!.GetAttribute("value"));
        Assert.Equal("kod", form.QuerySelector("input[name=q]")!.GetAttribute("value"));
        Assert.Equal("Szukaj po imieniu lub nagłówku", form.QuerySelector("input[name=q]")!.GetAttribute("aria-label")); // an unlabelled control is a violation (SPEC 5 rule 14)
    }

    [Fact]
    public void Pagination_renders_only_the_links_it_has()
    {
        Assert.Empty(Render<Pagination>().FindAll("nav"));
        var next = Render<Pagination>(p => p.Add(x => x.NextHref, "/programisci?cursor=B"));
        Assert.Equal(["/programisci?cursor=B"], next.FindAll("a").Select(a => a.GetAttribute("href")));
        Assert.Equal("next", next.Find("a").GetAttribute("rel"));
        var both = Render<Pagination>(p => p.Add(x => x.NextHref, "/n").Add(x => x.PreviousHref, "/p"));
        Assert.Equal(["prev", "next"], both.FindAll("a").Select(a => a.GetAttribute("rel")));
        Assert.Equal("Paginacja", both.Find("nav").GetAttribute("aria-label"));
    }
}

public class LayoutTests : BunitContext
{
    public LayoutTests() => Services.AddSingleton<TimeProvider>(TestClock.Default());

    static RenderFragment Body(string html = "<h1>Treść</h1>") => b => b.AddMarkupContent(0, html);

    sealed class Thrower : ComponentBase { protected override void OnInitialized() => throw new InvalidOperationException("SECRET-USER-TEXT-IN-AN-EXCEPTION"); }

    [Fact]
    public void The_shell_has_a_skip_link_first_and_the_landmarks()
    {
        AddAuthorization();
        var cut = Render<MainLayout>(p => p.Add(l => l.Body, Body()));
        var first = cut.Find("body, div, a"); // the first element rendered is the skip link
        Assert.Equal("a", first.TagName.ToLowerInvariant());
        Assert.Equal(("#main", "Przejdź do treści", "skip-link"), (first.GetAttribute("href"), first.TextContent, first.ClassName));
        Assert.Equal("Menu główne", cut.Find("header nav").GetAttribute("aria-label"));
        var main = cut.Find("main#main");
        Assert.Equal("-1", main.GetAttribute("tabindex")); // the skip link's target can take focus
        Assert.Single(cut.FindAll("main"));
        Assert.Single(cut.FindAll("footer"));
        Assert.Single(cut.FindAll("header"));
        Assert.Equal("Treść", main.QuerySelector("h1")!.TextContent);
        Assert.Equal("© 2026 Gabinety Tech", cut.Find("footer p").TextContent);
        Assert.Equal("Gabinety Tech", cut.Find("a.brand").TextContent);
    }

    [Fact]
    public void The_navigation_order_is_pinned_for_a_visitor_and_for_a_member()
    {
        var auth = AddAuthorization();
        auth.SetNotAuthorized();
        Assert.Equal(["/programisci", "/logowanie"], Render<MainLayout>(p => p.Add(l => l.Body, Body())).FindAll("header nav a").Select(a => a.GetAttribute("href")));
        auth.SetAuthorized("demo-client@demo.example");
        Assert.Equal(["/programisci", "/moje/rezerwacje", "/wyloguj"], Render<MainLayout>(p => p.Add(l => l.Body, Body())).FindAll("header nav a").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public void The_flash_area_is_a_polite_live_region_that_shows_known_messages_only()
    {
        AddAuthorization();
        Assert.Equal("polite", Render<MainLayout>(p => p.Add(l => l.Body, Body())).Find("[data-testid=flash]").GetAttribute("aria-live"));
        Assert.Equal("", Render<MainLayout>(p => p.Add(l => l.Body, Body())).Find("[data-testid=flash]").TextContent);

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/?flash=signed-out");
        Assert.Equal("Wylogowano.", Render<MainLayout>(p => p.Add(l => l.Body, Body())).Find("[data-testid=flash]").TextContent);
        nav.NavigateTo("/?flash=%3Cscript%3Ealert(1)%3C%2Fscript%3E");
        Assert.Equal("", Render<MainLayout>(p => p.Add(l => l.Body, Body())).Find("[data-testid=flash]").TextContent);
    }

    [Fact]
    public void A_component_that_throws_is_contained_by_the_error_boundary_which_says_so_and_shows_nothing_of_the_exception()
    {
        AddAuthorization();
        var cut = Render<MainLayout>(p => p.Add(l => l.Body, b => { b.OpenComponent<Thrower>(0); b.CloseComponent(); }));
        var alert = cut.Find("[data-testid=error-boundary]");
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Equal("Coś poszło nie tak", alert.QuerySelector("h2")!.TextContent);
        Assert.DoesNotContain("SECRET-USER-TEXT", cut.Markup);
        Assert.NotNull(cut.Find("header")); // the shell around it still renders
    }
}

public class MyBookingsTests : BunitContext
{
    readonly FakeBackend _booking = new();
    readonly BunitAuthorizationContext _auth;

    sealed class Token(string? value) : IAccessTokenSource { public Task<string?> AccessTokenAsync(CancellationToken ct = default) => Task.FromResult(value); }

    public MyBookingsTests()
    {
        _auth = AddAuthorization();
        _auth.SetAuthorized("demo-client@demo.example");
        var http = new HttpClient(_booking) { BaseAddress = new Uri("http://booking.test") };
        Services.AddSingleton(new BookingClient(http, new VisitorAddressAccessor(new HttpContextAccessor()), new Token("tok")));
        _booking.Respond = _ => FakeBackend.Json(new PageDto<BookingViewDto>([Sample.Booking("Pierwsza"), Sample.Booking("Druga", "PendingPayment")], null));
    }

    // A SupplyParameterFromQuery parameter takes its value from the URL, as in the browser: navigate, then render.
    IRenderedComponent<MyBookings> Open(string? role = null, string? status = null)
    {
        var query = string.Join('&', new[] { role is null ? null : $"role={Uri.EscapeDataString(role)}", status is null ? null : $"status={Uri.EscapeDataString(status)}" }.Where(q => q is not null));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/moje/rezerwacje" + (query.Length > 0 ? "?" + query : ""));
        return Render<MyBookings>();
    }

    [Fact]
    public void It_lists_the_bookings_with_their_hooks_and_loads_the_client_view_by_default()
    {
        var cut = Open();
        Assert.Equal("Moje rezerwacje", cut.Find("h1").TextContent);
        Assert.Equal(["Pierwsza", "Druga"], cut.FindAll("[data-testid=booking-row] h2").Select(h => h.TextContent));
        Assert.Equal(["Potwierdzona", "Oczekuje na płatność"], cut.FindAll("[data-testid=booking-row] .badge").Select(b => b.TextContent));
        Assert.Equal("/api/v1/me/bookings?role=client", _booking.Requests.Single().PathAndQuery);
        Assert.Equal("true", cut.Find("[data-testid=role-client]").GetAttribute("aria-pressed"));
        Assert.Equal("false", cut.Find("[data-testid=role-practitioner]").GetAttribute("aria-pressed"));
        Assert.Equal("group", cut.Find("[aria-label='Pokaż rezerwacje jako']").GetAttribute("role"));
        Assert.Empty(cut.FindAll("[role=alert]"));
        Assert.Empty(cut.FindAll("[data-testid=bookings-more]"));
    }

    [Fact]
    public void The_wrapper_in_the_components_scope_hands_the_token_to_the_handler_through_the_request_options()
    {
        // BLAZOR-WEB-APP section 7, trap 2: a message handler lives in its own DI scope and cannot see the circuit's services, so the token travels on the request.
        string? seen = "not called";
        var probe = new Probe(r => seen = r.Options.TryGetValue(RequestContext.AccessToken, out var t) ? t : null);
        Services.AddSingleton(new BookingClient(new HttpClient(probe) { BaseAddress = new Uri("http://booking.test") }, new VisitorAddressAccessor(new HttpContextAccessor()), new Token("tok-from-this-scope")));
        Open();
        Assert.Equal("tok-from-this-scope", seen);
    }

    sealed class Probe(Action<HttpRequestMessage> look) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            look(request);
            return Task.FromResult(FakeBackend.Json(new PageDto<BookingViewDto>([], null)));
        }
    }

    [Fact]
    public void An_initial_view_comes_from_the_query_parameters_and_garbage_falls_back()
    {
        Open("practitioner", "Confirmed");
        Assert.Equal("/api/v1/me/bookings?role=practitioner&status=Confirmed", _booking.Requests.Last().PathAndQuery);
        Open("admin", "Hacked");
        Assert.Equal("/api/v1/me/bookings?role=client", _booking.Requests.Last().PathAndQuery);
    }

    [Fact]
    public void Switching_the_role_loads_the_other_view_and_replaces_the_list()
    {
        var cut = Open();
        _booking.Respond = _ => FakeBackend.Json(new PageDto<BookingViewDto>([Sample.Booking("Jako programista")], null));
        cut.Find("[data-testid=role-practitioner]").Click();
        cut.WaitForAssertion(() => Assert.Equal(["Jako programista"], cut.FindAll("[data-testid=booking-row] h2").Select(h => h.TextContent)));
        Assert.Equal("/api/v1/me/bookings?role=practitioner", _booking.Requests.Last().PathAndQuery);
        Assert.Equal("true", cut.Find("[data-testid=role-practitioner]").GetAttribute("aria-pressed"));
        Assert.Equal("demo-client", cut.Find("[data-testid=booking-row] p.muted").TextContent.Split(" · ")[0]); // the other party, for a practitioner, is the client
    }

    [Fact]
    public void Filtering_by_status_reloads_with_that_status_and_an_unknown_status_is_no_filter()
    {
        var cut = Open();
        cut.Find("[data-testid=status-filter]").Change("Completed");
        cut.WaitForAssertion(() => Assert.Equal("/api/v1/me/bookings?role=client&status=Completed", _booking.Requests.Last().PathAndQuery));
        cut.Find("[data-testid=status-filter]").Change("<script>");
        cut.WaitForAssertion(() => Assert.Equal("/api/v1/me/bookings?role=client", _booking.Requests.Last().PathAndQuery));
    }

    [Fact]
    public void Show_more_appends_the_next_page_by_cursor_and_disappears_on_the_last()
    {
        _booking.Respond = r => r.RequestUri!.Query.Contains("cursor=C2")
            ? FakeBackend.Json(new PageDto<BookingViewDto>([Sample.Booking("Strona druga")], null))
            : FakeBackend.Json(new PageDto<BookingViewDto>([Sample.Booking("Strona pierwsza")], "C2"));
        var cut = Open();
        cut.Find("[data-testid=bookings-more]").Click();
        cut.WaitForAssertion(() => Assert.Equal(["Strona pierwsza", "Strona druga"], cut.FindAll("[data-testid=booking-row] h2").Select(h => h.TextContent)));
        Assert.Equal("/api/v1/me/bookings?role=client&cursor=C2", _booking.Requests.Last().PathAndQuery);
        Assert.Empty(cut.FindAll("[data-testid=bookings-more]"));
    }

    [Fact]
    public void An_empty_listing_says_so_without_an_alert()
    {
        _booking.Respond = _ => FakeBackend.Json(new PageDto<BookingViewDto>([], null));
        var cut = Open();
        Assert.Equal("Brak rezerwacji w tym widoku.", cut.Find("[data-testid=bookings-empty]").TextContent);
        Assert.Empty(cut.FindAll("[role=alert]"));
    }

    [Theory]
    [InlineData(0, "Brak połączenia z usługą rezerwacji. Spróbuj ponownie za chwilę.")]
    [InlineData(503, "Rezerwacje są chwilowo niedostępne. Spróbuj ponownie za chwilę.")]
    [InlineData(429, "Zbyt wiele zapytań. Spróbuj ponownie za chwilę.")]
    [InlineData(500, "Nie udało się wczytać rezerwacji.")]
    public void A_failure_is_an_alert_in_place_and_the_controls_stay(int status, string text)
    {
        _booking.Respond = status == 0 ? FakeBackend.Throw(new HttpRequestException("x")) : _ => FakeBackend.Problem((System.Net.HttpStatusCode)status, "some_code");
        var cut = Open();
        var alert = cut.Find("[data-testid=bookings-error]");
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Equal(text, alert.TextContent);
        Assert.DoesNotContain("SERVER-TITLE-MUST-NOT-APPEAR", cut.Markup);
        Assert.Equal("Moje rezerwacje", cut.Find("h1").TextContent);
        Assert.NotNull(cut.Find("[data-testid=role-practitioner]"));
    }

    [Fact]
    public void A_401_says_the_session_ended_and_offers_to_sign_in_and_come_back()
    {
        _booking.Respond = _ => FakeBackend.Problem(System.Net.HttpStatusCode.Unauthorized, "unauthenticated");
        var cut = Open();
        var alert = cut.Find("[data-testid=bookings-error]");
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Equal("/logowanie?redirect=%2Fmoje%2Frezerwacje", alert.QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public void When_the_sign_in_ends_while_the_page_is_open_the_signed_in_markup_is_replaced_by_an_alert()
    {
        // The circuit's revalidation turns the authentication state anonymous (SessionRevalidatingAuthenticationStateProvider); the page must follow.
        _auth.SetNotAuthorized();
        var cut = Open();
        Assert.Equal("alert", cut.Find("[data-testid=bookings-signed-out]").GetAttribute("role"));
        Assert.Empty(cut.FindAll("[data-testid=bookings]"));
    }

    [Fact]
    public async Task A_load_in_flight_is_cancelled_when_the_page_goes_away_and_by_a_newer_request()
    {
        var gate = new TaskCompletionSource<HttpResponseMessage>();
        var seen = new List<CancellationToken>();
        var http = new HttpClient(new CancelWatcher(gate, seen)) { BaseAddress = new Uri("http://booking.test") };
        Services.AddSingleton(new BookingClient(http, new VisitorAddressAccessor(new HttpContextAccessor()), new Token("tok")));
        var cut = Open();
        cut.Find("[data-testid=role-practitioner]").Click(); // a newer request: the first is cancelled
        Assert.Equal(2, seen.Count);
        Assert.True(seen[0].IsCancellationRequested);
        Assert.False(seen[1].IsCancellationRequested);
        await DisposeComponentsAsync(); // the circuit ends: nothing keeps running against a dead component
        Assert.True(seen[1].IsCancellationRequested);
        gate.TrySetResult(FakeBackend.Json(new PageDto<BookingViewDto>([], null)));
    }

    sealed class CancelWatcher(TaskCompletionSource<HttpResponseMessage> gate, List<CancellationToken> seen) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { seen.Add(ct); return gate.Task.WaitAsync(ct); }
    }
}
