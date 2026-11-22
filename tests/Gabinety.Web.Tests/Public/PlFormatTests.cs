using Gabinety.Web.Components.Pages.Shared;

namespace Gabinety.Web.Tests.Public;

/// <summary>F-08: ONE helper for Polish dates; months in the genitive and lowercase, 24 h time in Warsaw, the decimal comma.</summary>
public class PlFormatTests
{
    [Theory]
    [InlineData("2026-01-05", "poniedziałek, 5 stycznia")] [InlineData("2026-02-14", "sobota, 14 lutego")] [InlineData("2026-03-01", "niedziela, 1 marca")]
    [InlineData("2026-04-07", "wtorek, 7 kwietnia")] [InlineData("2026-05-13", "środa, 13 maja")] [InlineData("2026-06-04", "czwartek, 4 czerwca")]
    [InlineData("2026-07-03", "piątek, 3 lipca")] [InlineData("2026-08-10", "poniedziałek, 10 sierpnia")] [InlineData("2026-09-15", "wtorek, 15 września")]
    [InlineData("2026-10-21", "środa, 21 października")] [InlineData("2026-11-24", "wtorek, 24 listopada")] [InlineData("2026-12-25", "piątek, 25 grudnia")]
    public void A_day_heading_has_the_lowercase_weekday_and_the_genitive_month(string iso, string expected) =>
        Assert.Equal(expected, PlFormat.DayHeading(DateOnly.ParseExact(iso, "yyyy-MM-dd")));

    [Fact]
    public void Member_since_reads_od_listopada_2026_not_od_listopad_2026() =>
        Assert.Equal("listopada 2026", PlFormat.MonthYearGenitive(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void A_long_date_is_day_genitive_month_year() => Assert.Equal("24 listopada 2026", PlFormat.DayLong(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)));

    [Theory]
    [InlineData(2026, 11, 24, 12, "13:00")] // winter: UTC+1
    [InlineData(2026, 7, 24, 12, "14:00")]  // summer: UTC+2
    [InlineData(2026, 3, 29, 0, "01:00")]   // the night DST starts is still winter at 00:00 UTC
    [InlineData(2026, 3, 29, 1, "03:00")]   // 01:00 UTC is 03:00 summer time: 02:xx does not exist
    [InlineData(2026, 10, 25, 0, "02:00")]  // the night DST ends: 00:00 UTC is still summer
    [InlineData(2026, 10, 25, 1, "02:00")]  // 01:00 UTC is 02:00 winter time (the second 02:00)
    public void Time_is_24_hour_in_Warsaw_across_both_changes(int y, int m, int d, int hourUtc, string expected) =>
        Assert.Equal(expected, PlFormat.Time(new DateTimeOffset(y, m, d, hourUtc, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void Today_in_Warsaw_is_the_next_date_after_23_00_UTC_in_winter() =>
        Assert.Equal(new DateOnly(2026, 11, 25), PlFormat.TodayWarsaw(new DateTimeOffset(2026, 11, 24, 23, 30, 0, TimeSpan.Zero)));

    [Theory]
    [InlineData("2026-11-13", "13.11.2026")] [InlineData("2026-11-13T00:00:00.000Z", "13.11.2026")] [InlineData("nonsense", "nonsense")]
    public void A_numeric_date_takes_the_date_part_only(string iso, string expected) => Assert.Equal(expected, PlFormat.NumericDate(iso));

    [Theory]
    [InlineData(45.0, 2, "45")] [InlineData(12.5, 2, "12,5")] [InlineData(3.8, 2, "3,8")] [InlineData(0.125, 2, "0,13")] [InlineData(5.0, 2, "5")]
    public void A_number_has_a_decimal_comma_and_no_trailing_zeros(double v, int max, string expected) => Assert.Equal(expected, PlFormat.Number(v, max));

    [Fact]
    public void Fixed_percent_and_rating_use_the_comma()
    {
        Assert.Equal("40,20", PlFormat.Fixed(40.2));
        Assert.Equal("0,893", PlFormat.Fixed(0.89333, 3));
        Assert.Equal("45%", PlFormat.Percent(0.45));
        Assert.Equal("12,5%", PlFormat.Percent(0.125));
        Assert.Equal("4,8", PlFormat.Rating(4.8));
        Assert.Equal("4,5", PlFormat.Rating(4.5));
    }
}
