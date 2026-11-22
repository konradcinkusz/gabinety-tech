using Bunit;
using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Gabinet;
using Gabinety.Web.Components.Pages.Shared;

namespace Gabinety.Web.Tests.Public;

/// <summary>UX-015: day strip, nearest-time line, time-of-day groups and week paging of the slot picker. Warsaw is UTC+1 in November.</summary>
public class SlotPickerLayoutTests : BunitContext
{
    static readonly DateOnly Today = new(2026, 11, 23);
    static readonly Guid Offer = Guid.Parse("018f0000-0000-7000-8000-0000000000b1");
    static readonly OfferingDto Offering = new(Offer, "Audyt", "", 60, new MoneyDto(18000, "PLN"), "x", []);

    /// <summary>A slot at the given Warsaw wall-clock time (winter: UTC+1).</summary>
    static SlotDto At(int day, int hour, int minute = 0)
    {
        var utc = new DateTimeOffset(2026, 11, day, hour, minute, 0, TimeSpan.Zero).AddHours(-1);
        return new SlotDto(utc, utc.AddHours(1));
    }

    IRenderedComponent<SlotPicker> Render(IEnumerable<SlotDto> slots, DateOnly? selected = null) =>
        Render<SlotPicker>(p => p
            .Add(c => c.Slug, "demo-anna").Add(c => c.Offering, Offering)
            .Add(c => c.Slots, new OfferSlots(true, SlotPickerModel.GroupByDay(slots))).Add(c => c.SelectedDay, selected).Add(c => c.Today, Today));

    [Fact]
    public void Parts_of_the_day_break_at_12_00_and_17_00()
    {
        var groups = SlotPickerModel.GroupByPartOfDay([At(24, 11, 59), At(24, 12), At(24, 16, 59), At(24, 17), At(24, 8)]);
        Assert.Equal([SlotPickerModel.TimeOfDay.Morning, SlotPickerModel.TimeOfDay.Afternoon, SlotPickerModel.TimeOfDay.Evening], groups.Select(g => g.Part));
        Assert.Equal(["08:00", "11:59"], groups[0].Slots.Select(s => PlFormat.Time(s.StartUtc)));
        Assert.Equal(["12:00", "16:59"], groups[1].Slots.Select(s => PlFormat.Time(s.StartUtc)));
        Assert.Equal(["17:00"], groups[2].Slots.Select(s => PlFormat.Time(s.StartUtc)));
    }

    [Fact]
    public void Empty_groups_are_omitted_and_headings_follow_the_times_present()
    {
        var cut = Render([At(24, 9), At(24, 18)]);
        Assert.Equal(["Rano", "Wieczorem"], cut.FindAll(".slot-part__title").Select(h => h.TextContent));
        Assert.Equal(["09:00", "18:00"], cut.FindAll("a.slot").Select(a => a.TextContent));
        Assert.All(cut.FindAll("a.slot"), a => Assert.Matches(@"^\d{2}:\d{2}$", a.TextContent));
    }

    [Fact]
    public void Each_time_carries_its_utc_instant_and_a_hidden_local_time_note_waits_for_the_script()
    {
        // UX-062: enhance.js reads data-utc and fills the note only for a visitor outside Warsaw; the link text stays the Warsaw HH:MM
        var cut = Render([At(24, 9)]);
        Assert.Equal("2026-11-24T08:00:00Z", cut.Find("a.slot").GetAttribute("data-utc"));
        Assert.Equal("09:00", cut.Find("a.slot").TextContent);
        var note = cut.Find("[data-enhance=local-time-note]");
        Assert.True(note.HasAttribute("hidden"));
        Assert.Contains("{zone}", note.GetAttribute("data-template"));
        Assert.Contains("{local}", note.GetAttribute("data-title"));
    }

    [Fact]
    public void The_booking_href_of_a_time_is_unchanged()
    {
        var cut = Render([At(24, 9)]);
        Assert.Equal($"/rezerwacja/{Offer}?start=2026-11-24T08%3A00%3A00Z&gabinet=demo-anna", cut.Find("a.slot").GetAttribute("href"));
    }

    [Theory]
    [InlineData(23, "dziś, 09:30")]
    [InlineData(24, "jutro, 09:30")]
    [InlineData(26, "czwartek, 26 listopada, 09:30")]
    public void The_nearest_time_is_worded_relative_to_today(int day, string expected) =>
        Assert.Equal(expected, SlotPickerModel.NearestLabel(SlotPickerModel.GroupByDay([At(day, 14), At(day, 9, 30)]), Today));

