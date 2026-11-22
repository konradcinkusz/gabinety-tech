using Gabinety.Contracts.Booking;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Features.Payouts;
using Gabinety.Booking.Jobs.Base;
using Gabinety.Booking.Jobs.AutoComplete;
using Gabinety.Booking.Jobs.Payout;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

public class PayoutTests : IDisposable
{
    readonly CreateHost h;
    readonly PayoutProvider provider;
    public PayoutTests() => (h, provider) = PayJob.Host();
    public void Dispose() => h.Dispose();

    [Fact]
    public async Task A_due_completed_booking_is_paid_once_with_the_stable_key_and_ends_Sent_with_one_ledger_entry()
    {
        var id = await PayJob.Seed(h);

        Assert.Equal(1, await PayJob.Job(h, provider).PayDueAsync(h.Clock.GetUtcNow(), default));

        var req = Assert.Single(provider.Payouts);
        Assert.Equal(new(h.Practitioner, 8500, "PLN", $"payout:{id:N}", null, $"pay_{id:N}", id), req); // no connected account in the fake profile; the payment and booking ids ride along
        var row = Assert.Single(await PayJob.PayoutRows(h, id));
        Assert.Equal(PayoutStatus.Sent, row.Status);
        Assert.Equal($"mockpayout_payout:{id:N}", row.ProviderRef);
        Assert.Equal(h.Clock.GetUtcNow(), row.SentAt);
        Assert.Equal(8500, row.AmountMinor);
        Assert.Equal(h.Practitioner, row.PractitionerId);
        var entry = Assert.Single(await PayJob.PayoutLedger(h, id));
        Assert.Equal(8500, entry.AmountMinor);
    }

