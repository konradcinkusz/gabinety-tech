using System.Globalization;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Shared;

/// <summary>
/// The one place Polish dates and numbers are formatted (audit F-08): months in the genitive and lowercase ("wtorek, 24 listopada", "od listopada 2026"), 24-hour time, the decimal comma,
/// all in Europe/Warsaw because that is the zone the product speaks (SPEC 6.5). Plain C#, no culture data of the machine involved, so a page reads the same everywhere.
/// </summary>
public static class PlFormat
{
    static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static DateTime ToWarsaw(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Warsaw).DateTime;

    public static DateOnly TodayWarsaw(DateTimeOffset now) => DateOnly.FromDateTime(ToWarsaw(now));

    /// <summary><c>13:30</c> in Warsaw.</summary>
    public static string Time(DateTimeOffset instant) => ToWarsaw(instant).ToString("HH:mm", Inv);

    /// <summary><c>wtorek, 24 listopada</c> for a local date.</summary>
    public static string DayHeading(DateOnly date) => $"{Pl.Format.Weekdays[(int)date.DayOfWeek]}, {date.Day} {Pl.Format.MonthsGenitive[date.Month - 1]}";

    /// <summary><c>24 listopada 2026</c>.</summary>
    public static string DayLong(DateTimeOffset instant)
    {
        var d = ToWarsaw(instant);
        return $"{d.Day} {Pl.Format.MonthsGenitive[d.Month - 1]} {d.Year}";
    }

    /// <summary><c>listopada 2026</c>: the genitive that follows "od" ("Na platformie od listopada 2026").</summary>
    public static string MonthYearGenitive(DateTimeOffset instant)
    {
        var d = ToWarsaw(instant);
        return $"{Pl.Format.MonthsGenitive[d.Month - 1]} {d.Year}";
    }

    /// <summary><c>13.11.2026</c> from <c>2026-11-13</c> or a full timestamp (the date part is the date: no zone arithmetic); anything else is returned as it is.</summary>
    public static string NumericDate(string iso) =>
        iso.Length >= 10 && DateOnly.TryParseExact(iso[..10], "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d) ? d.ToString("dd.MM.yyyy", Inv) : iso;

    /// <summary>Decimal comma, at most <paramref name="max"/> decimals, no trailing zeros: 12.5 is "12,5", 45 is "45".</summary>
    public static string Number(double value, int max = 2) => Math.Round(value, max, MidpointRounding.AwayFromZero).ToString("0." + new string('#', max), Inv).Replace('.', ',');

    public static string Fixed(double value, int digits = 2) => Math.Round(value, digits, MidpointRounding.AwayFromZero).ToString("F" + digits, Inv).Replace('.', ',');

    public static string Percent(double weight) => Number(weight * 100, 1) + "%";

    /// <summary><c>4,8</c>.</summary>
    public static string Rating(double average) => Fixed(average, 1);
}
