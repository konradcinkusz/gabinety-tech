using System.Net;
using Gabinety.Booking.Features.Payments;
using Gabinety.Contracts.Booking;
using Gabinety.Booking.Jobs.Base;
using Gabinety.Booking.Jobs.HoldExpiry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

public class HoldExpiryTests : IDisposable
{
    static readonly TimeSpan Hold10 = TimeSpan.FromMinutes(10);
    readonly HashSet<Guid> failing = [];
    readonly CreateHost h;

    public HoldExpiryTests() => h = Hold.Host(failing);
    public void Dispose() => h.Dispose();

    [Fact]
    public async Task A_hold_expires_exactly_at_its_end_and_not_one_second_before()
    {
        var id = await Hold.Seed(h, Hold10);
        var job = Hold.Job(h);

        h.Clock.Advance(Hold10 - TimeSpan.FromSeconds(1));
        Assert.Equal(0, await job.ExpireDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(BookingStatus.PendingPayment, await Hold.StatusOf(h, id));

        h.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await job.ExpireDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(BookingStatus.Expired, await Hold.StatusOf(h, id));
    }

    [Fact]
    public async Task Several_due_holds_are_all_expired_in_one_run_and_a_not_due_one_is_left()
    {
        var due = new List<Guid>();
        for (var i = 0; i < 5; i++) due.Add(await Hold.Seed(h, TimeSpan.FromMinutes(i), slot: i));
        var later = await Hold.Seed(h, TimeSpan.FromMinutes(20), slot: 5);
        h.Clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(5, await Hold.Job(h).ExpireDueAsync(h.Clock.GetUtcNow(), default));

        foreach (var id in due) Assert.Equal(BookingStatus.Expired, await Hold.StatusOf(h, id));
        Assert.Equal(BookingStatus.PendingPayment, await Hold.StatusOf(h, later));
    }

    [Fact]
    public async Task The_batch_is_bounded_so_a_run_pages_until_none_are_left()
    {
        using var small = Hold.Host(null, ("Jobs:HoldExpiry:BatchSize", "2"));
        var ids = new List<Guid>();
        for (var i = 0; i < 7; i++) ids.Add(await Hold.Seed(small, TimeSpan.FromMinutes(i), slot: i));
        small.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(7, await Hold.Job(small).ExpireDueAsync(small.Clock.GetUtcNow(), default));
        foreach (var id in ids) Assert.Equal(BookingStatus.Expired, await Hold.StatusOf(small, id));
    }

    [Fact]
    public async Task A_per_run_cap_stops_the_run_and_takes_the_oldest_holds_first_leaving_the_rest_for_the_next_run()
    {
        using var capped = Hold.Host(null, ("Jobs:HoldExpiry:BatchSize", "2"), ("Jobs:HoldExpiry:MaxPerRun", "3"));
        // seeded newest first, so the order of insertion cannot be what makes the oldest go first
        var byAge = new Guid[5];
        for (var i = 4; i >= 0; i--) byAge[i] = await Hold.Seed(capped, TimeSpan.FromMinutes(i), slot: i);
        capped.Clock.Advance(TimeSpan.FromHours(1));
        var job = Hold.Job(capped);

        Assert.Equal(3, await job.ExpireDueAsync(capped.Clock.GetUtcNow(), default));
        for (var i = 0; i < 3; i++) Assert.Equal(BookingStatus.Expired, await Hold.StatusOf(capped, byAge[i]));
        for (var i = 3; i < 5; i++) Assert.Equal(BookingStatus.PendingPayment, await Hold.StatusOf(capped, byAge[i]));

        Assert.Equal(2, await job.ExpireDueAsync(capped.Clock.GetUtcNow(), default));
        for (var i = 3; i < 5; i++) Assert.Equal(BookingStatus.Expired, await Hold.StatusOf(capped, byAge[i]));
    }

    [Fact]
    public async Task A_booking_paid_between_selection_and_transition_is_left_Confirmed()
    {
        await h.WorkWeek();
        var (id, _) = await Pay.CreateBooking(h);
        Assert.Equal(HttpStatusCode.OK, (await Pay.Simulate(h.ClientFor("demo-client-1"), id, "succeeded")).StatusCode);
        Assert.Equal(BookingStatus.Confirmed, await Hold.StatusOf(h, id));
        h.Clock.Advance(Hold10 + TimeSpan.FromMinutes(1));

        // the job selected this id while the booking was still PendingPayment: the list is stale by the time the transition runs
        var applied = await Hold.Job(h).ExpireAsync([id], h.Clock.GetUtcNow(), default);

        Assert.Equal(0, applied);
        Assert.Equal(BookingStatus.Confirmed, await Hold.StatusOf(h, id));
        Assert.Equal(PaymentStatus.Succeeded, (await Life.PaymentOf(h, id)).Status);
    }

    [Fact]
    public async Task A_stale_id_whose_hold_is_not_due_is_left_alone_and_an_unknown_id_is_a_no_op()
    {
        var live = await Hold.Seed(h, Hold10);
        var applied = await Hold.Job(h).ExpireAsync([live, Guid.NewGuid()], h.Clock.GetUtcNow(), default);
        Assert.Equal(0, applied);
        Assert.Equal(BookingStatus.PendingPayment, await Hold.StatusOf(h, live));
    }

    [Fact]
    public async Task Running_twice_changes_nothing_and_a_run_with_nothing_due_writes_nothing()
    {
        await Hold.Seed(h, TimeSpan.FromMinutes(1), slot: 0);
        await Hold.Seed(h, TimeSpan.FromMinutes(30), slot: 1);
        var job = Hold.Job(h);
        var untouched = await Hold.Snapshot(h);

        Assert.Equal(0, await job.ExpireDueAsync(h.Clock.GetUtcNow(), default)); // nothing due yet
        Assert.Equal(untouched, await Hold.Snapshot(h));

        h.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, await job.ExpireDueAsync(h.Clock.GetUtcNow(), default));
        var afterFirst = await Hold.Snapshot(h);
        Assert.NotEqual(untouched, afterFirst);

        Assert.Equal(0, await job.ExpireDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(afterFirst, await Hold.Snapshot(h));
    }

    [Fact]
    public async Task Expiry_keeps_the_payment_Created_so_a_late_success_is_refunded_in_full()
    {
        await h.WorkWeek();
        var (id, pid) = await Pay.CreateBooking(h);
        h.Clock.Advance(Hold10);
        Assert.Equal(1, await Hold.Job(h).ExpireDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(PaymentStatus.Created, (await Life.PaymentOf(h, id)).Status);

        var late = await Pay.Simulate(h.ClientFor("demo-client-1"), id, "succeeded");

        Assert.Equal(HttpStatusCode.OK, late.StatusCode);
        Assert.Equal(BookingStatus.Expired, await Hold.StatusOf(h, id));
        Assert.Single(await Life.RefundRows(h, id));
        Assert.Equal(PaymentStatus.Refunded, (await Life.PaymentOf(h, id)).Status);
        _ = pid;
    }

    [Fact]
    public async Task After_expiry_a_new_booking_for_the_same_slot_succeeds()
    {
        await h.WorkWeek();
        var (first, _) = await Pay.CreateBooking(h, "demo-client-1");
        var taken = await CreateHost.Post(h.ClientFor("demo-client-2"), h.Req(), CreateHost.Key());
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode); // the live hold blocks the slot

        h.Clock.Advance(Hold10);
        Assert.Equal(1, await Hold.Job(h).ExpireDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(BookingStatus.Expired, await Hold.StatusOf(h, first));

        var second = await CreateHost.Post(h.ClientFor("demo-client-2"), h.Req(), CreateHost.Key());
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task One_bad_row_does_not_block_the_others_and_the_run_reports_the_failure()
    {
        var a = await Hold.Seed(h, TimeSpan.FromMinutes(1), slot: 0);
        var bad = await Hold.Seed(h, TimeSpan.FromMinutes(2), slot: 1);
        var c = await Hold.Seed(h, TimeSpan.FromMinutes(3), slot: 2);
        failing.Add(bad);
        h.Clock.Advance(TimeSpan.FromMinutes(10));

        await Assert.ThrowsAsync<JobFailedException>(() => Hold.Job(h).RunOnceAsync(h.Clock.GetUtcNow(), default));

        Assert.Equal(BookingStatus.Expired, await Hold.StatusOf(h, a));
        Assert.Equal(BookingStatus.PendingPayment, await Hold.StatusOf(h, bad)); // rolled back, retried next cycle
        Assert.Equal(BookingStatus.Expired, await Hold.StatusOf(h, c));

        failing.Clear();
        Assert.Equal(1, await Hold.Job(h).ExpireDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(BookingStatus.Expired, await Hold.StatusOf(h, bad));
    }

    [Fact]
    public async Task A_row_that_always_fails_does_not_make_the_run_loop_forever()
    {
        var bad = await Hold.Seed(h, TimeSpan.FromMinutes(1));
        failing.Add(bad);
        h.Clock.Advance(TimeSpan.FromMinutes(10));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await Assert.ThrowsAsync<JobFailedException>(() => Hold.Job(h).RunOnceAsync(h.Clock.GetUtcNow(), cts.Token));
        Assert.False(cts.IsCancellationRequested);
        // tried once in the run, not once per page until the cap
        Assert.Equal(1, h.Logs.Lines.Count(l => l.Contains(bad.ToString()) && l.Contains("could not expire")));
    }

    [Fact]
    public async Task Logs_carry_the_booking_id_and_never_the_brief_or_any_other_id()
    {
        var ok = await Hold.Seed(h, TimeSpan.FromMinutes(1), slot: 0);
        var bad = await Hold.Seed(h, TimeSpan.FromMinutes(2), slot: 1);
        failing.Add(bad);
        h.Clock.Advance(TimeSpan.FromMinutes(10));
        await Assert.ThrowsAsync<JobFailedException>(() => Hold.Job(h).RunOnceAsync(h.Clock.GetUtcNow(), default));

        var all = string.Join("\n", h.Logs.Lines.Where(l => !l.StartsWith("Microsoft")));
        Assert.Contains(ok.ToString(), all);
        Assert.Contains(bad.ToString(), all);
        Assert.Contains("InvalidOperationException", all);
        foreach (var forbidden in new[] { "demo-brief", "a first review of my plan", "boom", Life.Client, h.OwnerUserId, h.Practitioner.ToString(), h.Offering.ToString() })
            Assert.DoesNotContain(forbidden, all);
    }

    [Fact]
    public async Task The_cycle_records_the_job_run_with_the_hold_expiry_label_and_an_ok_or_error_outcome()
    {
        var capture = new MetricCapture(h.Services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        using var _ = capture;
        var id = await Hold.Seed(h, TimeSpan.FromMinutes(1));
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        var job = Hold.Job(h);

        Assert.True(await job.RunCycleAsync(h.Clock.GetUtcNow(), default));
        failing.Add(await Hold.Seed(h, TimeSpan.FromMinutes(1), slot: 1));
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.False(await job.RunCycleAsync(h.Clock.GetUtcNow(), default));

        Assert.Equal(1, capture.Runs("hold_expiry", "ok"));
        Assert.Equal(1, capture.Runs("hold_expiry", "error"));
        Assert.All(capture.Points, p => Assert.Equal("hold_expiry", p.Tags["job"]));
        Assert.Equal(BookingStatus.Expired, await Hold.StatusOf(h, id));
    }

    [Fact]
    public void The_job_is_registered_once_as_a_singleton_and_as_a_hosted_service_by_its_own_feature()
    {
        var job = h.Services.GetRequiredService<HoldExpiryJob>();
        Assert.Same(job, h.Services.GetRequiredService<HoldExpiryJob>());
        Assert.Single(h.Services.GetServices<IHostedService>(), s => ReferenceEquals(s, job));
        Assert.Equal("hold_expiry", job.Name);
    }
}
