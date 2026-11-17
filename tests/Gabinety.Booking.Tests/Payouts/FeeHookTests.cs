using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Contracts.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;
using BookingTrigger = Gabinety.Billing.BookingTrigger;

namespace Gabinety.Booking.Tests;

public class FeeHookTests
{
    static CreateHost Host(Action<CreateHost>? tweak = null) => Life.Host(new RecordingProvider(), tweak);

    [Theory]
    [InlineData(25000, 3750, 21250)]
    [InlineData(33333, 5000, 28333)]
    [InlineData(5000, 750, 4250)]
    [InlineData(150000, 22500, 127500)]
    public async Task Completion_of_a_first_booking_takes_the_fee_of_CONTRACTS_9_1(long price, long fee, long payout)
    {
        await using var h = Host();
        var client = Fee.NewClient();
        var id = await Fee.Seed(h, BookingStatus.Confirmed, client, h.Practitioner, price);
        Assert.True((await Fee.Apply(h, id, BookingTrigger.CompletionWindowElapsed)).Applied);

        Assert.Equal((fee, payout), await Fee.Amounts(h, id));
        var row = Assert.Single(await Fee.FeeRows(h, client, h.Practitioner));
        Assert.Equal((id, fee), (row.BookingId, row.AmountMinor));
    }

    [Fact]
    public async Task A_price_of_one_grosz_has_fee_zero_and_writes_no_ledger_row_so_the_pair_is_not_used_up()
    {
        await using var h = Host();
        var client = Fee.NewClient();
        var tiny = await Fee.Seed(h, BookingStatus.Confirmed, client, h.Practitioner, 1);
        await Fee.Apply(h, tiny, BookingTrigger.CompletionWindowElapsed);
        Assert.Equal((0L, 1L), await Fee.Amounts(h, tiny));
        Assert.Empty(await Fee.FeeRows(h, client, h.Practitioner)); // amount_minor is always positive (CONTRACTS 4.1)

        var next = await Fee.Seed(h, BookingStatus.Confirmed, client, h.Practitioner, 25000);
        await Fee.Apply(h, next, BookingTrigger.CompletionWindowElapsed);
        Assert.Equal((3750L, 21250L), await Fee.Amounts(h, next));
    }

    [Fact]
    public async Task The_second_consultation_of_a_pair_has_fee_zero_and_the_first_has_the_fee()
    {
        await using var h = Host();
        var client = Fee.NewClient();
        var first = await Fee.Seed(h, BookingStatus.Confirmed, client, h.Practitioner, 25000, TimeSpan.FromDays(2));
        var second = await Fee.Seed(h, BookingStatus.Confirmed, client, h.Practitioner, 25000, TimeSpan.FromDays(3));
        await Fee.Apply(h, first, BookingTrigger.CompletionWindowElapsed);
        await Fee.Apply(h, second, BookingTrigger.CompletionWindowElapsed);

        Assert.Equal((3750L, 21250L), await Fee.Amounts(h, first));
        Assert.Equal((0L, 25000L), await Fee.Amounts(h, second));
        Assert.Equal(first, Assert.Single(await Fee.FeeRows(h, client, h.Practitioner)).BookingId);
    }

    [Fact]
    public async Task A_different_client_or_a_different_practitioner_is_a_different_pair()
    {
        await using var h = Host();
        var client = Fee.NewClient();
        var other = Guid.NewGuid();
        var a = await Fee.Seed(h, BookingStatus.Confirmed, client, h.Practitioner, 10000);
        var b = await Fee.Seed(h, BookingStatus.Confirmed, Fee.NewClient(), h.Practitioner, 10000, TimeSpan.FromDays(3));
        var c = await Fee.Seed(h, BookingStatus.Confirmed, client, other, 10000);
        foreach (var id in new[] { a, b, c }) await Fee.Apply(h, id, BookingTrigger.CompletionWindowElapsed);
        foreach (var id in new[] { a, b, c }) Assert.Equal((1500L, 8500L), await Fee.Amounts(h, id));
    }

