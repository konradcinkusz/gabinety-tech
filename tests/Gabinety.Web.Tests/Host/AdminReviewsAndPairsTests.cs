using System.Net;
using Gabinety.Web.Api;
using Gabinety.Web.Text;

namespace Gabinety.Web.Tests.AdminPages;

/// <summary>/admin/opinie, /admin/podejrzane and the verification detail through the real host.</summary>
public class AdminReviewsAndPairsTests
{
    static readonly Guid ReviewId = Guid.Parse("11111111-2222-3333-4444-555555555555"), PractitionerId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    static AdminReviewDto Review(string status = "Held", string? text = "Opinia testowa, dane syntetyczne, wstrzymana przez asystenta.") =>
        new(ReviewId, PractitionerId, 4, text, status, ["personal_data", "banana"], "hold", "model-demo", new DateTimeOffset(2026, 11, 24, 12, 0, 9, TimeSpan.Zero), null, null, null);

    static async Task<AdminHost> HostAsync(string status = "Held")
    {
        var host = await AdminHost.StartAsync();
        host.Web.Reputation.Respond = r => r.Method == HttpMethod.Post ? AdminHost.NoContent() : AdminHost.Paged(Review(status));
        host.Web.Directory.Respond = r => r.RequestUri!.AbsolutePath == $"/api/v1/admin/practitioners/{PractitionerId}" ? FakeBackend.Json(AdminHost.DetailOf(PractitionerId)) : new HttpResponseMessage(HttpStatusCode.NotFound);
        return host;
    }

    [Fact]
    public async Task A_held_review_shows_what_a_moderator_needs_without_raw_ids_or_wire_values()
    {
        await using var host = await HostAsync();
        var page = await host.Browser.GetAsync("/admin/opinie");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Opinie do moderacji", Assert.Single(page.All("h1")).TextContent);
        var card = Assert.Single(page.All("main ul[aria-label] > li"));
        Assert.Equal("Ocena: 4/5", card.QuerySelector("h2")!.TextContent);
        Assert.Equal("Anna Demowska", AdminHost.Fact(card, "Programista")); // a name, from Directory
        Assert.Equal("Wstrzymana przez asystenta", AdminHost.Fact(card, "Wstępna ocena asystenta"));
        Assert.Equal(["Dane osobowe", "Inny powód"], card.QuerySelectorAll("dd li").Select(l => l.TextContent)); // an unknown reason is "other", not "banana"
        Assert.DoesNotContain("banana", page.Html);
        Assert.DoesNotContain("model-demo", page.Html);
        Assert.Equal("24 lis 2026, 13:00", AdminHost.Fact(card, "Dodano"));
        Assert.Contains(card.QuerySelectorAll("button"), b => b.TextContent.Trim() == "Opublikuj");
        Assert.Contains(card.QuerySelectorAll("button"), b => b.TextContent.Trim() == "Odrzuć");
        Assert.Equal("Wstrzymane", page.TextOf(".segmented a.chip[aria-current=true]"));
    }

    [Fact]
    public async Task Review_text_is_text_never_markup()
    {
        await using var host = await HostAsync();
        host.Web.Reputation.Respond = _ => AdminHost.Paged(Review(text: "<img src=x onerror=alert(1)><script>alert(2)</script>"));
        var page = await host.Browser.GetAsync("/admin/opinie");
        Assert.Empty(page.All("main img"));
        Assert.Empty(page.All("main script"));
        Assert.Contains("<img src=x onerror=alert(1)>", AdminHost.Fact(Assert.Single(page.All("main ul[aria-label] > li")), "Treść opinii"));
    }

    [Fact]
    public async Task Publish_is_an_antiforgery_post_with_no_body_and_goes_back_with_a_message()
    {
        await using var host = await HostAsync();
        var page = await host.Browser.GetAsync("/admin/opinie");
        var done = await host.PostAsync(page, "/admin/opinie", $"publish-{ReviewId}");
        Assert.Equal(HttpStatusCode.Redirect, done.Status);
        Assert.Equal("/admin/opinie?status=Held&flash=admin-review-published", done.Location);
        var post = Assert.Single(host.Web.Reputation.Requests, r => r.Method == HttpMethod.Post);
        Assert.Equal($"/api/v1/admin/reviews/{ReviewId}/publish", post.PathAndQuery);
        Assert.Null(post.Body);
        Assert.Equal(PlAdmin.Flash.ReviewPublished, (await host.Browser.GetAsync(done.Location!)).TextOf("[data-testid=flash]"));
    }

