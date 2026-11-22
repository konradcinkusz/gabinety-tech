using System.Net;
using Gabinety.Booking.Jobs.Payout;
using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>Supervisor decision on PR 127: the dispute right lasts to start + 72 h (CONTRACTS 4.2), also after auto-completion at end + 48 h (ADR proposal 0011).</summary>
public class DisputeFromCompletedTests : IDisposable
{
    readonly CreateHost h;
    readonly PayoutProvider provider;
    public DisputeFromCompletedTests() => (h, provider) = PayJob.Host();
    public void Dispose() => h.Dispose();

    static readonly object Body = new { kind = "not_as_described", text = "demo-dispute-text: it was not what the offer said" };

    /// <summary>A one-hour session that auto-completed at end + 48 h (start + 49 h); the clock is left there.</summary>
    async Task<Guid> Completed()
    {
        var id = await Auto.Seed(h, TimeSpan.FromHours(48));
        Assert.Equal(1, await Auto.Job(h).CompleteDueAsync(h.Clock.GetUtcNow(), default));
        var b = await Life.Row(h, id);
        Assert.Equal((BookingStatus.Completed, 1500L, 8500L), (b.Status, b.FeeMinor, b.PayoutMinor));
        Assert.Equal(b.StartUtc + TimeSpan.FromHours(72), b.PayoutDueAt);
        Assert.Single(await Life.Ledger(h, id), l => l.Kind == LedgerKind.PlatformFee);
        return id;
    }

    [Fact]
    public async Task A_dispute_at_start_plus_60_h_on_a_Completed_booking_succeeds_and_reopens_the_money()
    {
        var id = await Completed();
        h.Clock.Advance(TimeSpan.FromHours(11)); // start + 60 h
        var r = await DisputeApiTests.Dispute(h, id, Life.Client, Body);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var b = await Life.Row(h, id);
        Assert.Equal(BookingStatus.Disputed, b.Status);
        Assert.Equal((null, null, null), (b.FeeMinor, b.PayoutMinor, b.PayoutDueAt));
        Assert.DoesNotContain(await Life.Ledger(h, id), l => l.Kind == LedgerKind.PlatformFee); // the pair is free for the booking that really earns the fee
    }

    [Theory]
    [InlineData(71, 59, true)]
    [InlineData(72, 0, false)]
    [InlineData(73, 0, false)]
    public async Task The_right_ends_at_start_plus_72_h_and_not_a_moment_later(int hours, int minutes, bool ok)
    {
        var id = await Completed();
        h.Clock.Advance(TimeSpan.FromHours(hours - 49) + TimeSpan.FromMinutes(minutes));
        var r = await DisputeApiTests.Dispute(h, id, Life.Client, Body);
        Assert.Equal(ok ? HttpStatusCode.OK : HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(ok ? BookingStatus.Disputed : BookingStatus.Completed, (await Life.Row(h, id)).Status);
    }

    [Fact]
    public async Task While_disputed_the_payout_is_held_even_after_start_plus_72_h_and_a_release_pays_it_once_with_the_fee_set_again()
    {
        var id = await Completed();
        h.Clock.Advance(TimeSpan.FromHours(11));
        Assert.Equal(HttpStatusCode.OK, (await DisputeApiTests.Dispute(h, id, Life.Client, Body)).StatusCode);
        h.Clock.Advance(TimeSpan.FromHours(30));
        Assert.Equal(0, await PayJob.Job(h, provider).PayDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Empty(provider.Payouts);

        var resolve = await Adm.Resolve(h, id, Adm.Body("release"));
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        var b = await Life.Row(h, id);
        Assert.Equal((BookingStatus.Resolved, 1500L, 8500L), (b.Status, b.FeeMinor, b.PayoutMinor));
        Assert.Single(await Life.Ledger(h, id), l => l.Kind == LedgerKind.PlatformFee);
        Assert.Equal(1, await PayJob.Job(h, provider).PayDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Single(provider.Payouts);
    }

    [Fact]
    public async Task A_full_refund_after_such_a_dispute_leaves_no_fee_row_so_the_pairs_next_booking_still_earns_the_fee()
    {
        var id = await Completed();
        h.Clock.Advance(TimeSpan.FromHours(11));
        await DisputeApiTests.Dispute(h, id, Life.Client, Body);
        Assert.Equal(HttpStatusCode.OK, (await Adm.Resolve(h, id, Adm.Body("refund_full"))).StatusCode);
        var b = await Life.Row(h, id);
        Assert.Equal((BookingStatus.Resolved, 0L), (b.Status, b.FeeMinor ?? 0));
        Assert.DoesNotContain(await Life.Ledger(h, id), l => l.Kind == LedgerKind.PlatformFee);
    }

    [Fact]
    public async Task Only_the_client_may_dispute_a_Completed_booking()
    {
        var id = await Completed();
        Assert.Equal(HttpStatusCode.NotFound, (await DisputeApiTests.Dispute(h, id, "demo-stranger", Body)).StatusCode);
        Assert.Equal(BookingStatus.Completed, (await Life.Row(h, id)).Status);
    }
}
