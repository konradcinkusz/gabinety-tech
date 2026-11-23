namespace Gabinety.Web.Components.Pages.Gabinet;

/// <summary>Compact two-line labels of the day strip (<c>pon.</c> over <c>30 lis</c>); the full date stays the accessible name.</summary>
public static class SlotDayFormat
{
    static readonly string[] Weekdays = ["niedz.", "pon.", "wt.", "śr.", "czw.", "pt.", "sob."];
    static readonly string[] Months = ["sty", "lut", "mar", "kwi", "maj", "cze", "lip", "sie", "wrz", "paź", "lis", "gru"];

    public static string ShortWeekday(DateOnly d) => Weekdays[(int)d.DayOfWeek];
    public static string ShortDate(DateOnly d) => $"{d.Day} {Months[d.Month - 1]}";
}
