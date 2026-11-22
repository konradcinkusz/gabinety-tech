using System.Net;
using Gabinety.Web.Api;
using Gabinety.Web.Text;

namespace Gabinety.Web.Tests.AdminPages;

/// <summary>/admin/weryfikacja through the real host: routes, status codes, antiforgery, the J2 hooks, conflicts. Backends are fakes (BLAZOR-WEB-APP section 13).</summary>
public class AdminVerificationTests
{
    [Fact]
    public async Task The_queue_lists_the_submitted_profiles_with_the_hooks_of_journey_J2()
    {
        await using var host = await AdminHost.StartAsync();
        var anna = AdminHost.Row("Anna Demowska");
        host.Web.Directory.Respond = _ => AdminHost.Paged(anna, AdminHost.Row("Bartosz Demowski", github: false, nip: false));
        var page = await host.Browser.GetAsync("/admin/weryfikacja");

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Weryfikacja programistów", Assert.Single(page.All("h1")).TextContent);
        var rows = page.All("main ul[aria-label] > li");
        Assert.Equal(2, rows.Count);
        // J2: a listitem that has a link named exactly the display name, with a button named Zatwierdź in it
        var row = rows.First(r => r.QuerySelector("a")?.TextContent == "Anna Demowska");
        Assert.Equal($"/admin/weryfikacja/{anna.Id}", row.QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains(row.QuerySelectorAll("button"), b => b.TextContent.Contains("Zatwierdź"));
        Assert.Single(row.QuerySelectorAll("button"), b => b.TextContent.Trim() == "Odrzuć"); // reveal button of the reject step
        Assert.Single(row.QuerySelectorAll(".badge"), b => b.TextContent == "Czeka na weryfikację");
        Assert.Contains("24 lis 2026, 13:00", Page.Squash(row.TextContent)); // short date in Warsaw time, no seconds
        Assert.Contains("Weryfikacja programistów", page.TextOf("nav.tabs a.tabs__link[aria-current=page]"));
        Assert.Equal("Czeka na weryfikację\u00A0(2)", page.TextOf(".segmented a.chip[aria-current=true]")); // the chip of the status on screen counts the rows the page loaded
        var queue = host.Web.Directory.Requests.Single(r => r.PathAndQuery != "/api/v1/me");
        Assert.Equal("Bearer", queue.Authorization?.Split(' ')[0]); // the user's own token
        Assert.Contains("status=Submitted", queue.PathAndQuery);
    }

    [Fact]
    public async Task Approve_is_an_antiforgery_protected_post_and_goes_back_to_the_list_with_a_message()
    {
        await using var host = await AdminHost.StartAsync();
        var anna = AdminHost.Row("Anna Demowska");
        host.Web.Directory.Respond = r => r.Method == HttpMethod.Post ? AdminHost.NoContent() : AdminHost.Paged(anna);
        var page = await host.Browser.GetAsync("/admin/weryfikacja");

        var done = await host.PostAsync(page, "/admin/weryfikacja", $"approve-{anna.Id}");

        Assert.Equal(HttpStatusCode.Redirect, done.Status);
        Assert.Equal("/admin/weryfikacja?status=Submitted&flash=admin-approved", done.Location);
        var post = Assert.Single(host.Web.Directory.Requests, r => r.Method == HttpMethod.Post);
        Assert.Equal($"/api/v1/admin/practitioners/{anna.Id}/approve", post.PathAndQuery);
        Assert.NotNull(post.Authorization);
        Assert.Null(post.Body); // approve has no body
        var after = await host.Browser.GetAsync(done.Location!);
        Assert.Equal(PlAdmin.Flash.Approved, after.TextOf("[data-testid=flash]"));
    }

    [Fact]
    public async Task A_post_without_the_antiforgery_token_changes_nothing()
    {
        await using var host = await AdminHost.StartAsync();
        var anna = AdminHost.Row();
        host.Web.Directory.Respond = _ => AdminHost.Paged(anna);
        await host.Browser.GetAsync("/admin/weryfikacja");
        var forged = await host.PostRawAsync("/admin/weryfikacja", [KeyValuePair.Create("_handler", $"approve-{anna.Id}")]);
        Assert.Equal(HttpStatusCode.BadRequest, forged.Status);
        Assert.DoesNotContain(host.Web.Directory.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Reject_asks_for_the_reason_first_keeps_what_was_typed_when_it_is_too_short_and_sends_it_trimmed_when_it_is_fine()
    {
        await using var host = await AdminHost.StartAsync();
        var anna = AdminHost.Row();
        host.Web.Directory.Respond = r => r.Method == HttpMethod.Post ? AdminHost.NoContent() : AdminHost.Paged(anna);
        var open = await host.Browser.GetAsync($"/admin/weryfikacja?status=Submitted&odrzuc={anna.Id}");
        Assert.Equal(HttpStatusCode.OK, open.Status);
        Assert.Contains(open.All("button"), b => b.TextContent.Trim() == "Potwierdź odrzucenie");
        Assert.Contains("10 do 500", open.TextOf("label[for=reason]"));
        Assert.DoesNotContain(open.All("button"), b => b.TextContent.Trim() == "Zatwierdź"); // the reject step replaces the row's actions

        var tooShort = await host.PostAsync(open, $"/admin/weryfikacja?status=Submitted&odrzuc={anna.Id}", $"reject-{anna.Id}", ("reason", "za krótko"));
        Assert.Equal(HttpStatusCode.OK, tooShort.Status);
        Assert.Contains(PlAdmin.Verification.ReasonInvalid, tooShort.Text);
        Assert.Equal("za krótko", tooShort.One("textarea[name=reason]")!.TextContent.Trim()); // not lost
        Assert.Equal("true", tooShort.One("textarea[name=reason]")!.GetAttribute("aria-invalid"));
        Assert.DoesNotContain(host.Web.Directory.Requests, r => r.Method == HttpMethod.Post);

        var ok = await host.PostAsync(open, $"/admin/weryfikacja?status=Submitted&odrzuc={anna.Id}", $"reject-{anna.Id}", ("reason", "  Profil niekompletny, brak portfolio.  "));
        Assert.Equal(HttpStatusCode.Redirect, ok.Status);
        Assert.Contains("flash=admin-rejected", ok.Location);
        var post = Assert.Single(host.Web.Directory.Requests, r => r.Method == HttpMethod.Post);
        Assert.Equal($"/api/v1/admin/practitioners/{anna.Id}/reject", post.PathAndQuery);
        Assert.Equal("Profil niekompletny, brak portfolio.", AdminHost.Body(post).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_conflict_is_a_clear_message_and_the_list_is_read_again()
    {
        await using var host = await AdminHost.StartAsync();
        var anna = AdminHost.Row();
        var decided = false;
        host.Web.Directory.Respond = r =>
        {
            if (r.Method == HttpMethod.Post) { decided = true; return FakeBackend.Problem(HttpStatusCode.Conflict, "invalid_transition"); } // somebody else decided first
            return decided ? AdminHost.Paged<AdminPractitionerRowDto>() : AdminHost.Paged(anna);
        };
        var page = await host.Browser.GetAsync("/admin/weryfikacja");
        var conflict = await host.PostAsync(page, "/admin/weryfikacja", $"approve-{anna.Id}");
        Assert.Equal(HttpStatusCode.OK, conflict.Status);
        Assert.Contains(PlAdmin.Verification.StateChanged, conflict.Text);
        Assert.DoesNotContain("SERVER-TITLE", conflict.Html);
    }

    [Fact]
    public async Task A_post_for_a_row_that_is_gone_is_answered_with_the_list_and_a_message_not_an_error_page()
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Directory.Respond = _ => AdminHost.Paged<AdminPractitionerRowDto>();
        var answer = await host.PostRawAsync("/admin/weryfikacja", [KeyValuePair.Create("_handler", $"approve-{Guid.NewGuid()}"), .. await TokenAsync(host)]);
        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.Contains(PlAdmin.Common.StaleForm, answer.Text);
        Assert.DoesNotContain(host.Web.Directory.Requests, r => r.Method == HttpMethod.Post);
    }

    static async Task<IEnumerable<KeyValuePair<string, string>>> TokenAsync(AdminHost host)
    {
        // The antiforgery token of a page that has a form: the queue with one row.
        host.Web.Directory.Respond = _ => AdminHost.Paged(AdminHost.Row());
        var page = await host.Browser.GetAsync("/admin/weryfikacja");
        host.Web.Directory.Respond = _ => AdminHost.Paged<AdminPractitionerRowDto>();
        return page.All("form[method=post] input[name=__RequestVerificationToken]").Take(1).Select(i => KeyValuePair.Create("__RequestVerificationToken", i.GetAttribute("value")!));
    }
}
