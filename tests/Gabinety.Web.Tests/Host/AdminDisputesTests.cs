using System.Net;
using Gabinety.Web.Api;
using Gabinety.Web.Text;

namespace Gabinety.Web.Tests.AdminPages;

/// <summary>/admin/spory: the card (P-ADM-3), the J6 hooks, the three-step resolve flow, the amount bounds, the conflicts and the stale post.</summary>
public class AdminDisputesTests
{
    static readonly Guid BookingId = Guid.Parse("0b9e1d2c-7a5f-4c3e-9b1a-2d4e6f8a0b1c");
    const string Open = "/admin/spory?status=open";

    static async Task<(AdminHost Host, AdminDisputeDto Dispute)> HostWithDisputeAsync(long price = 18000, bool bookingReadable = true, string practitionerName = "")
    {
        var host = await AdminHost.StartAsync();
        var dispute = AdminHost.Dispute(BookingId);
        var practitionerId = Guid.NewGuid();
        host.Web.Booking.Respond = r => (r.Method.Method, r.RequestUri!.AbsolutePath) switch
        {
            ("GET", "/api/v1/admin/disputes") => AdminHost.Paged(dispute),
            ("GET", var p) when p == $"/api/v1/bookings/{BookingId}" => bookingReadable ? FakeBackend.Json(AdminHost.BookingOf(BookingId, price, practitionerName: practitionerName, practitionerId: practitionerId)) : FakeBackend.Problem(HttpStatusCode.ServiceUnavailable, "dependency_unavailable"),
            ("POST", var p) when p == $"/api/v1/admin/bookings/{BookingId}/resolve" => AdminHost.NoContent(),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        host.Web.Directory.Respond = r => r.RequestUri!.AbsolutePath == $"/api/v1/admin/practitioners/{practitionerId}" ? FakeBackend.Json(AdminHost.DetailOf(practitionerId, "Anna Demowska")) : new HttpResponseMessage(HttpStatusCode.NotFound);
        return (host, dispute);
    }

    [Fact]
    public async Task A_dispute_card_shows_what_the_administrator_needs_to_decide_and_the_booking_id_as_text_the_J6_hook()
    {
        var (host, _) = await HostWithDisputeAsync(practitionerName: "");
        await using var _h = host;
        var page = await host.Browser.GetAsync(Open);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Spory", Assert.Single(page.All("h1")).TextContent);
        var card = Assert.Single(page.All("main ul[aria-label] > li"));
        var text = Page.Squash(card.TextContent).Replace(' ', ' ');
        Assert.Contains(BookingId.ToString(), text); // J6: the listitem that names the booking id
        Assert.Contains("Usługa niezgodna z opisem", card.QuerySelector("h2")!.TextContent);
        Assert.Contains("Audyt kodu .NET", text); // the offer
        Assert.Contains("180,00 zł", text); // the amount at issue (P-ADM-1)
        Assert.Equal("180,00 zł", AdminHost.Fact(card, "Najwyższy możliwy zwrot"));
        Assert.Equal("180,00 zł", AdminHost.Fact(card, "Cena rezerwacji"));
        Assert.Equal("Anna Demowska", AdminHost.Fact(card, "Programista"));
        Assert.Contains("Anna Demowska", text); // the practitioner by name, read from Directory
        Assert.Contains("Opłacona", text);
        Assert.Contains("24 lis 2026, 13:00", text); // no seconds, short month
        Assert.DoesNotContain("13:00:09", text);
        Assert.Contains("Klient demo-cli", text); // a short reference, never the raw user id as the primary text
        Assert.DoesNotContain("demo-client-2", text);
        Assert.Contains("Konsultacja różniła się od opisu", text);
        Assert.Contains(card.QuerySelectorAll("button"), b => b.TextContent.Trim() == "Rozstrzygnij");
        Assert.Equal("Otwarte\u00A0(1)", page.TextOf(".segmented a.chip[aria-current=true]"));
    }

    [Fact]
    public async Task A_booking_that_cannot_be_read_leaves_the_card_without_the_amount_and_says_so()
    {
        var (host, _) = await HostWithDisputeAsync(bookingReadable: false);
        await using var _h = host;
        var card = Assert.Single((await host.Browser.GetAsync(Open)).All("main ul[aria-label] > li"));
        Assert.Contains(PlAdmin.Disputes.BookingUnreadable, card.TextContent);
        Assert.DoesNotContain("Najwyższy możliwy zwrot", card.TextContent);
        Assert.Contains(card.QuerySelectorAll("button"), b => b.TextContent.Trim() == "Rozstrzygnij"); // the report and the form still work: the server checks the bounds
    }

    [Fact]
    public async Task Rozstrzygnij_opens_the_form_in_the_same_card_with_the_J6_labels_and_roles_and_changes_nothing()
    {
        var (host, _) = await HostWithDisputeAsync();
        await using var _h = host;
        var page = await host.Browser.GetAsync($"{Open}&rozstrzygnij={BookingId}");
        var card = Assert.Single(page.All("main ul[aria-label] > li"));
        Assert.Equal(["Wypłać programiście", "Zwróć klientowi całość", "Zwróć klientowi część"], card.QuerySelectorAll("input[type=radio]").Select(r => Page.Squash(r.ParentElement!.QuerySelector("span")!.ChildNodes[0].TextContent)));
        Assert.Equal("Kwota zwrotu w zł", card.QuerySelector("label[for^=amount]")!.TextContent);
        Assert.StartsWith("Notatka", card.QuerySelector("label[for^=note]")!.TextContent);
        Assert.Contains("Od 0,01 zł do 180,00", Page.Squash(card.QuerySelector(".field__hint")!.TextContent).Replace(' ', ' ') + Page.Squash(card.TextContent).Replace(' ', ' ')); // the maximum is the field's hint
        Assert.Contains(card.QuerySelectorAll("button"), b => b.TextContent.Trim() == "Przejdź do potwierdzenia");
        Assert.DoesNotContain(card.QuerySelectorAll("button"), b => b.TextContent.Contains("Potwierdź rozstrzygnięcie"));
        Assert.NotNull(card.QuerySelector("form[method=post] input[name=__RequestVerificationToken]"));
        Assert.DoesNotContain(host.Web.Booking.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task The_resolve_flow_for_a_partial_refund_is_choose_check_the_consequence_then_confirm()
    {
        var (host, _) = await HostWithDisputeAsync();
        await using var _h = host;
        var url = $"{Open}&rozstrzygnij={BookingId}";
        var form = $"resolve-{BookingId}";
        var edit = await host.Browser.GetAsync(url);

        // step 1 -> 2: nothing is sent to the service, the page says what the money does
        var review = await host.PostAsync(edit, url, form, ("resolution", "refund_partial"), ("amount", "100,00"), ("note", "Częściowy zwrot po sprawdzeniu zgłoszenia, test."), ("step", "review"));
        Assert.Equal(HttpStatusCode.OK, review.Status);
        var card = Assert.Single(review.All("main ul[aria-label] > li"));
        var consequence = Page.Squash(card.QuerySelector("[data-testid=resolve-consequence]")!.TextContent).Replace(' ', ' ');
        Assert.Contains("100,00 zł", consequence); // the refund
        Assert.Contains("80,00 zł", consequence); // the rest, which the practitioner gets less the fee
        Assert.Contains("Tej decyzji nie można cofnąć", consequence);
        Assert.Contains(card.QuerySelectorAll("button"), b => b.TextContent.Trim() == "Potwierdź rozstrzygnięcie");
        Assert.Contains(card.QuerySelectorAll("button"), b => b.TextContent.Trim() == "Wróć");
        Assert.DoesNotContain(host.Web.Booking.Requests, r => r.Method == HttpMethod.Post);

        // step 3: the confirm posts what the confirm step carries, and goes back to the open list with the message
        var confirm = await host.PostAsync(review, url, form, ("step", "confirm"));
        Assert.Equal(HttpStatusCode.Redirect, confirm.Status);
        Assert.Equal("/admin/spory?status=open&flash=admin-dispute-resolved", confirm.Location);
        var post = Assert.Single(host.Web.Booking.Requests, r => r.Method == HttpMethod.Post);
        Assert.Equal($"/api/v1/admin/bookings/{BookingId}/resolve", post.PathAndQuery);
        Assert.NotNull(post.Authorization);
        var json = AdminHost.Body(post);
        Assert.Equal(("refund_partial", 10000L, "Częściowy zwrot po sprawdzeniu zgłoszenia, test."), (json.GetProperty("resolution").GetString(), json.GetProperty("refundMinor").GetInt64(), json.GetProperty("note").GetString()));
    }

    [Theory]
    [InlineData("release")]
    [InlineData("refund_full")]
    public async Task A_release_and_a_full_refund_carry_no_amount(string resolution)
    {
        var (host, _) = await HostWithDisputeAsync();
        await using var _h = host;
        var url = $"{Open}&rozstrzygnij={BookingId}";
        var edit = await host.Browser.GetAsync(url);
        var done = await host.PostAsync(edit, url, $"resolve-{BookingId}", ("resolution", resolution), ("amount", "99,00"), ("note", "Notatka o dziesięciu znakach"), ("step", "confirm"));
        Assert.Equal(HttpStatusCode.Redirect, done.Status);
        var json = AdminHost.Body(Assert.Single(host.Web.Booking.Requests, r => r.Method == HttpMethod.Post));
        Assert.Equal(resolution, json.GetProperty("resolution").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.GetProperty("refundMinor").ValueKind); // a stray amount is not sent
        Assert.Equal("Notatka o dziesięciu znakach", json.GetProperty("note").GetString());
    }

    [Theory]
    [InlineData("refund_partial", "180,00", "Nie można", "mniejsza od ceny (180,00")]
    [InlineData("refund_partial", "0", "Nie można", "mniejsza od ceny")]
    [InlineData("refund_partial", "1,234", "Nie można", "dwa miejsca")]
    [InlineData("refund_partial", "sto", "Nie można", "Podaj kwotę")]
    [InlineData("", "", "Nie można", "Wybierz rozstrzygnięcie")]
    public async Task A_form_that_is_not_valid_never_reaches_the_confirm_step_or_the_service_and_every_problem_is_shown_with_what_was_typed(string resolution, string amount, string _, string expected)
    {
        var (host, _h) = await HostWithDisputeAsync();
        await using var _d = host;
        var url = $"{Open}&rozstrzygnij={BookingId}";
        var edit = await host.Browser.GetAsync(url);
        var answer = await host.PostAsync(edit, url, $"resolve-{BookingId}", ("resolution", resolution), ("amount", amount), ("note", "krótka"), ("step", "confirm")); // the confirm is forged straight away: still validated
        Assert.Equal(HttpStatusCode.OK, answer.Status);
        var summary = answer.One("[data-testid=resolve-problems]")!;
        Assert.Equal("alert", summary.GetAttribute("role"));
        var text = Page.Squash(summary.TextContent).Replace(' ', ' ');
        Assert.Contains(expected, text);
        Assert.Contains("Podaj notatkę: od 10 do 500 znaków.", text); // all problems at once
        Assert.Equal("krótka", answer.One("textarea[name=note]")!.TextContent.Trim());
        Assert.Equal("true", answer.One("textarea[name=note]")!.GetAttribute("aria-invalid"));
        Assert.DoesNotContain(host.Web.Booking.Requests, r => r.Method == HttpMethod.Post);
        Assert.DoesNotContain(answer.All("button"), b => b.TextContent.Contains("Potwierdź rozstrzygnięcie"));
    }

    [Fact]
    public async Task Back_from_the_confirm_step_keeps_everything_that_was_typed()
    {
        var (host, _) = await HostWithDisputeAsync();
        await using var _h = host;
        var url = $"{Open}&rozstrzygnij={BookingId}";
        var edit = await host.Browser.GetAsync(url);
        var review = await host.PostAsync(edit, url, $"resolve-{BookingId}", ("resolution", "refund_partial"), ("amount", "42,5"), ("note", "Notatka o dziesięciu znakach"), ("step", "review"));
        var back = await host.PostAsync(review, url, $"resolve-{BookingId}", ("step", "back"));
        Assert.Equal("42,5", back.One("input[name=amount]")!.GetAttribute("value"));
        Assert.Equal("Notatka o dziesięciu znakach", back.One("textarea[name=note]")!.TextContent.Trim());
        Assert.True(back.One("input[type=radio][value=refund_partial]")!.HasAttribute("checked"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "invalid_transition", "Stan się zmienił, odśwież stronę.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "dependency_unavailable", "Operator płatności jest chwilowo niedostępny")]
    [InlineData(HttpStatusCode.BadRequest, "validation_failed", "Serwer odrzucił dane rozstrzygnięcia.")]
    [InlineData(HttpStatusCode.NotFound, "not_found", "Nie znaleziono rezerwacji.")]
    public async Task A_refusal_of_the_service_is_a_clear_message_never_a_crash_and_never_the_servers_own_text(HttpStatusCode status, string code, string expected)
    {
        var (host, _) = await HostWithDisputeAsync();
        await using var _h = host;
        var ok = host.Web.Booking.Respond;
        host.Web.Booking.Respond = r => r.Method == HttpMethod.Post ? FakeBackend.Problem(status, code) : ok(r);
        var url = $"{Open}&rozstrzygnij={BookingId}";
        var edit = await host.Browser.GetAsync(url);
        var answer = await host.PostAsync(edit, url, $"resolve-{BookingId}", ("resolution", "release"), ("amount", ""), ("note", "Notatka o dziesięciu znakach"), ("step", "confirm"));
        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.Contains(expected, answer.Text);
        Assert.DoesNotContain("SERVER-TITLE", answer.Html);
        Assert.DoesNotContain("SERVER-DETAIL", answer.Html);
        Assert.True(answer.HasAlert || code == "invalid_transition"); // a refusal is announced; a conflict is a polite status message
    }

    [Fact]
    public async Task A_second_confirm_of_a_dispute_somebody_resolved_is_the_stale_message_and_the_list_without_it_not_an_error_page()
    {
        var (host, _) = await HostWithDisputeAsync();
        await using var _h = host;
        var resolved = false;
        var ok = host.Web.Booking.Respond;
        host.Web.Booking.Respond = r => resolved && r.RequestUri!.AbsolutePath == "/api/v1/admin/disputes" ? AdminHost.Paged<AdminDisputeDto>() : ok(r);
        var url = $"{Open}&rozstrzygnij={BookingId}";
        var edit = await host.Browser.GetAsync(url);
        resolved = true; // somebody else resolved it between the page and the click
        var answer = await host.PostAsync(edit, url, $"resolve-{BookingId}", ("resolution", "release"), ("note", "Notatka o dziesięciu znakach"), ("step", "confirm"));
        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.Contains(PlAdmin.Common.StaleForm, answer.Text);
        Assert.Empty(answer.All("main ul[aria-label] > li"));
        Assert.DoesNotContain(host.Web.Booking.Requests, r => r.Method == HttpMethod.Post); // nothing was resolved a second time
    }

    [Fact]
    public async Task A_post_without_the_antiforgery_token_resolves_nothing()
    {
        var (host, _) = await HostWithDisputeAsync();
        await using var _h = host;
        var forged = await host.PostRawAsync($"{Open}&rozstrzygnij={BookingId}", [KeyValuePair.Create("_handler", $"resolve-{BookingId}"), KeyValuePair.Create("resolution", "refund_full"), KeyValuePair.Create("note", "Notatka o dziesięciu znakach"), KeyValuePair.Create("step", "confirm")]);
        Assert.Equal(HttpStatusCode.BadRequest, forged.Status);
        Assert.DoesNotContain(host.Web.Booking.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task The_resolved_tab_shows_the_outcome_and_the_note_and_offers_no_form()
    {
        await using var host = await AdminHost.StartAsync();
        var done = AdminHost.Dispute(BookingId) with { ResolvedAt = new DateTimeOffset(2026, 11, 25, 9, 30, 0, TimeSpan.Zero), Resolution = "refund_partial", Note = "Częściowy zwrot po sprawdzeniu." };
        host.Web.Booking.Respond = r => r.RequestUri!.AbsolutePath == "/api/v1/admin/disputes" ? AdminHost.Paged(done) : FakeBackend.Json(AdminHost.BookingOf(BookingId, payment: "PartiallyRefunded", refunded: 10000));
        host.Web.Directory.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        var page = await host.Browser.GetAsync("/admin/spory?status=resolved");
        var text = Page.Squash(page.TextOf("main ul[aria-label] > li")).Replace(' ', ' ');
        Assert.Contains("Częściowy zwrot dla klienta", text);
        Assert.Contains("Częściowy zwrot po sprawdzeniu.", text);
        Assert.Contains("Zwrócono: 100,00 zł", text);
        Assert.Contains("Zwrócona częściowo", text);
        Assert.Contains("25 lis 2026, 10:30", text);
        Assert.Empty(page.All("main button"));
        Assert.Contains("status=resolved", host.Web.Booking.Requests[0].PathAndQuery);
        Assert.StartsWith("Rozstrzygnięte", page.TextOf(".segmented a.chip[aria-current=true]"));
    }

    [Theory]
    [InlineData("?status=banana")]
    [InlineData("?status=open&cursor=%3Cscript%3E")]
    [InlineData("?rozstrzygnij=not-a-guid")]
    public async Task A_query_that_is_not_ours_is_ignored_and_never_forwarded_to_the_service(string query)
    {
        var (host, _) = await HostWithDisputeAsync();
        await using var _h = host;
        var page = await host.Browser.GetAsync("/admin/spory" + query);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        var asked = host.Web.Booking.Requests[0].PathAndQuery;
        Assert.Contains("status=open", asked);
        Assert.DoesNotContain("banana", asked);
        Assert.DoesNotContain("script", asked, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_service_that_is_down_is_an_alert_with_the_unavailable_hook_and_the_page_still_renders()
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Booking.Respond = _ => FakeBackend.Problem(HttpStatusCode.ServiceUnavailable, "dependency_unavailable");
        var page = await host.Browser.GetAsync(Open);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(PlAdmin.Disputes.Unavailable, page.TextOf("[data-testid=unavailable]"));
        Assert.Equal("alert", page.One("[data-testid=unavailable]")!.GetAttribute("role"));
        Assert.Equal("Spory", page.TextOf("h1"));
        Assert.DoesNotContain("SERVER-TITLE", page.Html);
    }

    [Fact]
    public async Task At_most_five_booking_reads_run_at_once_for_a_page_of_cards()
    {
        await using var host = await AdminHost.StartAsync();
        var disputes = Enumerable.Range(0, 12).Select(_ => AdminHost.Dispute()).ToArray();
        int active = 0, peak = 0;
        host.Web.Booking.Respond = r =>
        {
            if (r.RequestUri!.AbsolutePath == "/api/v1/admin/disputes") return AdminHost.Paged(disputes);
            var now = Interlocked.Increment(ref active);
            int seen; while ((seen = Volatile.Read(ref peak)) < now && Interlocked.CompareExchange(ref peak, now, seen) != seen) { }
            Thread.Sleep(30);
            Interlocked.Decrement(ref active);
            return FakeBackend.Json(AdminHost.BookingOf(Guid.Parse(r.RequestUri.AbsolutePath.Split('/')[^1])));
        };
        host.Web.Directory.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        var page = await host.Browser.GetAsync(Open);
        Assert.Equal(12, page.All("main ul[aria-label] > li").Count);
        Assert.InRange(peak, 1, 5);
    }
}
