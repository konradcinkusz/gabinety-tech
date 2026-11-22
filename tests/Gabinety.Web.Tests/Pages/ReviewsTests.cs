using System.Net;
using System.Text.Json;
using Gabinety.Web.Api;
using static Gabinety.Web.Tests.Pages.ClientAreaFixture;

namespace Gabinety.Web.Tests.Pages;

/// <summary>The review form of a client (J3, F-14) and the practitioner's list with its replies.</summary>
public class ReviewsTests
{
    static readonly string Form = $"/moje/opinie/{BookingId}";
    const string Text = "Konsultacja przebiegła rzetelnie, dostałem konkretny plan prototypu.";
    static readonly Guid ReviewId = Guid.Parse("018f0000-0000-7000-8000-0000000000d1");

    static async Task<ClientAreaFixture> Open(Func<HttpRequestMessage, HttpResponseMessage>? reputation = null)
    {
        var f = await ClientAreaFixture.SignedIn();
        f.Web.Reputation.Respond = reputation ?? (_ => FakeBackend.Json(new CreateReviewResponseDto(ReviewId, "Published"), HttpStatusCode.Created));
        return f;
    }

    // ---- the form

    [Fact]
    public async Task The_form_has_the_hooks_of_the_journey_five_named_radios_a_labelled_text_and_the_button()
    {
        await using var f = await Open();
        var page = await f.Browser.GetAsync(Form);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(["Wystaw opinię"], page.All("h1").Select(h => h.TextContent));
        Assert.Equal("Ocena", page.TextOf("fieldset legend"));
        var radios = page.All("input[type=radio][name='Model.Rating']").ToList();
        Assert.Equal(["1", "2", "3", "4", "5"], radios.Select(r => r.GetAttribute("value")));
        Assert.Equal(["1 z 5", "2 z 5", "3 z 5", "4 z 5", "5 z 5"], radios.Select(r => r.ParentElement!.QuerySelector(".sr-only")!.TextContent)); // the accessible name: getByRole("radio", { name: "5 z 5" })
        Assert.All(radios, r => Assert.Equal("LABEL", r.ParentElement!.TagName)); // the whole option is the label: a 44 px target
        Assert.Equal("Treść opinii", page.One("label[for=text]")!.TextContent);
        Assert.Equal(["Wyślij opinię"], page.All("form button").Select(b => b.TextContent));
        Assert.Contains("1 oznacza „słabo”", page.TextOf("#rating-hint"));
        Assert.NotNull(page.One("form input[name=__RequestVerificationToken]"));
        Assert.Empty(f.Web.Reputation.Requests);
    }

    [Fact]
    public async Task A_visitor_is_sent_to_sign_in_and_back()
    {
        await using var f = await ClientAreaFixture.Visitor();
        Assert.Equal($"/logowanie?redirect=%2Fmoje%2Fopinie%2F{BookingId}", (await f.Browser.GetAsync(Form)).Location);
    }

    [Fact]
    public async Task Both_problems_are_listed_at_once_each_linked_to_its_field_and_inline_and_nothing_is_sent()
    {
        await using var f = await Open();
        var page = await f.Post(Form, "review", ("Model.Text", "krótko"));
        Assert.Equal("Popraw 2 pola", page.TextOf(".form-summary h2"));
        Assert.Equal(["Wybierz ocenę od 1 do 5.", "Opinia musi mieć co najmniej 20 znaków."], page.All(".form-summary a").Select(a => a.TextContent));
        Assert.Equal(["#rating-1", "#text"], page.All(".form-summary a").Select(a => a.GetAttribute("href")));
        Assert.NotNull(page.One("#rating-1")); // the link points at a real control
        Assert.Equal("Błąd: Wybierz ocenę od 1 do 5.", Page.Squash(page.TextOf("#rating-error")));
        Assert.Equal("Błąd: Opinia musi mieć co najmniej 20 znaków.", Page.Squash(page.TextOf("#text-error")));
        Assert.Contains("rating-error", page.One("fieldset")!.GetAttribute("aria-describedby"));
        Assert.Equal("true", page.One("#text")!.GetAttribute("aria-invalid"));
        Assert.Equal("krótko", page.One("#text")!.TextContent);
        Assert.NotNull(page.One(".form-summary[autofocus]")); // two problems: focus goes to the summary
        Assert.Empty(f.Web.Reputation.Requests);
    }

