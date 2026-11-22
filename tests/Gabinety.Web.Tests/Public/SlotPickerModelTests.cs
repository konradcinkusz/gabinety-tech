using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Gabinet;

namespace Gabinety.Web.Tests.Public;

public class SlotPickerModelTests
{
    static readonly DateOnly Today = new(2026, 11, 23);
    static readonly DateTimeOffset Now = new(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Windows_are_14_days_each_and_stop_at_today_plus_59()
    {
        Assert.Equal(new SlotWindow(Today, Today.AddDays(13)), SlotPickerModel.Window(Today, 0));
        Assert.Equal(new SlotWindow(Today.AddDays(14), Today.AddDays(27)), SlotPickerModel.Window(Today, 1));
        Assert.Equal(new SlotWindow(Today.AddDays(56), Today.AddDays(59)), SlotPickerModel.Window(Today, 4)); // clipped to the horizon
        Assert.Null(SlotPickerModel.Window(Today, 5));
        Assert.Null(SlotPickerModel.Window(Today, -1));
        Assert.Equal(5, SlotPickerModel.MaxWindows);
    }

    [Fact]
    public void Slots_that_started_or_lie_beyond_the_horizon_are_dropped()
    {
        var past = new SlotDto(Now.AddMinutes(-1), Now.AddHours(1));
        var now = new SlotDto(Now, Now.AddHours(1)); // starting exactly now has started
        var ok = new SlotDto(Now.AddHours(1), Now.AddHours(2));
        var lastDay = new SlotDto(new DateTimeOffset(2027, 1, 21, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 1, 21, 11, 0, 0, TimeSpan.Zero)); // today + 59
        var tooFar = new SlotDto(new DateTimeOffset(2027, 1, 22, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 1, 22, 11, 0, 0, TimeSpan.Zero)); // today + 60
        Assert.Equal([ok, lastDay], SlotPickerModel.WithinHorizon([past, now, ok, lastDay, tooFar], Now));
    }

    [Fact]
    public void Days_are_Warsaw_days_so_22_30_UTC_in_winter_belongs_to_the_next_day()
    {
        var late = PublicSupport.Slot(24, 22);       // 23:00 Warsaw on the 24th
        var later = new SlotDto(new DateTimeOffset(2026, 11, 24, 23, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 25, 0, 30, 0, TimeSpan.Zero)); // 00:30 Warsaw on the 25th
        var early = PublicSupport.Slot(24, 8);
        var days = SlotPickerModel.GroupByDay([later, late, early]);
        Assert.Equal([new DateOnly(2026, 11, 24), new DateOnly(2026, 11, 25)], days.Select(d => d.Date));
        Assert.Equal([early, late], days[0].Slots); // sorted by time inside a day
        Assert.Equal([later], days[1].Slots);
    }

    [Fact]
    public void The_selected_day_is_the_one_asked_for_else_the_first_with_slots()
    {
        var days = SlotPickerModel.GroupByDay([PublicSupport.Slot(24, 9), PublicSupport.Slot(26, 9)]);
        Assert.Equal(new DateOnly(2026, 11, 26), SlotPickerModel.Selected(days, new DateOnly(2026, 11, 26))!.Date);
        Assert.Equal(new DateOnly(2026, 11, 24), SlotPickerModel.Selected(days, new DateOnly(2026, 11, 25))!.Date); // a day with no slots falls back
        Assert.Equal(new DateOnly(2026, 11, 24), SlotPickerModel.Selected(days, null)!.Date);
        Assert.Null(SlotPickerModel.Selected([], null));
    }

    [Fact]
    public void A_slot_leads_to_the_booking_flow_with_start_and_gabinet_as_the_Next_page_does()
    {
        var id = Guid.Parse("018f0000-0000-7000-8000-0000000000b1");
        Assert.Equal($"/rezerwacja/{id}?start=2026-11-24T09%3A00%3A00Z&gabinet=demo-anna", SlotPickerModel.BookingHref(id, new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero), "demo-anna"));
    }

    [Fact]
    public void Picker_links_keep_the_state_in_the_url_and_anchor_at_the_offer()
    {
        var id = Guid.Parse("018f0000-0000-7000-8000-0000000000b1");
        Assert.Equal("/gabinet/demo-anna", SlotPickerModel.PickerHref("demo-anna", 1, null, null));
        Assert.Equal($"/gabinet/demo-anna?okna=2&oferta={id}&dzien=2026-11-25#oferta-{id}", SlotPickerModel.PickerHref("demo-anna", 2, id, new DateOnly(2026, 11, 25)));
    }

    [Theory]
    [InlineData(null, 1)] [InlineData("", 1)] [InlineData("abc", 1)] [InlineData("-3", 1)] [InlineData("0", 1)] [InlineData("3", 3)] [InlineData("99", 5)]
    public void The_window_count_of_the_url_is_clamped(string? raw, int expected) => Assert.Equal(expected, SlotPickerModel.ClampWindows(raw));
}
