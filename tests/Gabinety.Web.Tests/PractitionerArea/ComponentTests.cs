using Bunit;
using Gabinety.Web.Components.Pages.PractitionerArea;
using Microsoft.AspNetCore.Components;

namespace Gabinety.Web.Tests.PractitionerAreaTests;

/// <summary>The presentational components of the practitioner area, rendered with bUnit: the markup and the accessibility wiring the journeys and the audit rely on.</summary>
public class AreaComponentTests : BunitContext
{
    static RenderFragment<FormField.FieldContext> Input(string name = "x") => ctx => b =>
    {
        b.OpenElement(0, "input");
        b.AddAttribute(1, "id", ctx.Id);
        b.AddAttribute(2, "name", name);
        b.AddAttribute(3, "aria-describedby", ctx.DescribedBy);
        b.AddAttribute(4, "aria-invalid", ctx.Invalid ? "true" : null);
        b.CloseElement();
    };

    [Fact]
    public void A_field_links_label_hint_and_control_and_shows_no_error_when_there_is_none()
    {
        var cut = Render<FormField>(p => p.Add(f => f.Id, "headline").Add(f => f.Label, "Nagłówek").Add(f => f.Hint, "Wskazówka").Add(f => f.ChildContent, Input()));
        Assert.Equal("headline", cut.Find("label").GetAttribute("for"));
        Assert.Equal("headline-hint", cut.Find(".field__hint").Id);
        Assert.Equal("headline-hint", cut.Find("input").GetAttribute("aria-describedby"));
        Assert.Null(cut.Find("input").GetAttribute("aria-invalid"));
        Assert.Empty(cut.FindAll(".field__error"));
        Assert.DoesNotContain("field--invalid", cut.Find(".field").ClassName);
        Assert.Empty(cut.FindAll(".field__counter")); // no counter unless there is a maximum
    }

    [Fact]
    public void An_error_is_linked_marked_invalid_and_starts_with_a_hidden_word_for_screen_readers()
    {
        var cut = Render<FormField>(p => p.Add(f => f.Id, "nip").Add(f => f.Label, "NIP").Add(f => f.Hint, "10 cyfr").Add(f => f.Error, "Zły NIP").Add(f => f.ChildContent, Input()));
        Assert.Equal("nip-hint nip-error", cut.Find("input").GetAttribute("aria-describedby"));
        Assert.Equal("true", cut.Find("input").GetAttribute("aria-invalid"));
        Assert.Contains("field--invalid", cut.Find(".field").ClassName);
        var error = cut.Find("#nip-error");
        Assert.Equal("Błąd: Zły NIP", error.TextContent.Trim());
        Assert.Equal("visually-hidden", error.QuerySelector("span")!.ClassName);
    }

    [Fact]
    public void The_counter_sits_inside_the_field_after_the_control_and_is_hidden_from_screen_readers()
    {
        var cut = Render<FormField>(p => p.Add(f => f.Id, "h").Add(f => f.Label, "L").Add(f => f.Max, 120).Add(f => f.Length, 17).Add(f => f.ChildContent, Input()));
        var counter = cut.Find(".field .field__counter");
        Assert.Equal("17 / 120", counter.TextContent);
        Assert.Equal("true", counter.GetAttribute("aria-hidden"));
        Assert.Equal(["label", "input", "p"], cut.Find(".field").Children.Select(e => e.LocalName)); // the counter is right after the control
    }

    [Fact]
    public void An_optional_field_says_so_and_a_required_one_does_not()
    {
        Assert.Contains("(opcjonalnie)", Render<FormField>(p => p.Add(f => f.Id, "a").Add(f => f.Label, "Link").Add(f => f.Optional, true).Add(f => f.ChildContent, Input())).Find("label").TextContent);
        Assert.DoesNotContain("opcjonalnie", Render<FormField>(p => p.Add(f => f.Id, "a").Add(f => f.Label, "Link").Add(f => f.ChildContent, Input())).Find("label").TextContent);
    }