    [Fact]
    public async Task A_fee_row_that_already_exists_for_the_pair_makes_the_fee_zero()
    {
        await using var h = Host();
        var client = Fee.NewClient();
        await h.Seed(db => db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(), BookingId = Guid.NewGuid(), Kind = LedgerKind.PlatformFee, AmountMinor = 1500, PairKey = $"{client}:{h.Practitioner}", CreatedAt = h.Clock.GetUtcNow(),
        }));
        var id = await Fee.Seed(h, BookingStatus.Confirmed, client, h.Practitioner, 10000);
        await Fee.Apply(h, id, BookingTrigger.CompletionWindowElapsed);
        Assert.Equal((0L, 10000L), await Fee.Amounts(h, id));
        Assert.Single(await Fee.FeeRows(h, client, h.Practitioner));
    }

    [Fact]
    public async Task The_rate_comes_from_Billing_FirstConsultationFeeBps()
    {
        await using var h = Host(x => x.Settings["Billing:FirstConsultationFeeBps"] = "1000");
        var id = await Fee.Seed(h, BookingStatus.Confirmed, Fee.NewClient(), h.Practitioner, 25000);
        await Fee.Apply(h, id, BookingTrigger.CompletionWindowElapsed);
        Assert.Equal((2500L, 22500L), await Fee.Amounts(h, id));
    }

    [Fact]
    public async Task A_client_no_show_report_is_money_complete()
    {
        await using var h = Host();
        var id = await Fee.Seed(h, BookingStatus.Confirmed, Fee.NewClient(), h.Practitioner, 25000);
        Assert.True((await Fee.Apply(h, id, BookingTrigger.ClientNoShowReported)).Applied);
        Assert.Equal((BookingStatus.ClientNoShow, 3750L, 21250L), ((await Life.Row(h, id)).Status, (await Life.Row(h, id)).FeeMinor, (await Life.Row(h, id)).PayoutMinor));
    }

    [Fact]
    public async Task A_late_client_cancel_through_the_endpoint_takes_the_fee_and_refunds_nothing()
    {
        await using var h = Host();
        var id = await Life.Seed(h, BookingStatus.Confirmed, TimeSpan.FromHours(23));
        var r = await Life.Cancel(h, id, Life.Client);
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        var row = await Life.Row(h, id);
        Assert.Equal((BookingStatus.LateCancelledByClient, 1500L, 8500L), (row.Status, row.FeeMinor, row.PayoutMinor));
        Assert.Single(await Fee.FeeRows(h, Life.Client, h.Practitioner));
    }

    [Fact]
    public async Task Resolved_release_keeps_the_full_price()
    {
        await using var h = Host();
        var id = await Fee.Seed(h, BookingStatus.Disputed, Fee.NewClient(), h.Practitioner, 25000);
        Assert.True((await Fee.Apply(h, id, BookingTrigger.AdminResolves, c => c.Booking.DisputeResolution = "release")).Applied);
        Assert.Equal((3750L, 21250L), await Fee.Amounts(h, id));
    }

    [Fact]
    public async Task Resolved_refund_partial_takes_the_fee_on_the_kept_part_only()
    {
        await using var h = Host();
        var client = Fee.NewClient();
        var id = await Fee.Seed(h, BookingStatus.Disputed, client, h.Practitioner, 25000, refunded: 5000);
        Assert.True((await Fee.Apply(h, id, BookingTrigger.AdminResolves, c => c.Booking.DisputeResolution = "refund_partial")).Applied);
        Assert.Equal((3000L, 17000L), await Fee.Amounts(h, id)); // kept 20000: fee 15 % = 3000
        Assert.Equal(3000, Assert.Single(await Fee.FeeRows(h, client, h.Practitioner)).AmountMinor);
    }

    [Fact]
    public async Task Resolved_refund_full_writes_nothing()
    {
        await using var h = Host();
        var client = Fee.NewClient();
        var id = await Fee.Seed(h, BookingStatus.Disputed, client, h.Practitioner, 25000, refunded: 25000);
        Assert.True((await Fee.Apply(h, id, BookingTrigger.AdminResolves, c => c.Booking.DisputeResolution = "refund_full")).Applied);
        Assert.Equal((default(long?), default(long?)), await Fee.Amounts(h, id));
        Assert.Empty(await Fee.FeeRows(h, client, h.Practitioner));
    }

    [Fact]
    public async Task The_full_refund_statuses_get_no_fee_and_no_payout()
    {
        await using var h = Host();
        var client = Fee.NewClient();
        var early = await Fee.Seed(h, BookingStatus.Confirmed, client, h.Practitioner, 10000, TimeSpan.FromDays(2));
        Assert.True((await Fee.Apply(h, early, BookingTrigger.PractitionerCancels)).Applied);
        var pending = await Fee.Seed(h, BookingStatus.PendingPayment, client, h.Practitioner, 10000, TimeSpan.FromDays(3));
        Assert.True((await Fee.Apply(h, pending, BookingTrigger.HoldExpired)).Applied);

        foreach (var id in new[] { early, pending }) Assert.Equal((default(long?), default(long?)), await Fee.Amounts(h, id));
        Assert.Empty(await Fee.FeeRows(h, client, h.Practitioner));
    }

    [Fact]
    public async Task A_client_cancel_24h_or_more_before_start_gets_no_fee()
    {
        await using var h = Host();
        var id = await Life.Seed(h, BookingStatus.Confirmed, TimeSpan.FromDays(2));
        Assert.Equal(System.Net.HttpStatusCode.OK, (await Life.Cancel(h, id, Life.Client)).StatusCode);
        var row = await Life.Row(h, id);
        Assert.Equal((BookingStatus.CancelledByClient, default(long?), default(long?)), (row.Status, row.FeeMinor, row.PayoutMinor));
        Assert.Empty(await Fee.FeeRows(h, Life.Client, h.Practitioner));
    }
}
