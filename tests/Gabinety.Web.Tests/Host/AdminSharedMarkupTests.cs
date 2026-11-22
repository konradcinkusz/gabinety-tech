using System.Net;
using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Admin;

namespace Gabinety.Web.Tests.AdminPages;

/// <summary>UX-050: the administrator area uses the shared vocabulary of app.css (tabs, segmented chips, option cards, row actions) and its counts never fail a page.</summary>
public class AdminSharedMarkupTests
{
    static readonly Guid BookingId = Guid.Parse("0b9e1d2c-7a5f-4c3e-9b1a-2d4e6f8a0b1c");

    [Fact]
    public async Task The_section_tabs_are_the_shared_tabs_and_carry_no_count_so_their_names_stay_the_labels()
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Booking.Respond = _ => AdminHost.Paged<AdminDisputeDto>();
        var page = await host.Browser.GetAsync("/admin/spory");

        var nav = page.One("nav.tabs[aria-label='Sekcje administracji']")!;
        Assert.Equal(5, nav.QuerySelectorAll("a.tabs__link").Length);
        Assert.Equal(["Spory"], nav.QuerySelectorAll("a[aria-current=page]").Select(a => a.TextContent));
        Assert.DoesNotContain("(", nav.TextContent); // a count would be one more backend call on every admin page
        Assert.Empty(page.All("[class*=admin-tabs], [class*=admin-segmented], [class*=admin-option], [class*=admin-table]"));
    }

    [Fact]
    public async Task The_status_switch_is_the_shared_segmented_of_chips_and_only_the_current_one_is_counted()
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Booking.Respond = r => r.RequestUri!.AbsolutePath == "/api/v1/admin/disputes" ? AdminHost.Paged(AdminHost.Dispute(BookingId), AdminHost.Dispute()) : new HttpResponseMessage(HttpStatusCode.NotFound);
        var page = await host.Browser.GetAsync("/admin/spory?status=open");

        var chips = page.All("nav[aria-label=Status] ul.segmented a.chip");
        Assert.Equal(["Otwarte (2)", "Rozstrzygnięte"], chips.Select(c => c.TextContent));
        Assert.Equal(["true", null], chips.Select(c => c.GetAttribute("aria-current")));
        Assert.StartsWith("Otwarte", chips[0].TextContent); // the accessible name starts with the label
    }

    [Fact]
    public async Task A_page_with_a_next_page_counts_with_a_plus_and_a_later_page_shows_no_count()
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Directory.Respond = r => AdminHost.Paged("abc_DEF-1", AdminHost.Row("Anna Demowska"), AdminHost.Row("Bartosz Demowski"));
        var first = await host.Browser.GetAsync("/admin/weryfikacja");
        Assert.Equal("Czeka na weryfikację (2+)", first.TextOf(".segmented a.chip[aria-current=true]"));

        var later = await host.Browser.GetAsync("/admin/weryfikacja?cursor=abc_DEF-1");
        Assert.Equal("Czeka na weryfikację", later.TextOf(".segmented a.chip[aria-current=true]")); // a page of the list is not the size of the queue
    }

    [Fact]
    public async Task A_failing_service_leaves_the_chips_without_a_count_and_the_page_renders()
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Booking.Respond = _ => FakeBackend.Problem(HttpStatusCode.ServiceUnavailable, "dependency_unavailable");
        var page = await host.Browser.GetAsync("/admin/spory");

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(["Otwarte", "Rozstrzygnięte"], page.All(".segmented a.chip").Select(c => c.TextContent));
        Assert.DoesNotContain("(", page.TextOf("nav[aria-label=Status]"));
        Assert.Single(page.All("h1"));
    }

    [Fact]
    public async Task The_verification_rows_stay_cards_in_a_list_with_row_actions_and_badges()
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Directory.Respond = _ => AdminHost.Paged(AdminHost.Row("Anna Demowska"));
        var page = await host.Browser.GetAsync("/admin/weryfikacja");

        var row = Assert.Single(page.All("main ul[aria-label] > li.card")); // journeys address a listitem, so the rows are not table rows
        var actions = row.QuerySelector(".row-actions")!;
        Assert.Contains("btn--primary", actions.QuerySelector("button[type=submit]")!.ClassName);
        Assert.Equal("Zatwierdź", actions.QuerySelector("button.btn--primary")!.TextContent.Trim());
        Assert.Equal("Odrzuć", actions.QuerySelector("button.btn--ghost-danger")!.TextContent.Trim());
        Assert.NotEmpty(row.QuerySelectorAll(".badge"));
        Assert.Equal(["h1", "h2"], page.All("main h1, main h2").Select(h => h.TagName.ToLowerInvariant()).Distinct()); // no heading level skipped
    }

    [Fact]
    public async Task The_resolve_form_offers_its_three_options_as_option_cards_in_an_option_group_and_the_booking_id_is_text_not_a_link()
    {
        await using var host = await AdminHost.StartAsync();
        var dispute = AdminHost.Dispute(BookingId);
        host.Web.Booking.Respond = r => r.RequestUri!.AbsolutePath == "/api/v1/admin/disputes" ? AdminHost.Paged(dispute) : FakeBackend.Json(AdminHost.BookingOf(BookingId));
        var page = await host.Browser.GetAsync($"/admin/spory?status=open&rozstrzygnij={BookingId}");

        var card = Assert.Single(page.All("main ul[aria-label] > li.card"));
        var group = card.QuerySelector("fieldset.option-group")!;
        Assert.Equal(3, group.QuerySelectorAll("label.option input[type=radio]").Length);
        Assert.NotNull(card.QuerySelector(".admin-amount")); // the amount field rule stays in CSS
        Assert.Contains(BookingId.ToString(), card.TextContent);
        Assert.DoesNotContain(card.QuerySelectorAll("a"), a => (a.GetAttribute("href") ?? "").Contains(BookingId.ToString())); // no admin-readable booking page exists to link to
    }

    [Theory]
    [InlineData(true, null, null, 3, "3")]
    [InlineData(true, null, "next", 3, "3+")]
    [InlineData(true, "cursor", null, 3, null)]
    [InlineData(false, null, null, 0, null)]
    public void The_chip_count_is_exact_only_for_a_loaded_first_page(bool loaded, string? cursor, string? next, int count, string? expected) =>
        Assert.Equal(expected, AdminQuery.ChipCount(loaded, cursor, next, count));
}
