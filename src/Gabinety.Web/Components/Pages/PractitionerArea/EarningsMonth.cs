using System.Globalization;
using Gabinety.Web.Api;
using Gabinety.Web.Components.Shared;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.PractitionerArea;

/// <summary>
/// The month the earnings card shows (T-11.17): <c>?miesiac=yyyy-MM</c>, or the current Warsaw month when the parameter is absent. Pure: the day is passed in, so the parsing,
/// the range and the wording are unit-tested without a page. A future month is not a month the card can show, so it reads as the current one.
/// </summary>
public static class EarningsMonth
{
    public const string Parameter = "miesiac";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The first day of the month asked for, or null when the parameter is absent, not a <c>yyyy-MM</c> month, or after the current Warsaw month.</summary>
    public static DateOnly? Parse(string? text, DateOnly today)
    {
        if (text is not { Length: 7 } || !DateOnly.TryParseExact(text + "-01", "yyyy-MM-dd", Inv, DateTimeStyles.None, out var first)) return null;
        return first > FirstOf(today) ? null : first;
    }

    /// <summary>The first day of the month of <paramref name="day"/>.</summary>
    public static DateOnly FirstOf(DateOnly day) => new(day.Year, day.Month, 1);

    /// <summary>Both days of a month, inclusive: what the service's <c>from</c> and <c>to</c> take.</summary>
    public static (DateOnly From, DateOnly To) Range(DateOnly first) => (first, new DateOnly(first.Year, first.Month, DateTime.DaysInMonth(first.Year, first.Month)));

    /// <summary>The value of the parameter for a month: <c>2026-10</c>.</summary>
    public static string Key(DateOnly first) => first.ToString("yyyy-MM", Inv);

    /// <summary><c>1 października 2026 – 31 października 2026</c>, from the range the service answered.</summary>
    public static string Period(DateOnly from, DateOnly to) => string.Format(Inv, PlPractitioner.Dashboard.EarningsPeriod, DayOf(from), DayOf(to));

    /// <summary>A grosz amount as the other money of the site: <c>1 234,56 zł</c>; a negative one (a reversal) keeps its sign.</summary>
    public static string Amount(MoneyDto money) => money.AmountMinor < 0 ? "−" + Money.Pln(-money.AmountMinor) : Money.Pln(money.AmountMinor);

    static string DayOf(DateOnly d) => $"{d.Day} {Pl.Format.MonthsGenitive[d.Month - 1]} {d.Year}";
}
