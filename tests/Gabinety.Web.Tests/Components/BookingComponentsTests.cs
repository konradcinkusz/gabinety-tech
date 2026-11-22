using Bunit;
using Gabinety.Web.Components.Shared;
using Gabinety.Web.Components.Pages.Booking;
using Gabinety.Web.Components.Pages.Bookings;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Web.Tests.Rendering;

/// <summary>A clock and timers the test drives by hand: no waiting, no real timer.</summary>
sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public List<ManualTimer> Timers { get; } = [];
    public override DateTimeOffset GetUtcNow() => Now;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(() => callback(state));
        Timers.Add(timer);
        return timer;
    }
    public void Tick(TimeSpan by) { Now += by; foreach (var t in Timers.Where(t => !t.Disposed).ToList()) t.Fire(); }
}

sealed class ManualTimer(Action fire) : ITimer
{
    public bool Disposed { get; private set; }
    public void Fire() => fire();
    public bool Change(TimeSpan dueTime, TimeSpan period) => true;
    public void Dispose() => Disposed = true;
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}

public class HoldTimerTests : BunitContext
{
    static readonly DateTimeOffset T0 = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);
    readonly ManualTime _time = new(T0);

    public HoldTimerTests() => Services.AddSingleton<TimeProvider>(_time);

    IRenderedComponent<HoldTimer> Show(TimeSpan left) => Render<HoldTimer>(p => p.Add(t => t.ExpiresAt, T0 + left));

    [Fact]
    public void It_is_a_timer_role_warning_with_a_label_and_the_time_left_and_says_nothing_aloud_while_there_is_plenty()
    {
        var cut = Show(TimeSpan.FromSeconds(598));
        var timer = cut.Find("[role=timer]");
        Assert.Equal("Czas na płatność", timer.GetAttribute("aria-label"));
        Assert.Contains("callout--warning", timer.ClassName);
        Assert.Equal("Termin jest wstrzymany jeszcze przez 9:58", timer.TextContent);
        Assert.Equal("", cut.Find("[data-testid=hold-announcement]").TextContent);
        Assert.Equal("status", cut.Find("[data-testid=hold-announcement]").GetAttribute("role"));
    }

    [Fact]
    public void It_starts_no_timer_until_it_has_rendered_so_a_prerender_costs_nothing_and_ticks_once_a_second_after()
    {
        var cut = Show(TimeSpan.FromMinutes(10));
        Assert.Single(_time.Timers); // OnAfterRender: the circuit's first render; prerendering never calls it
        _time.Tick(TimeSpan.FromSeconds(1));
        cut.WaitForAssertion(() => Assert.Equal("Termin jest wstrzymany jeszcze przez 9:59", cut.Find("[role=timer]").TextContent));
    }

    [Fact]
    public void The_announcement_changes_only_at_five_minutes_one_minute_and_expiry_never_every_second()
    {
        var cut = Show(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(3));
        var spoken = new List<string>();
        for (var i = 0; i < 400; i++)
        {
            _time.Tick(TimeSpan.FromSeconds(1));
            var text = cut.Find("[data-testid=hold-announcement]").TextContent;
            if (spoken.Count == 0 || spoken[^1] != text) spoken.Add(text);
        }
        Assert.Equal(["", "Zostało 5 minut na płatność.", "Została 1 minuta na płatność.", "Czas na płatność minął, termin został zwolniony. Wybierz termin ponownie."], spoken);
    }

    [Fact]
    public void At_zero_the_clock_stops_and_the_expiry_replaces_the_countdown()
    {
        var cut = Show(TimeSpan.FromSeconds(2));
        _time.Tick(TimeSpan.FromSeconds(1));
        _time.Tick(TimeSpan.FromSeconds(1));
        cut.WaitForAssertion(() => Assert.Equal("Czas na płatność minął, termin został zwolniony. Wybierz termin ponownie.", cut.Find("[role=timer]").TextContent));
        Assert.True(_time.Timers.Single().Disposed);
        Assert.DoesNotContain("0:00", cut.Markup);
    }

    [Fact]
    public void An_expired_hold_renders_expired_at_once_and_a_disposed_component_stops_its_timer()
    {
        Assert.Contains("Czas na płatność minął", Show(TimeSpan.FromSeconds(-5)).Markup);
        var live = Show(TimeSpan.FromMinutes(3));
        live.Instance.Dispose();
        Assert.True(_time.Timers.Last().Disposed);
    }
}

public class BookingComponentsTests : BunitContext
{
    [Fact]
    public void The_text_area_is_one_unit_label_hint_control_counter_and_error_linked_together()
    {
        var cut = Render<CounterTextArea>(p => p.Add(c => c.Id, "brief").Add(c => c.Name, "Model.Brief").Add(c => c.Label, "Opisz problem").Add(c => c.Hint, "Od 20 do 2000 znaków.")
            .Add(c => c.Value, "  abc  ").Add(c => c.CounterFormat, "{0} / 2000").Add(c => c.Error, "Za krótki.").Add(c => c.Autofocus, true));
        var area = cut.Find("textarea");
        Assert.Equal(("brief", "Model.Brief", "true", "brief-hint brief-error"), (area.Id, area.GetAttribute("name")!, area.GetAttribute("aria-invalid")!, area.GetAttribute("aria-describedby")!));
        Assert.True(area.HasAttribute("autofocus"));
        Assert.Equal("Opisz problem", cut.Find("label[for=brief]").TextContent);
        Assert.Equal("Od 20 do 2000 znaków.", cut.Find("#brief-hint").TextContent);
        Assert.Equal("3 / 2000", cut.Find(".field__counter").TextContent); // counted after trimming, as the rule is
        Assert.Equal("Błąd: Za krótki.", cut.Find("#brief-error").TextContent);
        Assert.Contains("field--invalid", cut.Find(".field").ClassName);
        Assert.True(cut.Find("#brief-error").Closest(".field") is not null); // the error is inside the field
    }

