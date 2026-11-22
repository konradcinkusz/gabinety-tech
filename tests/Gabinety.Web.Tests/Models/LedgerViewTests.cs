using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Admin;

namespace Gabinety.Web.Tests.Admin;

/// <summary>The ledger page's view logic (UX-051): the reference of a row, the period in Polish, the totals per kind. Plain C#, no renderer.</summary>
public class LedgerViewTests
{
    // a UUIDv7-shaped id: the first eight characters are the timestamp, the last eight the random part
    static readonly Guid BookingA = Guid.Parse("0190b3f2-7c1d-7a4e-9b2f-3c5d8e1f2a6b");
    static readonly Guid BookingB = Guid.Parse("0190b3f2-7c1d-7a4e-9b2f-3c5d8e1f2a7c");

    [Fact]
    public void The_reference_is_the_random_tail_so_two_bookings_from_the_same_moment_tell_apart()
    {
        Assert.Equal("8e1f2a6b", AdminFormat.IdTail(BookingA));
        Assert.Equal("8e1f2a7c", AdminFormat.IdTail(BookingB));
        Assert.Equal(AdminFormat.ShortRef(BookingA), AdminFormat.ShortRef(BookingB)); // the old column: identical rows
        Assert.NotEqual(AdminFormat.IdTail(BookingA), AdminFormat.IdTail(BookingB));
    }

    [Theory]
    [InlineData("2026-11-01", "2026-11-22", "1–22 listopada 2026")] // one month: the day range, the month once
    [InlineData("2026-11-22", "2026-11-22", "22 listopada 2026")] // one day
    [InlineData("2026-10-01", "2026-11-22", "1 października – 22 listopada 2026")] // two months of one year
    [InlineData("2026-12-31", "2027-01-02", "31 grudnia 2026 – 2 stycznia 2027")] // across the year
    [InlineData("2026-02-30", "2026-03-01", "")] // not a real day: nothing to say (the page shows the format problem)
    public void The_period_is_written_in_Polish_with_the_genitive_months(string from, string to, string expected) =>
        Assert.Equal(expected, LedgerPeriod.Polish(from, to));

    static LedgerRowDto Row(string kind, long amount) => new(BookingA, kind, amount, "PLN", new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Totals_sum_each_kind_in_grosz_in_ledger_order_and_count_its_rows()
    {
        var totals = LedgerTotals.Of([Row("PlatformFee", 2700), Row("ClientCharge", 18000), Row("Refund", 10000), Row("ClientCharge", 9000), Row("PractitionerPayout", 6000)]);
        Assert.Equal(
            [new KindTotal("ClientCharge", 27000, 2), new KindTotal("Refund", 10000, 1), new KindTotal("PlatformFee", 2700, 1), new KindTotal("PractitionerPayout", 6000, 1)],
            totals);
    }

    [Fact]
    public void A_row_of_an_unknown_kind_is_not_summed_because_its_direction_is_unknown()
    {
        var totals = LedgerTotals.Of([Row("ClientCharge", 100), Row("Mystery", 999)]);
        Assert.Equal([new KindTotal("ClientCharge", 100, 1)], totals);
    }

    [Fact]
    public void No_rows_means_no_totals() => Assert.Empty(LedgerTotals.Of([]));
}