    [Fact]
    public void No_days_means_no_nearest_time() => Assert.Null(SlotPickerModel.NearestLabel([], Today));

    [Fact]
    public void The_nearest_line_is_shown_above_the_strip()
    {
        var cut = Render([At(24, 9, 30)]);
        Assert.Equal("Najbliższy wolny termin: jutro, 09:30", cut.Find("[data-testid=slots-nearest]").TextContent);
    }

    [Fact]
    public void Day_chips_are_two_lines_with_the_full_date_as_the_name()
    {
        var cut = Render([At(30, 9)]);
        var chip = cut.Find(".slot-days a");
        Assert.Equal("poniedziałek, 30 listopada", chip.GetAttribute("aria-label"));
        Assert.Equal("pon.30 lis", chip.TextContent);
        Assert.Equal("true", chip.GetAttribute("aria-current"));
        Assert.Contains("chip", chip.ClassList);
    }

    [Fact]
    public void Up_to_seven_days_show_no_week_links()
    {
        var cut = Render(Enumerable.Range(24, 7).Select(d => At(d, 9)));
        Assert.Equal(7, cut.FindAll(".slot-days a").Count);
        Assert.DoesNotContain(cut.FindAll("a"), a => a.TextContent.Contains("tydzień"));
    }

    [Fact]
    public void More_than_seven_days_page_by_seven_with_links_to_the_first_day_of_the_neighbour_page()
    {
        var slots = Enumerable.Range(1, 20).Select(d => At(d, 9)).ToList(); // 1..20 Nov
        var days = SlotPickerModel.GroupByDay(slots);
        var first = Render(slots);
        Assert.Equal(7, first.FindAll(".slot-days a").Count);
        Assert.DoesNotContain(first.FindAll("a"), a => a.TextContent == "Poprzedni tydzień");
        var next = first.FindAll("a").Single(a => a.TextContent == "Następny tydzień");
        Assert.Equal(SlotPickerModel.PickerHref("demo-anna", 1, Offer, days[7].Date), next.GetAttribute("href"));

        var second = Render(slots, days[8].Date); // a day on the second page
        Assert.Equal(days.Skip(7).Take(7).Select(d => d.Date.Day), second.FindAll(".slot-days a").Select(a => int.Parse(a.GetAttribute("aria-label")!.Split(' ')[1])));
        var prev = second.FindAll("a").Single(a => a.TextContent == "Poprzedni tydzień");
        Assert.Equal(SlotPickerModel.PickerHref("demo-anna", 1, Offer, days[0].Date), prev.GetAttribute("href"));
    }

    [Fact]
    public void Neighbour_pages_are_null_at_the_ends()
    {
        var days = SlotPickerModel.GroupByDay(Enumerable.Range(1, 10).Select(d => At(d, 9)));
        Assert.Null(SlotPickerModel.NeighbourPageDay(days, 0, -1));
        Assert.Equal(days[7].Date, SlotPickerModel.NeighbourPageDay(days, 0, 1));
        Assert.Null(SlotPickerModel.NeighbourPageDay(days, 1, 1));
        Assert.Equal(days[0].Date, SlotPickerModel.NeighbourPageDay(days, 1, -1));
    }

    [Fact]
    public void More_days_is_a_quiet_link_after_the_times()
    {
        var cut = Render([At(24, 9)]);
        var more = cut.Find("a.btn--ghost:not(.slot)");
        Assert.Equal("Pokaż kolejne dni", more.TextContent);
        Assert.True(cut.Markup.IndexOf("slot-part", StringComparison.Ordinal) < cut.Markup.IndexOf("Pokaż kolejne dni", StringComparison.Ordinal));
    }

    [Fact]
    public void Unavailable_and_empty_states_keep_their_hooks()
    {
        var failed = Render<SlotPicker>(p => p.Add(c => c.Slug, "demo-anna").Add(c => c.Offering, Offering).Add(c => c.Slots, OfferSlots.Failed).Add(c => c.Today, Today));
        Assert.Contains("Nie możemy teraz pokazać terminów", failed.Find("[data-testid=slots-unavailable]").TextContent);
        Assert.Equal("Spróbuj ponownie", failed.Find("[data-testid=slots-unavailable] + a").TextContent);
        var empty = Render([]);
        Assert.NotNull(empty.Find("[data-testid=slots-empty]"));
        Assert.Equal("polite", empty.Find(".slot-picker").GetAttribute("aria-live"));
    }
}
