using Bunit;
using Bunit.TestDoubles;
using Gabinety.Web.Components.Admin;
using Gabinety.Web.Components.Layout;
using Gabinety.Web.Components.Pages.Admin;
using Gabinety.Web.Components.Shared;
using Gabinety.Web.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Web.Tests.Rendering;

/// <summary>The presentational pieces of the admin area with bUnit: the names and roles the journeys and the audit rely on, with no host.</summary>
public class AdminComponentTests : BunitContext
{
    [Theory]
    [InlineData(BadgeKind.Success, "badge--success")]
    [InlineData(BadgeKind.Warning, "badge--warning")]
    [InlineData(BadgeKind.Danger, "badge--danger")]
    [InlineData(BadgeKind.Info, "badge--info")]
    [InlineData(BadgeKind.Neutral, "badge--neutral")]
    public void A_badge_is_a_text_node_with_the_class_of_its_kind(BadgeKind kind, string css)
    {
        var cut = Render<AdminBadge>(p => p.Add(c => c.Kind, kind).AddChildContent("Zatwierdzony"));
        Assert.Equal("Zatwierdzony", cut.Find("span").TextContent); // J2 reads it as a real text node, exactly
        Assert.Contains(css, cut.Find("span").ClassName);
    }

    [Fact]
    public void Segmented_links_name_their_navigation_and_mark_only_the_current_one()
    {
        var cut = Render<SegmentedLinks>(p => p.Add(c => c.Label, "Status").Add(c => c.Items, [new("Otwarte", "/a?status=open", true), new("Rozstrzygnięte", "/a?status=resolved", false)]));
        Assert.Equal("Status", cut.Find("nav").GetAttribute("aria-label"));
        Assert.Equal(["true", null], cut.FindAll("a").Select(a => a.GetAttribute("aria-current")));
        Assert.Equal(["/a?status=open", "/a?status=resolved"], cut.FindAll("a").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public void The_reason_form_is_a_labelled_antiforgery_post_that_keeps_the_text_and_links_the_error_to_the_field()
    {
        Services.AddSingleton<Microsoft.AspNetCore.Components.Forms.AntiforgeryStateProvider, FakeAntiforgery>();
        var cut = Render<ReasonForm>(p => p
            .Add(c => c.FormName, "reject-x").Add(c => c.Label, "Powód (10 do 500 znaków)").Add(c => c.Hint, "Podpowiedź").Add(c => c.ConfirmText, "Potwierdź odrzucenie").Add(c => c.CancelHref, "/lista")
            .Add(c => c.Value, "za krótko").Add(c => c.Error, "Podaj powód").Add(c => c.Warning, "Uwaga"));
        Assert.Equal("post", cut.Find("form").GetAttribute("method"));
        var area = cut.Find("textarea");
        Assert.Equal("reason", area.GetAttribute("name"));
        Assert.Equal("za krótko", area.TextContent);
        Assert.Equal("500", area.GetAttribute("maxlength"));
        Assert.Equal("true", area.GetAttribute("aria-invalid"));
        Assert.Equal(["reason-hint", "reason-error"], area.GetAttribute("aria-describedby")!.Split(' '));
        Assert.NotNull(cut.Find("#reason-error"));
        Assert.Equal("reason", cut.Find("label").GetAttribute("for"));
        Assert.Equal("Potwierdź odrzucenie", cut.Find("button[type=submit]").TextContent);
        Assert.Equal("/lista", cut.Find("a.btn").GetAttribute("href"));
        Assert.Contains("Uwaga", cut.Find("[role=status]").TextContent);
        Assert.Contains("Błąd:", cut.Find("#reason-error .visually-hidden").TextContent); // the error never relies on colour alone
    }

    [Fact]
    public void The_reason_form_without_an_error_is_not_marked_invalid()
    {
        Services.AddSingleton<Microsoft.AspNetCore.Components.Forms.AntiforgeryStateProvider, FakeAntiforgery>();
        var cut = Render<ReasonForm>(p => p.Add(c => c.FormName, "n").Add(c => c.Label, "L").Add(c => c.Hint, "H").Add(c => c.ConfirmText, "C").Add(c => c.CancelHref, "/"));
        Assert.Null(cut.Find("textarea").GetAttribute("aria-invalid"));
        Assert.Empty(cut.FindAll(".field__error"));
        Assert.DoesNotContain("reason-error", cut.Find("textarea").GetAttribute("aria-describedby"));
    }

    [Fact]
    public void A_reveal_button_is_a_get_form_that_keeps_the_view_and_names_the_row()
    {
        var cut = Render<RevealButton>(p => p.Add(c => c.Action, "/admin/spory").Add(c => c.Name, "rozstrzygnij").Add(c => c.Value, "abc")
            .Add(c => c.Keep, [new("status", "open"), new("cursor", "C1")]).AddChildContent("Rozstrzygnij"));
        Assert.Equal("get", cut.Find("form").GetAttribute("method"));
        Assert.Equal("/admin/spory", cut.Find("form").GetAttribute("action"));
        Assert.Equal([("status", "open"), ("cursor", "C1"), ("rozstrzygnij", "abc")], cut.FindAll("input[type=hidden]").Select(i => (i.GetAttribute("name")!, i.GetAttribute("value")!)));
        Assert.Equal("Rozstrzygnij", cut.Find("button").TextContent); // J6: a button, named so
        Assert.DoesNotContain("__RequestVerificationToken", cut.Markup); // a GET carries no token
    }

    [Theory]
    [InlineData("/admin/spory", "Spory")]
    [InlineData("/admin/weryfikacja/0b9e1d2c-aaaa-bbbb-cccc-1234567890ab", "Weryfikacja programistów")]
    [InlineData("/admin/ksiega", "Księga")]
    public void The_admin_layout_marks_the_section_of_the_current_address_and_nothing_else(string path, string current)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(path);
        var cut = Render<AdminLayout>(p => p.Add(c => c.Body, (RenderFragment)(b => b.AddContent(0, "tresc"))));
        Assert.Equal("Sekcje administracji", cut.Find("nav").GetAttribute("aria-label"));
        Assert.Equal([current], cut.FindAll("a[aria-current=page]").Select(a => a.TextContent));
        Assert.Equal(5, cut.FindAll("nav a").Count);
        Assert.Contains("tresc", cut.Markup);
        Assert.Equal("Administracja", cut.Find(".admin-strip").TextContent);
    }

    [Fact]
    public void The_forbidden_notice_has_the_hook_one_h1_the_alert_role_and_the_way_home()
    {
        var cut = Render<ForbiddenNotice>();
        Assert.Equal("alert", cut.Find("[data-testid=forbidden]").GetAttribute("role"));
        Assert.Equal("Brak dostępu", Assert.Single(cut.FindAll("h1")).TextContent);
        Assert.Equal("/", cut.Find("a").GetAttribute("href"));
    }

    [Fact]
    public void A_failure_alert_says_forbidden_signed_out_or_unavailable_by_status_and_sets_403_on_the_response()
    {
        var http = new DefaultHttpContext();
        Services.AddSingleton<Microsoft.AspNetCore.Components.Forms.AntiforgeryStateProvider, FakeAntiforgery>();
        var forbidden = Render<CascadingValue<HttpContext>>(p => p.Add(c => c.Value, http).AddChildContent<FailureAlert>(c => c.Add(f => f.Failure, new Api.ApiFailure(403, "forbidden")).Add(f => f.Unavailable, "Niedostępne")));
        Assert.Equal(PlAdmin.Forbidden.Body, forbidden.Find("[data-testid=forbidden]").TextContent);
        Assert.Equal(403, http.Response.StatusCode);

        var outage = Render<FailureAlert>(p => p.Add(f => f.Failure, new Api.ApiFailure(0, "unavailable")).Add(f => f.Unavailable, "Niedostępne"));
        Assert.Equal("Niedostępne", outage.Find("[data-testid=unavailable]").TextContent);
        Assert.Equal("alert", outage.Find("[data-testid=unavailable]").GetAttribute("role"));

        var signedOut = Render<FailureAlert>(p => p.Add(f => f.Failure, new Api.ApiFailure(401, "unauthorized")).Add(f => f.Unavailable, "Niedostępne"));
        Assert.Contains("Sesja wygasła", signedOut.Markup);
        Assert.StartsWith("/logowanie", signedOut.Find("a").GetAttribute("href"));
    }

    sealed class FakeAntiforgery : Microsoft.AspNetCore.Components.Forms.AntiforgeryStateProvider
    {
        public override Microsoft.AspNetCore.Components.Forms.AntiforgeryRequestToken? GetAntiforgeryToken() => new("test-token", "__RequestVerificationToken");
    }
}
