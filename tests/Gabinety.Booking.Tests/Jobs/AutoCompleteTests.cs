using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Contracts.Booking;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Jobs.AutoComplete;
using Gabinety.Booking.Jobs.Base;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

public class AutoCompleteTests : IDisposable
{
    readonly CreateHost h = Auto.Host();
    public void Dispose() => h.Dispose();

    [Fact]
    public async Task A_confirmed_booking_completes_exactly_at_end_plus_48_hours_and_not_one_second_before()
    {
        var id = await Auto.Seed(h, Auto.Window - TimeSpan.FromSeconds(1));
        var job = Auto.Job(h);

        Assert.Equal(0, await job.CompleteDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(BookingStatus.Confirmed, await Hold.StatusOf(h, id));

        h.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await job.CompleteDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(BookingStatus.Completed, await Hold.StatusOf(h, id));
    }

    [Fact]
    public async Task Completion_sets_ended_at_and_payout_due_at_and_the_fee_hook_computes_the_amounts_in_the_same_transaction()
    {
        var id = await Auto.Seed(h, Auto.Window);

        Assert.Equal(1, await Auto.Job(h).CompleteDueAsync(h.Clock.GetUtcNow(), default));

        var b = await Life.Row(h, id);
        Assert.Equal(BookingStatus.Completed, b.Status);
        Assert.Equal(b.EndUtc, b.EndedAt);
        Assert.Equal(b.StartUtc + TimeSpan.FromHours(72), b.PayoutDueAt); // max(end + 48 h, start + 72 h): the claim window has closed first (ADR proposal 0011)
        Assert.Equal(1500, b.FeeMinor);    // 15 % of 10000, first consultation of the pair
        Assert.Equal(8500, b.PayoutMinor);
        var fee = Assert.Single(await Life.Ledger(h, id), l => l.Kind == LedgerKind.PlatformFee);
        Assert.Equal(1500, fee.AmountMinor);
    }

    [Fact]
    public async Task The_window_comes_from_Booking_CompleteAfterHours()
    {
        using var h24 = Auto.Host(("Booking:CompleteAfterHours", "24"));
        var id = await Auto.Seed(h24, TimeSpan.FromHours(24));
        Assert.Equal(1, await Auto.Job(h24).CompleteDueAsync(h24.Clock.GetUtcNow(), default));
        Assert.Equal(BookingStatus.Completed, await Hold.StatusOf(h24, id));
    }

    [Fact]
    public async Task A_disputed_booking_is_never_completed_even_when_its_id_comes_from_a_stale_selection()
    {
        var disputed = await Auto.Seed(h, Auto.Window + TimeSpan.FromHours(5), BookingStatus.Disputed);
        var job = Auto.Job(h);

        Assert.Equal(0, await job.CompleteDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(0, await job.CompleteAsync([disputed], h.Clock.GetUtcNow(), default)); // selected while Confirmed, disputed meanwhile
        Assert.Equal(BookingStatus.Disputed, await Hold.StatusOf(h, disputed));
        Assert.Null((await Life.Row(h, disputed)).FeeMinor);
    }

    [Fact]
    public async Task A_stale_id_that_is_not_due_and_an_unknown_id_are_no_ops()
    {
        var early = await Auto.Seed(h, TimeSpan.FromHours(10));
        Assert.Equal(0, await Auto.Job(h).CompleteAsync([early, Guid.NewGuid()], h.Clock.GetUtcNow(), default));
        Assert.Equal(BookingStatus.Confirmed, await Hold.StatusOf(h, early));
    }

    [Fact]
    public async Task Other_statuses_are_left_alone()
    {
        var ids = new List<Guid>();
        foreach (var s in new[] { BookingStatus.CancelledByClient, BookingStatus.ClientNoShow, BookingStatus.Completed, BookingStatus.Resolved })
            ids.Add(await Auto.Seed(h, Auto.Window + TimeSpan.FromDays(1), s));
        var before = await Hold.Snapshot(h);
        Assert.Equal(0, await Auto.Job(h).CompleteDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(before, await Hold.Snapshot(h));
    }

    [Fact]
    public async Task Running_twice_changes_nothing()
    {
        await Auto.Seed(h, Auto.Window + TimeSpan.FromHours(1));
        await Auto.Seed(h, TimeSpan.FromHours(3));
        var job = Auto.Job(h);

        Assert.Equal(1, await job.CompleteDueAsync(h.Clock.GetUtcNow(), default));
        var afterFirst = await Hold.Snapshot(h);
        Assert.Equal(0, await job.CompleteDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(afterFirst, await Hold.Snapshot(h));
    }

    [Fact]
    public async Task A_per_run_cap_leaves_the_rest_for_the_next_run()
    {
        using var capped = Auto.Host(("Jobs:AutoComplete:BatchSize", "2"), ("Jobs:AutoComplete:MaxPerRun", "3"));
        for (var i = 0; i < 5; i++) await Auto.Seed(capped, Auto.Window + TimeSpan.FromHours(i));
        var job = Auto.Job(capped);
        Assert.Equal(3, await job.CompleteDueAsync(capped.Clock.GetUtcNow(), default));
        Assert.Equal(2, await job.CompleteDueAsync(capped.Clock.GetUtcNow(), default));
    }

    [Fact]
    public async Task A_failing_row_does_not_block_the_others_and_the_run_reports_it()
    {
        var failing = new HashSet<Guid>();
        using var fh = Hold.Host(failing);
        var a = await Auto.Seed(fh, Auto.Window + TimeSpan.FromHours(3));
        var bad = await Auto.Seed(fh, Auto.Window + TimeSpan.FromHours(2));
        var c = await Auto.Seed(fh, Auto.Window + TimeSpan.FromHours(1));
        failing.Add(bad);

        await Assert.ThrowsAsync<JobFailedException>(() => Auto.Job(fh).RunOnceAsync(fh.Clock.GetUtcNow(), default));

        Assert.Equal(BookingStatus.Completed, await Hold.StatusOf(fh, a));
        Assert.Equal(BookingStatus.Confirmed, await Hold.StatusOf(fh, bad));
        Assert.Equal(BookingStatus.Completed, await Hold.StatusOf(fh, c));
        var all = string.Join("\n", fh.Logs.Lines.Where(l => !l.StartsWith("Microsoft")));
        Assert.Contains(bad.ToString(), all);
        foreach (var forbidden in new[] { "demo-brief", "boom", Life.Client, fh.OwnerUserId, fh.Practitioner.ToString() }) Assert.DoesNotContain(forbidden, all);
    }

    [Fact]
    public async Task The_cycle_records_the_job_run_with_the_auto_complete_label()
    {
        using var capture = new MetricCapture(h.Services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        await Auto.Seed(h, Auto.Window);
        Assert.True(await Auto.Job(h).RunCycleAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(1, capture.Runs("auto_complete", "ok"));
    }

    [Fact]
    public void The_job_is_registered_once_as_a_singleton_and_as_a_hosted_service_by_its_own_feature()
    {
        var job = h.Services.GetRequiredService<AutoCompleteJob>();
        Assert.Same(job, h.Services.GetRequiredService<AutoCompleteJob>());
        Assert.Single(h.Services.GetServices<IHostedService>(), s => ReferenceEquals(s, job));
        Assert.Equal("auto_complete", job.Name);
    }
}