    [Fact]
    public async Task A_chosen_rating_survives_a_failed_submit_and_shows_its_word()
    {
        await using var f = await Open();
        var page = await f.Post(Form, "review", ("Model.Rating", "4"), ("Model.Text", "krótko"));
        Assert.Equal("Popraw 1 pole", page.TextOf(".form-summary h2"));
        Assert.Equal(["4"], page.All("input[type=radio]:checked").Select(r => r.GetAttribute("value")));
        Assert.Equal("Bardzo dobrze", page.TextOf(".rating-words .rating-word--4")); // CSS shows this one word of the five
    }

    [Fact]
    public async Task The_rating_is_five_star_targets_each_named_n_z_5_and_all_five_words_are_rendered_in_order()
    {
        await using var f = await Open();
        var page = await f.Browser.GetAsync(Form);
        var options = page.All(".rating-scale > label.rating-scale__option").ToList();
        Assert.Equal(5, options.Count);
        Assert.All(options, o =>
        {
            var star = o.QuerySelector(".rating-scale__star")!;
            Assert.Equal("true", star.GetAttribute("aria-hidden")); // decoration: the accessible name is the radio's "n z 5"
            Assert.Equal("\u2605", star.TextContent); // U+2605 BLACK STAR
        });
        Assert.Equal(["Słabo", "Przeciętnie", "Dobrze", "Bardzo dobrze", "Świetnie"], page.All(".rating-words .rating-word").Select(w => w.TextContent));
        Assert.Equal(["rating-word rating-word--1", "rating-word rating-word--2", "rating-word rating-word--3", "rating-word rating-word--4", "rating-word rating-word--5"], page.All(".rating-words .rating-word").Select(w => w.GetAttribute("class")));
        Assert.Empty(page.All("input[type=radio]:checked")); // a first visit has no rating, so no word is shown
    }

    [Theory]
    [InlineData("0")] [InlineData("6")] [InlineData("abc")] [InlineData("-1")]
    public async Task A_rating_outside_one_to_five_is_not_accepted(string rating)
    {
        await using var f = await Open();
        var page = await f.Post(Form, "review", ("Model.Rating", rating), ("Model.Text", Text));
        Assert.Contains("Wybierz ocenę od 1 do 5.", page.Text);
        Assert.Empty(f.Web.Reputation.Requests);
    }

    // ---- success (F-14)

    [Theory]
    [InlineData("Published", "published", "Opinia została opublikowana na profilu programisty.")]
    [InlineData("Held", "held", "Opinia czeka na sprawdzenie.")]
    [InlineData("Whatever", "held", "Opinia czeka na sprawdzenie.")]
    public async Task A_sent_review_redirects_and_the_page_thanks_with_the_heading_the_journey_expects(string status, string key, string text)
    {
        await using var f = await Open(_ => FakeBackend.Json(new CreateReviewResponseDto(ReviewId, status), HttpStatusCode.Created));
        var posted = await f.Post(Form, "review", ("Model.Rating", "5"), ("Model.Text", $"  {Text}  "));
        Assert.Equal(HttpStatusCode.Found, posted.Status);
        Assert.Equal(Form + "?sent=" + key, posted.Location);
        Assert.DoesNotContain("Konsultacja", posted.Location ?? ""); // the text never travels in a URL
        var call = Assert.Single(f.Web.Reputation.Requests);
        Assert.Equal("/api/v1/reviews", call.PathAndQuery);
        Assert.StartsWith("Bearer eyJ", call.Authorization);
        var body = JsonDocument.Parse(call.Body!).RootElement;
        Assert.Equal((BookingId.ToString(), 5, Text), (body.GetProperty("bookingId").GetString()!, body.GetProperty("rating").GetInt32(), body.GetProperty("text").GetString()!));

        var page = await f.Browser.GetAsync(posted.Location!);
        Assert.Equal(["Wystaw opinię"], page.All("h1").Select(h => h.TextContent));
        Assert.Equal("Dziękujemy za opinię", page.TextOf("h2")); // J3: getByRole("heading", { name: "Dziękujemy za opinię" })
        Assert.StartsWith(text, page.TextOf("section [role=status]"));
        Assert.DoesNotContain("Ta konsultacja ma już opinię", page.Text); // F-14
        Assert.Empty(page.All("form"));
        Assert.Equal("/moje/rezerwacje", page.One("section a")!.GetAttribute("href"));
        Assert.Equal(published(status), page.One("section [role=status]")!.ClassName?.Contains("callout--success") == true);
    }