    [Fact]
    public void Without_an_error_the_control_is_valid_and_describes_only_its_hint_and_optional_says_so()
    {
        var cut = Render<CounterTextArea>(p => p.Add(c => c.Id, "reason").Add(c => c.Name, "Model.Reason").Add(c => c.Label, "Powód").Add(c => c.Optional, true));
        var area = cut.Find("textarea");
        Assert.Null(area.GetAttribute("aria-invalid"));
        Assert.Null(area.GetAttribute("aria-describedby"));
        Assert.False(area.HasAttribute("autofocus"));
        Assert.Empty(cut.FindAll(".field__error"));
        Assert.Empty(cut.FindAll(".field__counter"));
        Assert.Contains("opcjonalnie", cut.Find("label").TextContent);
        Assert.DoesNotContain("wymagane", cut.Markup);
    }

    [Fact]
    public void A_value_with_markup_is_text()
    {
        var cut = Render<CounterTextArea>(p => p.Add(c => c.Id, "t").Add(c => c.Name, "n").Add(c => c.Label, "l").Add(c => c.Value, "</textarea><script>alert(1)</script>"));
        Assert.Empty(cut.FindAll("script"));
        Assert.Equal("</textarea><script>alert(1)</script>", cut.Find("textarea").TextContent);
    }

    [Fact]
    public void The_form_summary_lists_every_problem_as_a_link_and_takes_focus_only_when_there_are_several()
    {
        FormProblem[] two = [new("brief", "Opis jest za krótki."), new("rating-1", "Wybierz ocenę.")];
        var cut = Render<BookingFormProblems>(p => p.Add(c => c.Items, two));
        var summary = cut.Find(".form-summary");
        Assert.Equal(("alert", "Popraw 2 pola"), (summary.GetAttribute("role")!, cut.Find("h2").TextContent));
        Assert.Equal([("#brief", "Opis jest za krótki."), ("#rating-1", "Wybierz ocenę.")], cut.FindAll("a").Select(a => (a.GetAttribute("href")!, a.TextContent)));
        Assert.True(summary.HasAttribute("autofocus"));
        Assert.Equal("-1", summary.GetAttribute("tabindex"));

        var one = Render<BookingFormProblems>(p => p.Add(c => c.Items, two[..1]));
        Assert.False(one.Find(".form-summary").HasAttribute("autofocus")); // one problem: the control itself takes focus
        Assert.Empty(Render<BookingFormProblems>(p => p.Add(c => c.Items, [])).FindAll(".form-summary"));
    }

    [Theory]
    [InlineData(1, "Popraw 1 pole")] [InlineData(2, "Popraw 2 pola")] [InlineData(4, "Popraw 4 pola")] [InlineData(5, "Popraw 5 pól")] [InlineData(11, "Popraw 11 pól")] [InlineData(12, "Popraw 12 pól")]
    [InlineData(14, "Popraw 14 pól")] [InlineData(22, "Popraw 22 pola")] [InlineData(25, "Popraw 25 pól")]
    public void The_summary_heading_agrees_with_the_number_in_polish(int n, string heading) => Assert.Equal(heading, FormProblemText.Heading(n));

    [Fact]
    public void The_summary_shows_the_party_only_when_there_is_one_and_the_total_in_the_money_format()
    {
        var with = Render<ConsultationSummary>(p => p.Add(c => c.Offering, "Audyt").Add(c => c.Party, "Anna").Add(c => c.When, "wtorek, 8 grudnia, 12:00–13:00").Add(c => c.Minutes, 60).Add(c => c.PriceMinor, 18000)
            .AddChildContent("<div class=\"callout--info\">Wstrzymany</div>"));
        Assert.Equal(["Konsultacja", "Programista", "Termin", "Czas trwania", "Do zapłaty"], with.FindAll("dt").Select(d => d.TextContent));
        Assert.Equal("180,00 zł", with.Find("dd.booking-summary__total").TextContent);
        Assert.Contains("num", with.Find("dd.booking-summary__total").ClassName);
        Assert.NotNull(with.Find(".callout--info")); // the hold notice sits in the summary
        var without = Render<ConsultationSummary>(p => p.Add(c => c.Offering, "Audyt").Add(c => c.When, "x").Add(c => c.PriceMinor, 100));
        Assert.Equal(["Konsultacja", "Termin", "Do zapłaty"], without.FindAll("dt").Select(d => d.TextContent));
        Assert.Equal("Podsumowanie", without.Find("h2").TextContent);
        Assert.Equal(without.Find("h2").Id, without.Find("section").GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void A_badge_is_a_real_text_node_with_the_class_of_its_kind()
    {
        var cut = Render<BookingBadge>(p => p.Add(c => c.Badge, Badge.ForBooking("Disputed")));
        Assert.Equal("Spór w toku", cut.Find("span").TextContent);
        Assert.Equal("badge badge--warning", cut.Find("span").ClassName);
        Assert.Empty(cut.Find("span").Children); // text, not CSS content: the journeys read it with getByText
    }
}