    [Fact]
    public async Task Running_twice_changes_nothing()
    {
        await PayJob.Seed(h);
        var job = PayJob.Job(h, provider);
        Assert.Equal(1, await job.PayDueAsync(h.Clock.GetUtcNow(), default));
        var afterFirst = await PayJob.Snapshot(h);

        Assert.Equal(0, await job.PayDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(0, await PayJob.Job(h, provider).PayDueAsync(h.Clock.GetUtcNow(), default)); // a fresh instance too
        Assert.Equal(afterFirst, await PayJob.Snapshot(h));
        Assert.Single(provider.Payouts);
    }

    [Fact]
    public async Task A_payout_is_paid_exactly_when_due_and_not_one_second_before()
    {
        var id = await PayJob.Seed(h, dueAgo: -TimeSpan.FromSeconds(1)); // due in one second
        var job = PayJob.Job(h, provider);

        Assert.Equal(0, await job.PayDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Empty(provider.Payouts);
        Assert.Empty(await PayJob.PayoutRows(h, id));

        h.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await job.PayDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Single(provider.Payouts);
    }

    [Theory]
    [InlineData(BookingStatus.Completed, null)]
    [InlineData(BookingStatus.ClientNoShow, null)]
    [InlineData(BookingStatus.LateCancelledByClient, null)]
    [InlineData(BookingStatus.Resolved, "release")]
    [InlineData(BookingStatus.Resolved, "refund_partial")]
    public async Task Each_money_complete_status_is_paid(BookingStatus status, string? resolution)
    {
        var id = await PayJob.Seed(h, status, resolution);
        Assert.Equal(1, await PayJob.Job(h, provider).PayDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(PayoutStatus.Sent, Assert.Single(await PayJob.PayoutRows(h, id)).Status);
        Assert.Single(await PayJob.PayoutLedger(h, id));
    }

    [Theory]
    [InlineData(BookingStatus.CancelledByClient, null)]
    [InlineData(BookingStatus.CancelledByPractitioner, null)]
    [InlineData(BookingStatus.Expired, null)]
    [InlineData(BookingStatus.PendingPayment, null)]
    [InlineData(BookingStatus.Confirmed, null)]
    [InlineData(BookingStatus.Disputed, null)]
    [InlineData(BookingStatus.Resolved, "refund_full")]
    public async Task No_other_status_is_ever_paid_even_with_an_amount_and_a_past_due_date(BookingStatus status, string? resolution)
    {
        var id = await PayJob.Seed(h, status, resolution); // the amount is seeded on purpose: only the status must stop the payout
        Assert.Equal(0, await PayJob.Job(h, provider).PayDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Empty(provider.Payouts);
        Assert.Empty(await PayJob.PayoutRows(h, id));
        Assert.Empty(await PayJob.PayoutLedger(h, id));
    }

    [Fact]
    public async Task A_booking_disputed_between_selection_and_payout_is_skipped()
    {
        var id = await PayJob.Seed(h, BookingStatus.Disputed);
        Assert.Equal(0, await PayJob.Job(h, provider).PayAsync([id], h.Clock.GetUtcNow(), default)); // selected earlier, disputed since
        Assert.Empty(provider.Payouts);
        Assert.Empty(await PayJob.PayoutRows(h, id));
    }

    [Fact]
    public async Task A_payout_of_zero_or_unknown_amount_gets_no_row_and_no_call()
    {
        var zero = await PayJob.Seed(h, payoutMinor: 0);
        var unknown = await PayJob.Seed(h, payoutMinor: null);
        Assert.Equal(0, await PayJob.Job(h, provider).PayDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Empty(provider.Payouts);
        Assert.Empty(await PayJob.PayoutRows(h, zero));
        Assert.Empty(await PayJob.PayoutRows(h, unknown));
    }

    [Fact]
    public async Task A_resolved_booking_without_payout_due_at_falls_due_at_its_resolution_time()
    {
        var id = await PayJob.Seed(h, BookingStatus.Resolved, "release", setDue: false); // T-04.07c sets no payout_due_at on resolve
        Assert.Equal(1, await PayJob.Job(h, provider).PayDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Single(await PayJob.PayoutLedger(h, id));
    }

    [Fact]
    public async Task A_provider_failure_marks_Failed_and_a_later_run_pays_with_the_same_key_and_one_ledger_entry()
    {
        var id = await PayJob.Seed(h);
        var job = PayJob.Job(h, provider);
        provider.FailNext = 1;

        await Assert.ThrowsAsync<JobFailedException>(() => job.RunOnceAsync(h.Clock.GetUtcNow(), default));
        var failed = Assert.Single(await PayJob.PayoutRows(h, id));
        Assert.Equal(PayoutStatus.Failed, failed.Status);
        Assert.Null(failed.ProviderRef);
        Assert.Empty(await PayJob.PayoutLedger(h, id));

        h.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, await job.PayDueAsync(h.Clock.GetUtcNow(), default));

        var row = Assert.Single(await PayJob.PayoutRows(h, id));
        Assert.Equal(PayoutStatus.Sent, row.Status);
        Assert.Equal(failed.Id, row.Id); // the same row, not a second one
        Assert.Equal(2, provider.Payouts.Count);
        Assert.All(provider.Payouts, r => Assert.Equal(PayJob.Key(id), r.IdempotencyKey));
        Assert.Single(await PayJob.PayoutLedger(h, id));
    }

    [Fact]
    public async Task A_failed_payout_waits_for_its_backoff_and_is_retried_at_most_five_times_in_all()
    {
        await PayJob.Seed(h);
        var job = PayJob.Job(h, provider);
        provider.FailNext = int.MaxValue;

        await Assert.ThrowsAsync<JobFailedException>(() => job.RunOnceAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(0, await job.PayDueAsync(h.Clock.GetUtcNow(), default)); // immediately again: backing off
        Assert.Single(provider.Payouts);

        for (var i = 0; i < 12; i++)
        {
            h.Clock.Advance(TimeSpan.FromMinutes(30));
            try { await job.PayDueAsync(h.Clock.GetUtcNow(), default); } catch (JobFailedException) { }
        }
        Assert.Equal(5, provider.Payouts.Count); // 1 first attempt + 4 retries, then it stays Failed for an operator
    }

    [Fact]
    public async Task A_Pending_row_whose_lease_ran_out_is_recovered_with_the_same_key_and_a_leased_one_is_left_to_its_owner()
    {
        var stale = await PayJob.Seed(h);
        var fresh = await PayJob.Seed(h);
        await h.Seed(db =>
        {
            db.Payouts.Add(new Payout { Id = Guid.NewGuid(), BookingId = stale, PractitionerId = h.Practitioner, AmountMinor = 8500, Status = PayoutStatus.Pending, CreatedAt = h.Clock.GetUtcNow() - TimeSpan.FromMinutes(30), AttemptCount = 1, NextAttemptAt = h.Clock.GetUtcNow() - TimeSpan.FromMinutes(28) });
            db.Payouts.Add(new Payout { Id = Guid.NewGuid(), BookingId = fresh, PractitionerId = h.Practitioner, AmountMinor = 8500, Status = PayoutStatus.Pending, CreatedAt = h.Clock.GetUtcNow() - TimeSpan.FromSeconds(5), AttemptCount = 1, NextAttemptAt = h.Clock.GetUtcNow() + TimeSpan.FromSeconds(115) });
        }); // a crash after the row was created, or after the provider call and before the second transaction

        Assert.Equal(1, await PayJob.Job(h, provider).PayDueAsync(h.Clock.GetUtcNow(), default));

        Assert.Equal(PayJob.Key(stale), Assert.Single(provider.Payouts).IdempotencyKey);
        Assert.Equal(PayoutStatus.Sent, Assert.Single(await PayJob.PayoutRows(h, stale)).Status);
        Assert.Single(await PayJob.PayoutLedger(h, stale));
        Assert.Equal(PayoutStatus.Pending, Assert.Single(await PayJob.PayoutRows(h, fresh)).Status);
        Assert.Empty(await PayJob.PayoutLedger(h, fresh));
    }

    [Fact]
    public async Task A_Sent_row_is_never_paid_again_and_never_gets_a_second_ledger_entry()
    {
        var id = await PayJob.Seed(h);
        var job = PayJob.Job(h, provider);
        await job.PayDueAsync(h.Clock.GetUtcNow(), default);
        h.Clock.Advance(TimeSpan.FromDays(3));
        Assert.Equal(0, await job.PayAsync([id], h.Clock.GetUtcNow(), default));
        Assert.Single(provider.Payouts);
        Assert.Single(await PayJob.PayoutLedger(h, id));
    }

    [Fact]
    public async Task An_instance_that_finishes_the_same_payout_during_our_provider_call_leaves_exactly_one_ledger_entry()
    {
        var id = await PayJob.Seed(h);
        provider.Before = async _ => await h.Seed(db => // what a second instance does between our step 1 and step 3: same key, same provider result, its own step 3
        {
            db.Payouts.Single(x => x.BookingId == id).Status = PayoutStatus.Sent;
            db.LedgerEntries.Add(new LedgerEntry { Id = Guid.NewGuid(), BookingId = id, Kind = LedgerKind.PractitionerPayout, AmountMinor = 8500, CreatedAt = h.Clock.GetUtcNow() });
        });

        Assert.Equal(0, await PayJob.Job(h, provider).PayDueAsync(h.Clock.GetUtcNow(), default)); // not ours to count: the other instance completed it

        Assert.Equal(PayoutStatus.Sent, Assert.Single(await PayJob.PayoutRows(h, id)).Status);
        Assert.Single(await PayJob.PayoutLedger(h, id));
    }

    [Fact]
    public async Task Two_job_instances_at_once_pay_each_booking_exactly_once_whatever_the_interleaving()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 8; i++) ids.Add(await PayJob.Seed(h));
        provider.Before = async _ => await Task.Yield();
        var (a, b) = (PayJob.Job(h, provider), PayJob.Job(h, provider));
        var now = h.Clock.GetUtcNow();

        var paid = await Task.WhenAll(Task.Run(() => a.PayDueAsync(now, default)), Task.Run(() => b.PayDueAsync(now, default)));

        Assert.Equal(ids.Count, paid.Sum()); // every payout was completed by exactly one of the two
        foreach (var id in ids)
        {
            Assert.Equal(PayoutStatus.Sent, Assert.Single(await PayJob.PayoutRows(h, id)).Status);
            Assert.Single(await PayJob.PayoutLedger(h, id));
        }
        Assert.All(provider.Payouts, r => Assert.Contains(r.IdempotencyKey, ids.Select(PayJob.Key)));
        Assert.Equal(ids.Count, provider.Payouts.Select(r => r.IdempotencyKey).Distinct().Count()); // a single provider key per booking
    }

    [Fact]
    public async Task The_provider_is_never_called_while_a_database_scope_or_transaction_is_open_and_gets_a_cancellable_token()
    {
        await PayJob.Seed(h);
        await PayJob.Seed(h, BookingStatus.ClientNoShow);
        provider.FailNext = 1;
        var job = PayJob.Job(h, provider);
        await Assert.ThrowsAsync<JobFailedException>(() => job.RunOnceAsync(h.Clock.GetUtcNow(), default));

        Assert.NotEmpty(provider.Payouts); // not vacuous
        Assert.Empty(provider.Violations);
        Assert.All(provider.Cancellable, Assert.True); // the 10 s budget (SPEC 5.1) rides on this token
    }

    [Fact]
    public async Task Logs_carry_the_booking_id_and_never_the_key_the_practitioner_or_the_user_ids()
    {
        var id = await PayJob.Seed(h);
        provider.FailNext = 1;
        var job = PayJob.Job(h, provider);
        await Assert.ThrowsAsync<JobFailedException>(() => job.RunOnceAsync(h.Clock.GetUtcNow(), default));
        h.Clock.Advance(TimeSpan.FromMinutes(10));
        await job.RunOnceAsync(h.Clock.GetUtcNow(), default);

        var all = string.Join("\n", h.Logs.Lines.Where(l => !l.StartsWith("Microsoft")));
        Assert.Contains(id.ToString(), all);
        Assert.Contains("HttpRequestException", all);
        foreach (var forbidden in new[] { "payout:", id.ToString("N"), "provider down", "mockpayout", h.Practitioner.ToString(), h.OwnerUserId, Life.Client, "demo-brief" })
            Assert.DoesNotContain(forbidden, all);
    }

    [Fact]
    public async Task The_cycle_records_the_job_run_with_the_payout_label_and_the_job_is_registered_by_its_own_feature()
    {
        using var capture = new MetricCapture(h.Services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        await PayJob.Seed(h);
        Assert.True(await PayJob.Job(h, provider).RunCycleAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(1, capture.Runs("payout", "ok"));

        var job = h.Services.GetRequiredService<PayoutJob>();
        Assert.Single(h.Services.GetServices<IHostedService>(), s => ReferenceEquals(s, job));
        Assert.Equal("payout", job.Name);
    }

    [Fact]
    public async Task The_attempt_bound_lives_in_the_row_so_a_restart_does_not_reset_it_and_a_payout_that_used_its_attempts_is_not_called_again()
    {
        var id = await PayJob.Seed(h);
        provider.FailNext = int.MaxValue;
        for (var i = 0; i < 8; i++)
        {
            // a new job object every time: nothing is remembered in memory, as after a restart or in a second instance
            try { await PayJob.Job(h, provider).PayDueAsync(h.Clock.GetUtcNow(), default); } catch (JobFailedException) { }
            h.Clock.Advance(TimeSpan.FromHours(3)); // past every backoff
        }
        Assert.Equal(5, provider.Payouts.Count);
        var row = Assert.Single(await PayJob.PayoutRows(h, id));
        Assert.Equal((PayoutStatus.Failed, 5), (row.Status, row.AttemptCount));
        Assert.Contains(h.Logs.Lines, l => l.Contains($"{id}") && l.Contains("failed 5 times"));
        Assert.Single(h.Logs.Lines, l => l.Contains("failed 5 times")); // said once
        Assert.DoesNotContain(h.Logs.Lines, l => l.Contains(PayJob.Key(id)));
    }

    [Fact]
    public async Task A_failure_sets_the_backoff_in_the_row_and_the_next_attempt_is_not_before_it()
    {
        var id = await PayJob.Seed(h);
        provider.FailNext = 1;
        var t0 = h.Clock.GetUtcNow();
        await Assert.ThrowsAsync<JobFailedException>(() => PayJob.Job(h, provider).PayDueAsync(t0, default));
        var row = Assert.Single(await PayJob.PayoutRows(h, id));
        Assert.Equal((PayoutStatus.Failed, 1, t0 + TimeSpan.FromSeconds(60)), (row.Status, row.AttemptCount, row.NextAttemptAt));

        Assert.Equal(0, await PayJob.Job(h, provider).PayDueAsync(t0 + TimeSpan.FromSeconds(59), default)); // a fresh job object still waits
        Assert.Single(provider.Payouts);
        Assert.Equal(1, await PayJob.Job(h, provider).PayDueAsync(t0 + TimeSpan.FromSeconds(60), default));
        var done = Assert.Single(await PayJob.PayoutRows(h, id));
        Assert.Equal((PayoutStatus.Sent, 2, (DateTimeOffset?)null), (done.Status, done.AttemptCount, done.NextAttemptAt));
        Assert.Equal(PayJob.Key(id), provider.Payouts[1].IdempotencyKey); // the same key on the retry
    }

    [Fact]
    public async Task A_payout_that_is_being_made_is_leased_so_a_second_job_does_not_call_the_provider_too()
    {
        var id = await PayJob.Seed(h);
        var second = PayJob.Job(h, provider);
        var secondResult = 0;
        provider.Before = async _ => { secondResult = await second.PayDueAsync(h.Clock.GetUtcNow(), default); await Task.CompletedTask; }; // inside the first job's provider call
        Assert.Equal(1, await PayJob.Job(h, provider).PayDueAsync(h.Clock.GetUtcNow(), default));
        provider.Before = null;
        Assert.Equal(0, secondResult);
        Assert.Equal(PayJob.Key(id), Assert.Single(provider.Payouts).IdempotencyKey);
    }

    [Fact]
    public async Task A_completed_booking_is_not_paid_before_the_claim_window_has_closed()
    {
        // payout_due_at = max(end + 48 h, start + 72 h): at end + 48 h and one hour after start + 72 h the money has not moved yet (ADR proposal 0011)
        var id = await Life.Seed(h, BookingStatus.Completed, TimeSpan.FromHours(-1)); // starts one hour ago, ends now
        await h.Seed(db =>
        {
            var b = db.Bookings.Single(x => x.Id == id);
            (b.FeeMinor, b.PayoutMinor) = (1500, 8500);
            b.PayoutDueAt = AutoCompleteJob.PayoutDueAt(b.StartUtc, b.EndUtc);
        });
        var b0 = await Life.Row(h, id);
        Assert.Equal(b0.StartUtc + TimeSpan.FromHours(72), b0.PayoutDueAt);
        h.Clock.Advance(TimeSpan.FromHours(48)); // end + 48 h: the completion moment, still before start + 72 h
        Assert.Equal(0, await PayJob.Job(h, provider).PayDueAsync(h.Clock.GetUtcNow(), default));
        h.Clock.Advance(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(59)); // one minute before start + 72 h (the session started an hour ago, so that is 71 h from now)
        Assert.Equal(0, await PayJob.Job(h, provider).PayDueAsync(h.Clock.GetUtcNow(), default));
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await PayJob.Job(h, provider).PayDueAsync(h.Clock.GetUtcNow(), default));
    }

    [Theory]
    [InlineData(1, 0, 72)]     // a one hour session: start + 72 h
    [InlineData(0.5, 0, 72)]
    [InlineData(30, 0, 78)]   // longer than a day: end + 48 h is the later moment
    public void The_payout_is_due_at_the_later_of_end_plus_48_h_and_start_plus_72_h(double hours, double _, double expectedHoursAfterStart)
    {
        var start = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(start + TimeSpan.FromHours(expectedHoursAfterStart), AutoCompleteJob.PayoutDueAt(start, start + TimeSpan.FromHours(hours)));
    }
}