    static bool published(string status) => status == "Published";

    [Theory]
    [InlineData("<script>")] [InlineData("")] [InlineData("Dziękujemy")]
    public async Task A_sent_key_that_is_not_ours_shows_the_form_not_a_thank_you(string key)
    {
        await using var f = await Open();
        var page = await f.Browser.GetAsync(Form + "?sent=" + Uri.EscapeDataString(key));
        Assert.NotNull(page.One("form"));
        Assert.DoesNotContain("Dziękujemy za opinię", page.Text);
    }

    // ---- refusals

    [Theory]
    [InlineData("already_reviewed", "Ta konsultacja ma już opinię")]
    [InlineData("not_reviewable", "Tej konsultacji nie można ocenić")]
    public async Task A_booking_that_has_a_review_or_cannot_have_one_gets_a_card_with_the_way_back(string code, string heading)
    {
        await using var f = await Open(_ => FakeBackend.Problem(HttpStatusCode.Conflict, code));
        var page = await f.Post(Form, "review", ("Model.Rating", "5"), ("Model.Text", Text));
        Assert.Equal(heading, page.TextOf("h2"));
        Assert.Empty(page.All("form"));
        Assert.Equal("/moje/rezerwacje", page.One("section a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task A_text_the_service_refuses_is_said_under_the_field_in_our_words()
    {
        await using var f = await Open(_ => FakeBackend.Raw("""{"code":"validation_failed","status":422,"errors":{"text":["Text must not contain contact details"]}}""", HttpStatusCode.UnprocessableEntity));
        var page = await f.Post(Form, "review", ("Model.Rating", "5"), ("Model.Text", Text));
        Assert.Equal("Treść opinii nie została przyjęta. Popraw ją i spróbuj ponownie.", page.TextOf(".form-summary a"));
        Assert.DoesNotContain("contact details", page.Html);
        Assert.Equal(Text, page.One("#text")!.TextContent);
    }

    [Theory]
    [InlineData(503, "dependency_unavailable", "Usługa jest chwilowo niedostępna. Spróbuj za chwilę.")]
    [InlineData(429, "rate_limited", "Zbyt wiele prób. Spróbuj za chwilę.")]
    [InlineData(403, "forbidden", "Nie masz do tego uprawnień.")]
    [InlineData(500, "http_500", "Coś poszło nie tak. Spróbuj ponownie.")]
    public async Task Other_refusals_are_an_alert_and_the_form_keeps_what_was_typed(int status, string code, string text)
    {
        await using var f = await Open(_ => FakeBackend.Problem((HttpStatusCode)status, code));
        var page = await f.Post(Form, "review", ("Model.Rating", "3"), ("Model.Text", Text));
        Assert.Equal(text, page.TextOf("[data-testid=review-error] p"));
        Assert.Equal("alert", page.One("[data-testid=review-error]")!.GetAttribute("role"));
        Assert.Equal(Text, page.One("#text")!.TextContent);
        Assert.Equal(["3"], page.All("input[type=radio]:checked").Select(r => r.GetAttribute("value")));
        Assert.DoesNotContain("SERVER-TITLE", page.Html);
    }

    [Fact]
    public async Task A_401_goes_to_sign_in_and_the_review_text_is_never_logged()
    {
        const string userText = "Znacznik opinii syntetyczna treść opinii";
        await using var f = await Open(_ => FakeBackend.Problem(HttpStatusCode.Unauthorized, "unauthenticated"));
        var page = await f.Post(Form, "review", ("Model.Rating", "5"), ("Model.Text", userText));
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.StartsWith("/logowanie?redirect=", page.Location);
        f.Web.Reputation.Respond = FakeBackend.Throw(new HttpRequestException("down"));
        await f.Post(Form, "review", ("Model.Rating", "5"), ("Model.Text", userText));
        Assert.DoesNotContain(f.Logs.Lines, l => l.Contains("Znacznik opinii", StringComparison.Ordinal));
        Assert.Equal(2, f.Web.Reputation.Calls); // never retried by itself
    }

    // ---- the practitioner's list

    static async Task<ClientAreaFixture> Practitioner(IReadOnlyList<ReviewDto> reviews, string? next = null, Func<HttpRequestMessage, HttpResponseMessage>? replies = null)
    {
        var f = await ClientAreaFixture.SignedIn();
        f.Web.Directory.Respond = r => r.RequestUri!.AbsolutePath == "/api/v1/me" ? FakeBackend.Json(new MeDto("user-1", Guid.Parse("018f0000-0000-7000-8000-0000000000e1"), "Approved", false, false)) : new HttpResponseMessage(HttpStatusCode.NotFound);
        f.Web.Reputation.Respond = r => r.Method == HttpMethod.Post ? (replies ?? (_ => FakeBackend.Json(new { })))(r) : FakeBackend.Json(new PageDto<ReviewDto>(reviews, next));
        return f;
    }

    static ReviewDto Review(string text = "Świetna konsultacja, polecam.", int rating = 5, ReplyDto? reply = null, Guid? id = null) =>
        new(id ?? ReviewId, rating, text, new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero), reply);

    [Fact]
    public async Task A_client_without_a_profile_is_not_offered_a_list_that_is_not_theirs_P_REV_2()
    {
        await using var f = await ClientAreaFixture.SignedIn();
        var page = await f.Browser.GetAsync("/moje/opinie");
        var empty = page.One("[data-testid=no-profile]")!;
        Assert.Equal("Opinie widzą tu programiści", empty.QuerySelector("h2")!.TextContent);
        Assert.Equal("/gabinet-programisty", empty.QuerySelector("a")!.GetAttribute("href"));
        Assert.Empty(f.Web.Reputation.Requests); // nothing to list
    }

    [Fact]
    public async Task The_list_shows_the_reviews_as_text_with_a_reply_link_for_the_ones_without_a_reply()
    {
        await using var f = await Practitioner([Review("<b>Dobrze</b> bardzo"), Review("Druga opinia, z odpowiedzią.", 4, new ReplyDto("Dziękuję za opinię.", DateTimeOffset.UtcNow), Guid.NewGuid())]);
        var page = await f.Browser.GetAsync("/moje/opinie");
        Assert.Equal("Opinie o mnie", page.TextOf("h1"));
        var items = page.All("main ul > li").ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal("Zweryfikowany klient · Ocena: 5 z 5 · 20 listopada 2026", items[0].QuerySelector("p.muted")!.TextContent);
        Assert.Equal("<b>Dobrze</b> bardzo", items[0].QuerySelector(".review-text")!.TextContent);
        Assert.Empty(items[0].QuerySelectorAll("b"));
        Assert.Equal($"/moje/opinie?reply={ReviewId}", items[0].QuerySelector("a.btn")!.GetAttribute("href"));
        Assert.Equal("Twoja odpowiedź", items[1].QuerySelector(".reply strong")!.TextContent);
        Assert.Equal("Dziękuję za opinię.", items[1].QuerySelector(".reply .review-text")!.TextContent);
        Assert.Empty(items[1].QuerySelectorAll("a.btn"));
        Assert.Empty(page.All("form")); // one reply form at a time, only when asked for
    }

    [Fact]
    public async Task Opening_a_reply_shows_exactly_one_form_with_the_label_and_hint_and_counter()
    {
        await using var f = await Practitioner([Review(), Review("Inna", 3, null, Guid.NewGuid())]);
        var page = await f.Browser.GetAsync($"/moje/opinie?reply={ReviewId}");
        var form = Assert.Single(page.All("form[method=post]"));
        Assert.Equal("Odpowiedź na opinię", form.QuerySelector("label")!.TextContent);
        Assert.Contains("Odpowiedzieć można raz", form.QuerySelector(".field__hint")!.TextContent);
        Assert.Equal(ReviewId.ToString(), form.QuerySelector("input[name='Model.ReviewId']")!.GetAttribute("value"));
        Assert.Equal(["Wyślij odpowiedź"], form.QuerySelectorAll("button").Select(b => b.TextContent));
    }

    [Fact]
    public async Task A_reply_is_posted_to_the_review_and_the_page_says_it_was_added()
    {
        await using var f = await Practitioner([Review()]);
        var posted = await f.Post($"/moje/opinie?reply={ReviewId}", "reply", ("Model.Text", "  Dziękuję za miłe słowa i zapraszam ponownie.  "));
        Assert.Equal("/moje/opinie?replied=1", posted.Location);
        var call = f.Web.Reputation.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal($"/api/v1/reviews/{ReviewId}/reply", call.PathAndQuery);
        Assert.Equal("Dziękuję za miłe słowa i zapraszam ponownie.", JsonDocument.Parse(call.Body!).RootElement.GetProperty("text").GetString());
        Assert.StartsWith("Bearer eyJ", call.Authorization);
        Assert.Equal("Odpowiedź została dodana.", (await f.Browser.GetAsync("/moje/opinie?replied=1")).TextOf(".callout--success"));
    }

    [Fact]
    public async Task A_short_reply_is_refused_inline_and_a_second_reply_is_said_to_exist_already()
    {
        await using var f = await Practitioner([Review()], replies: _ => FakeBackend.Problem(HttpStatusCode.Conflict, "already_replied"));
        var short_ = await f.Post($"/moje/opinie?reply={ReviewId}", "reply", ("Model.Text", "krótko"));
        Assert.Equal("Odpowiedź musi mieć co najmniej 10 znaków.", short_.TextOf(".form-summary a"));
        Assert.DoesNotContain(f.Web.Reputation.Requests, r => r.Method == HttpMethod.Post);
        var twice = await f.Post($"/moje/opinie?reply={ReviewId}", "reply", ("Model.Text", "Dziękuję bardzo za tę opinię."));
        Assert.Equal("Na tę opinię już odpowiedziano.", twice.TextOf("form [role=alert]"));
    }

    [Fact]
    public async Task An_empty_list_says_so_with_a_heading_and_more_is_a_cursor_link()
    {
        await using var empty = await Practitioner([]);
        var page = await empty.Browser.GetAsync("/moje/opinie");
        Assert.Equal("Brak opinii", page.TextOf(".empty-state h2"));
        await using var more = await Practitioner([Review()], next: "C 2");
        Assert.Equal("/moje/opinie?cursor=C%202", (await more.Browser.GetAsync("/moje/opinie")).One("a[rel=next]")!.GetAttribute("href"));
        Assert.Contains("cursor=C2", more.Web.Reputation.Requests.Last().PathAndQuery.Length > 0 ? (await Cursor(more)) : "");
    }

    static async Task<string> Cursor(ClientAreaFixture f) { await f.Browser.GetAsync("/moje/opinie?cursor=C2"); return f.Web.Reputation.Requests.Last().PathAndQuery; }

    [Theory]
    [InlineData(503, "Usługa jest chwilowo niedostępna. Spróbuj za chwilę.")]
    [InlineData(500, "Coś poszło nie tak. Spróbuj ponownie.")]
    public async Task A_list_that_cannot_be_read_is_an_alert_with_a_retry(int status, string text)
    {
        await using var f = await Practitioner([]);
        f.Web.Reputation.Respond = _ => FakeBackend.Problem((HttpStatusCode)status, "x");
        var page = await f.Browser.GetAsync("/moje/opinie");
        Assert.Equal(text, page.TextOf("[data-testid=unavailable] p"));
        Assert.Equal("/moje/opinie", page.One("[data-testid=unavailable] a")!.GetAttribute("href"));
    }
}