    [Theory]
    [InlineData("za krótko", false)]
    [InlineData("         ", false)]
    [InlineData("Treść narusza zasady serwisu.", true)]
    public async Task Reject_needs_a_reason_of_10_to_500_characters_and_sends_it_trimmed(string reason, bool accepted)
    {
        await using var host = await HostAsync();
        var url = $"/admin/opinie?status=Held&odrzuc={ReviewId}";
        var open = await host.Browser.GetAsync(url);
        Assert.Contains("Odrzucenie usuwa opinię", open.Text);
        var answer = await host.PostAsync(open, url, $"reject-{ReviewId}", ("reason", $"  {reason}  "));
        if (accepted)
        {
            Assert.Equal(HttpStatusCode.Redirect, answer.Status);
            var sent = AdminHost.Body(Assert.Single(host.Web.Reputation.Requests, r => r.Method == HttpMethod.Post));
            Assert.Equal(reason, sent.GetProperty("reason").GetString());
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, answer.Status);
            Assert.Contains(PlAdmin.Reviews.ReasonInvalid, answer.Text);
            Assert.DoesNotContain(host.Web.Reputation.Requests, r => r.Method == HttpMethod.Post);
        }
    }

    [Fact]
    public async Task A_reason_of_exactly_501_characters_is_refused_and_500_is_accepted()
    {
        await using var host = await HostAsync();
        var url = $"/admin/opinie?status=Held&odrzuc={ReviewId}";
        var open = await host.Browser.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync(open, url, $"reject-{ReviewId}", ("reason", new string('x', 501)))).Status);
        Assert.Equal(HttpStatusCode.Redirect, (await host.PostAsync(open, url, $"reject-{ReviewId}", ("reason", new string('x', 500)))).Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "Stan opinii się zmienił")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "Serwer odrzucił powód")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Usługa opinii jest chwilowo niedostępna")]
    public async Task A_refusal_of_the_service_is_a_clear_message(HttpStatusCode status, string expected)
    {
        await using var host = await HostAsync();
        var ok = host.Web.Reputation.Respond;
        host.Web.Reputation.Respond = r => r.Method == HttpMethod.Post ? FakeBackend.Problem(status, "x_code") : ok(r);
        var page = await host.Browser.GetAsync("/admin/opinie");
        var answer = await host.PostAsync(page, "/admin/opinie", $"publish-{ReviewId}");
        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.Contains(expected, answer.Text);
        Assert.DoesNotContain("SERVER-TITLE", answer.Html);
    }

    [Fact]
    public async Task A_published_review_can_only_be_rejected_and_a_rejected_one_has_no_action()
    {
        await using var published = await HostAsync("Published");
        var card = Assert.Single((await published.Browser.GetAsync("/admin/opinie?status=Published")).All("main ul[aria-label] > li"));
        Assert.Equal(["Odrzuć"], card.QuerySelectorAll("button").Select(b => b.TextContent.Trim()));
        await using var rejected = await HostAsync("Rejected");
        Assert.Empty((await rejected.Browser.GetAsync("/admin/opinie?status=Rejected")).All("main button"));
    }

    [Fact]
    public async Task A_post_for_a_review_that_left_the_list_is_the_stale_message()
    {
        await using var host = await HostAsync();
        var page = await host.Browser.GetAsync("/admin/opinie");
        host.Web.Reputation.Respond = _ => AdminHost.Paged<AdminReviewDto>();
        var answer = await host.PostAsync(page, "/admin/opinie", $"publish-{ReviewId}");
        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.Contains(PlAdmin.Common.StaleForm, answer.Text);
        Assert.DoesNotContain(host.Web.Reputation.Requests, r => r.Method == HttpMethod.Post);
    }

    // ---- suspicious pairs
    [Fact]
    public async Task Suspicious_pairs_are_a_table_with_a_named_practitioner_and_a_short_client_reference()
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Reputation.Respond = _ => AdminHost.Paged("NEXT_cursor-1", new SuspiciousPairDto("pair-1", "demo-client-9", PractitionerId, 4, 4));
        host.Web.Directory.Respond = r => r.RequestUri!.AbsolutePath == $"/api/v1/admin/practitioners/{PractitionerId}" ? FakeBackend.Json(AdminHost.DetailOf(PractitionerId)) : new HttpResponseMessage(HttpStatusCode.NotFound);
        var page = await host.Browser.GetAsync("/admin/podejrzane");
        Assert.Equal("Podejrzane pary", Assert.Single(page.All("h1")).TextContent);
        Assert.Equal(["Klient", "Programista", "Opłacone rezerwacje", "Opinie (wszystkie 5/5)"], page.All("table thead th[scope=col]").Select(t => t.TextContent));
        var cells = page.All("table tbody tr td").Select(t => Page.Squash(t.TextContent)).ToList();
        Assert.Equal(["Klient demo-cli", "Anna Demowska", "4", "4"], cells);
        Assert.Equal("region", page.One(".table-wrap")!.GetAttribute("role"));
        Assert.Equal("/admin/podejrzane?cursor=NEXT_cursor-1", page.All("main a.btn").Single().GetAttribute("href"));
        Assert.DoesNotContain("demo-client-9", page.Text);
    }

    [Fact]
    public async Task Suspicious_pairs_without_a_name_fall_back_to_a_short_reference_and_an_outage_is_the_unavailable_alert()
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Reputation.Respond = _ => AdminHost.Paged(new SuspiciousPairDto("pair-1", "demo-client-9", PractitionerId, 3, 3));
        host.Web.Directory.Respond = _ => FakeBackend.Problem(HttpStatusCode.ServiceUnavailable, "dependency_unavailable");
        Assert.Contains("Programista aaaaaaaa", (await host.Browser.GetAsync("/admin/podejrzane")).Text);

        host.Web.Reputation.Respond = _ => FakeBackend.Problem(HttpStatusCode.ServiceUnavailable, "dependency_unavailable");
        var down = await host.Browser.GetAsync("/admin/podejrzane");
        Assert.Equal(HttpStatusCode.OK, down.Status);
        Assert.Equal(PlAdmin.Suspicious.Unavailable, down.TextOf("[data-testid=unavailable]"));
    }

    // ---- verification detail (J2: the history region)
    [Fact]
    public async Task The_detail_has_the_h1_the_data_and_a_history_region_with_one_listitem_per_event_the_J2_hooks()
    {
        await using var host = await AdminHost.StartAsync();
        var id = PractitionerId;
        host.Web.Directory.Respond = r => r.RequestUri!.AbsolutePath.EndsWith("/verification-events")
            ? FakeBackend.Json(new AdminVerificationEventsDto([new(Guid.NewGuid(), "Submitted", "demo-practitioner-1", null, new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero)), new(Guid.NewGuid(), "Approved", "demo-admin", "Profil kompletny, wszystko w porządku.", new DateTimeOffset(2026, 11, 22, 8, 0, 0, TimeSpan.Zero)), new(Guid.NewGuid(), "Surprise", "demo-admin", null, new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero))]))
            : FakeBackend.Json(AdminHost.DetailOf(id));
        var page = await host.Browser.GetAsync($"/admin/weryfikacja/{id}");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Anna Demowska", Assert.Single(page.All("h1")).TextContent);
        var region = page.All("section[aria-labelledby]").Single(r => r.GetAttribute("aria-labelledby") == "timeline-title");
        Assert.Equal("Historia weryfikacji", region.QuerySelector("#timeline-title")!.TextContent);
        var items = region.QuerySelectorAll("li").Select(l => Page.Squash(l.TextContent)).ToList();
        Assert.Equal(3, items.Count);
        Assert.Contains("Zgłoszono do weryfikacji", items[0]);
        Assert.Contains("Zatwierdzono", items[1]);
        Assert.Contains("Profil kompletny", items[1]);
        Assert.Contains("Inne zdarzenie", items[2]); // an unknown kind is never shown raw
        Assert.DoesNotContain("Surprise", page.Html);
        Assert.DoesNotContain(">demo-admin<", page.Html); // an actor is a short reference with the id in a title
        Assert.Equal("https://github.com/demo-dev", page.All("main a").First(a => a.TextContent == "demo-dev").GetAttribute("href"));
        Assert.Equal("/admin/weryfikacja", page.All("main a").First(a => a.TextContent == "Wróć do kolejki").GetAttribute("href"));
    }

    [Fact]
    public async Task A_failing_history_does_not_hide_the_profile_a_portfolio_that_is_not_http_is_text_and_an_unknown_profile_is_a_404()
    {
        await using var host = await AdminHost.StartAsync();
        var id = PractitionerId;
        host.Web.Directory.Respond = r => r.RequestUri!.AbsolutePath.EndsWith("/verification-events") ? FakeBackend.Problem(HttpStatusCode.ServiceUnavailable, "dependency_unavailable")
            : FakeBackend.Json(AdminHost.DetailOf(id) with { PortfolioUrl = "javascript:alert(1)" });
        var page = await host.Browser.GetAsync($"/admin/weryfikacja/{id}");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains(PlAdmin.Verification.TimelineUnavailable, page.Text);
        Assert.DoesNotContain(page.All("main a"), a => a.GetAttribute("href")?.StartsWith("javascript") == true);

        host.Web.Directory.Respond = _ => FakeBackend.Problem(HttpStatusCode.NotFound, "not_found");
        var missing = await host.Browser.GetAsync($"/admin/weryfikacja/{id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
        Assert.Equal("Nie znaleziono strony", missing.TextOf("h1"));
        Assert.Equal(HttpStatusCode.NotFound, (await host.Browser.GetAsync("/admin/weryfikacja/not-a-guid")).Status); // not a route at all
    }
}