    [Fact]
    public void The_summary_is_an_alert_that_counts_and_links_every_error_and_is_absent_without_errors()
    {
        Assert.Empty(Render<ErrorSummary>(p => p.Add(s => s.Errors, [])).Markup.Trim());
        var cut = Render<ErrorSummary>(p => p.Add(s => s.Errors, [KeyValuePair.Create("title", "Zły tytuł"), KeyValuePair.Create("price", "Zła cena")]));
        var box = cut.Find("[role=alert]");
        Assert.Equal("Popraw 2 pola", box.QuerySelector("h2")!.TextContent);
        Assert.Equal([("#title", "Zły tytuł"), ("#price", "Zła cena")], box.QuerySelectorAll("a").Select(a => (a.GetAttribute("href")!, a.TextContent)));
    }

    [Theory]
    [InlineData(1, "pole")] [InlineData(2, "pola")] [InlineData(4, "pola")] [InlineData(5, "pól")] [InlineData(11, "pól")] [InlineData(12, "pól")] [InlineData(14, "pól")] [InlineData(21, "pól")] [InlineData(22, "pola")] [InlineData(25, "pól")]
    public void The_polish_plural_of_pole(int n, string word) => Assert.Equal(word, ErrorSummary.Noun(n));

    [Theory]
    [InlineData("success")] [InlineData("warning")] [InlineData("danger")] [InlineData("neutral")] [InlineData("info")]
    public void A_badge_is_text_with_its_kind_as_a_class_and_the_label_is_a_real_text_node(string kind)
    {
        var cut = Render<KindBadge>(p => p.Add(b => b.Label, "Gotowe").Add(b => b.Kind, kind).Add(b => b.TestId, "x"));
        var span = cut.Find("span");
        Assert.Equal(($"badge badge--{kind}", "Gotowe", "x"), (span.ClassName, span.TextContent, span.GetAttribute("data-testid")));
        Assert.Empty(span.Children); // a text node, not an element or CSS content (contract 9.6)
    }

    [Theory]
    [InlineData(PaymentsState.Ready, "badge badge--success", "Gotowe")]
    [InlineData(PaymentsState.Pending, "badge badge--warning", "Wymaga uzupełnienia")]
    [InlineData(PaymentsState.None, "badge badge--neutral", "Nie rozpoczęto")]
    public void The_payments_status_badge_has_the_kind_and_the_label_for_each_state(PaymentsState state, string className, string label)
    {
        var cut = Render<KindBadge>(p => p.Add(b => b.Label, PaymentsView.Label(state)).Add(b => b.Kind, PaymentsView.Kind(state)).Add(b => b.TestId, "payments-status"));
        var badge = cut.Find("[data-testid=payments-status]");
        Assert.Equal((className, label), (badge.ClassName, badge.TextContent));
    }

    [Fact]
    public void The_tabs_mark_only_the_current_section_and_a_query_string_does_not_change_it()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/gabinet-programisty/oferty?edit=1");
        var cut = Render<PractitionerLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "treść"))));
        Assert.Equal(["Oferty"], cut.FindAll("a[aria-current=page]").Select(a => a.TextContent));
        Assert.Equal(["/gabinet-programisty", "/gabinet-programisty/profil", "/gabinet-programisty/oferty", "/gabinet-programisty/dostepnosc", "/gabinet-programisty/platnosci"], cut.FindAll("nav.tabs a").Select(a => a.GetAttribute("href")));
        Assert.Contains("treść", cut.Markup);
    }

    [Fact]
    public void Start_is_current_on_the_dashboard_only_not_on_every_page_under_it()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/gabinet-programisty/");
        var cut = Render<PractitionerLayout>(p => p.Add(l => l.Body, (RenderFragment)(_ => { })));
        Assert.Equal(["Start"], cut.FindAll("a[aria-current=page]").Select(a => a.TextContent));
    }
}
