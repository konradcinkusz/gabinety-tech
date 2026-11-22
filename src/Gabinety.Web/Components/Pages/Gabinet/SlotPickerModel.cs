using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Shared;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Gabinet;

public sealed record DayGroup(DateOnly Date, IReadOnlyList<SlotDto> Slots);

public sealed record SlotWindow(DateOnly From, DateOnly To);

/// <summary>
/// Slot logic of the profile page, pure (BLAZOR-WEB-APP section 10). Port of <c>web/src/components/slots/slotTime.ts</c>: the API speaks UTC instants and Warsaw dates (CONTRACTS 4.2: a request
/// spans at most 14 days and lies within 60), the page speaks Europe/Warsaw. The picker is static: the days, the selected day and "more days" are links, so it works without JavaScript.
/// </summary>
public static class SlotPickerModel
{
    public const int WindowDays = 14, HorizonDays = 60;
    public static readonly int MaxWindows = (HorizonDays + WindowDays - 1) / WindowDays;

    /// <summary>The index-th request window, or null once the horizon is reached. The last offered date is today + 59.</summary>
    public static SlotWindow? Window(DateOnly today, int index)
    {
        var last = today.AddDays(HorizonDays - 1);
        var from = today.AddDays(index * WindowDays);
        if (index < 0 || from > last) return null;
        var to = from.AddDays(WindowDays - 1);
        return new SlotWindow(from, to > last ? last : to);
    }

    /// <summary>Defence in depth: whatever the API returns, offer only slots that have not started and lie inside the horizon.</summary>
    public static IEnumerable<SlotDto> WithinHorizon(IEnumerable<SlotDto> slots, DateTimeOffset now)
    {
        var last = PlFormat.TodayWarsaw(now).AddDays(HorizonDays - 1);
        return slots.Where(s => s.StartUtc > now && DateOnly.FromDateTime(PlFormat.ToWarsaw(s.StartUtc)) <= last);
    }

    public static IReadOnlyList<DayGroup> GroupByDay(IEnumerable<SlotDto> slots) =>
        [.. slots.OrderBy(s => s.StartUtc).GroupBy(s => DateOnly.FromDateTime(PlFormat.ToWarsaw(s.StartUtc))).Select(g => new DayGroup(g.Key, [.. g]))];

    /// <summary>The day shown for an offer: the one asked for when it has slots, else the first day with slots.</summary>
    public static DayGroup? Selected(IReadOnlyList<DayGroup> days, DateOnly? requested) =>
        days.FirstOrDefault(d => d.Date == requested) ?? days.FirstOrDefault();

    /// <summary>Link of the booking flow for one slot (journey J3): the booking page resolves the offering through the public profile, so <c>gabinet</c> travels with <c>start</c>.</summary>
    public static string BookingHref(Guid offeringId, DateTimeOffset startUtc, string profileSlug) =>
        $"/rezerwacja/{offeringId}?start={Uri.EscapeDataString(startUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture))}&gabinet={Uri.EscapeDataString(profileSlug)}";

    /// <summary>The profile with the picker state of one offer in the URL (windows loaded, offer and day chosen), anchored at the offer.</summary>
    public static string PickerHref(string profileSlug, int windows, Guid? offeringId, DateOnly? day)
    {
        var parts = new List<string>();
        if (windows > 1) parts.Add($"okna={windows}");
        if (offeringId is { } o && day is { } d) parts.Add($"oferta={o}&dzien={d:yyyy-MM-dd}");
        return $"/gabinet/{Uri.EscapeDataString(profileSlug)}{(parts.Count == 0 ? "" : "?" + string.Join('&', parts))}{(offeringId is { } id ? $"#oferta-{id}" : "")}";
    }

    public static int ClampWindows(string? raw) => int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, 1, MaxWindows) : 1;

    public const int PageDays = 7;

    /// <summary>The 7-day page (index into the days that have times) that contains the selected day.</summary>
    public static IReadOnlyList<DayGroup> Page(IReadOnlyList<DayGroup> days, DateOnly selected, out int pageIndex)
    {
        var at = Math.Max(0, days.ToList().FindIndex(d => d.Date == selected));
        pageIndex = at / PageDays;
        return [.. days.Skip(pageIndex * PageDays).Take(PageDays)];
    }

    /// <summary>First day of the previous or next 7-day page, or null at either end.</summary>
    public static DateOnly? NeighbourPageDay(IReadOnlyList<DayGroup> days, int pageIndex, int direction)
    {
        var target = (pageIndex + direction) * PageDays;
        return target < 0 || target >= days.Count ? null : days[target].Date;
    }

    public enum TimeOfDay { Morning, Afternoon, Evening }

    /// <summary>Rano before 12:00, Po południu 12:00-16:59, Wieczorem from 17:00 (Warsaw time).</summary>
    public static TimeOfDay PartOfDay(DateTimeOffset startUtc) => PlFormat.ToWarsaw(startUtc).Hour switch { < 12 => TimeOfDay.Morning, < 17 => TimeOfDay.Afternoon, _ => TimeOfDay.Evening };

    /// <summary>The slots of one day in the three groups, empty groups omitted, in day order.</summary>
    public static IReadOnlyList<(TimeOfDay Part, IReadOnlyList<SlotDto> Slots)> GroupByPartOfDay(IEnumerable<SlotDto> slots) =>
        [.. slots.OrderBy(s => s.StartUtc).GroupBy(s => PartOfDay(s.StartUtc)).OrderBy(g => g.Key).Select(g => (g.Key, (IReadOnlyList<SlotDto>)[.. g]))];

    /// <summary>"jutro, 09:30" for the first free time (relative day for today and tomorrow, else "wtorek, 24 listopada, 09:30"); null when there is none.</summary>
    public static string? NearestLabel(IReadOnlyList<DayGroup> days, DateOnly today)
    {
        var first = days.OrderBy(d => d.Date).FirstOrDefault(d => d.Slots.Count > 0);
        if (first is null) return null;
        var slot = first.Slots.OrderBy(s => s.StartUtc).First();
        var day = first.Date == today ? PlPages.Slots.Today : first.Date == today.AddDays(1) ? PlPages.Slots.Tomorrow : PlFormat.DayHeading(first.Date);
        return $"{day}, {PlFormat.Time(slot.StartUtc)}";
    }
}
