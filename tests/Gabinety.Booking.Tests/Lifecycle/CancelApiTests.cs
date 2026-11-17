using System.Net;
using Gabinety.Billing;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Booking.Features.Payments;
using Gabinety.Contracts.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

public class CancelApiTests
{
    static readonly TimeSpan H24 = TimeSpan.FromHours(24);

    static async Task<string> Code(HttpResponseMessage r) => (await r.Body()).GetProperty("code").GetString()!;

    [Fact]
    public async Task A_client_cancel_exactly_24h_before_start_refunds_in_full()
    {
        var p = new RecordingProvider();
        await using var h = Life.Host(p);
        var id = await Life.Seed(h, BookingStatus.Confirmed, H24);
        var r = await Life.Cancel(h, id, Life.Client);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var view = await r.Body();
        Assert.Equal("CancelledByClient", view.GetProperty("status").GetString());
        Assert.Equal(10000, view.GetProperty("payment").GetProperty("refunded").GetProperty("amountMinor").GetInt64());
        Assert.Equal("Refunded", view.GetProperty("payment").GetProperty("status").GetString());

        var row = await Life.Row(h, id);
        Assert.Equal(BookingStatus.CancelledByClient, row.Status);
        Assert.Equal(h.Clock.GetUtcNow(), row.CancelledAt);
        Assert.Null(row.PayoutDueAt);
        var pay = await Life.PaymentOf(h, id);
        Assert.Equal(PaymentStatus.Refunded, pay.Status);
        Assert.Equal(10000, pay.RefundedMinor);
        var refunds = (await Life.Ledger(h, id)).Where(l => l.Kind == LedgerKind.Refund).ToList();
        Assert.Equal(10000, Assert.Single(refunds).AmountMinor);
        var asked = Assert.Single(p.Refunds);
        Assert.Equal(10000, asked.AmountMinor);
        Assert.Equal("pay_" + id.ToString("N"), asked.ProviderPaymentId);
        Assert.Contains(id.ToString("N"), asked.IdempotencyKey);
    }

    [Fact]
    public async Task A_client_cancel_1s_inside_24h_is_late_with_no_refund_and_payout_due_at_start()
    {
        var p = new RecordingProvider();
        await using var h = Life.Host(p);
        var id = await Life.Seed(h, BookingStatus.Confirmed, H24 - TimeSpan.FromSeconds(1));
        var r = await Life.Cancel(h, id, Life.Client);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("LateCancelledByClient", (await r.Body()).GetProperty("status").GetString());

        var row = await Life.Row(h, id);
        Assert.Equal(BookingStatus.LateCancelledByClient, row.Status);
        Assert.Equal(row.StartUtc, row.PayoutDueAt);
        Assert.Equal(h.Clock.GetUtcNow(), row.CancelledAt);
        Assert.Empty(p.Refunds);
        var pay = await Life.PaymentOf(h, id);
        Assert.Equal(PaymentStatus.Succeeded, pay.Status);
        Assert.Equal(0, pay.RefundedMinor);
        Assert.DoesNotContain(await Life.Ledger(h, id), l => l.Kind == LedgerKind.Refund);
    }

    [Theory]
    [InlineData(60 * 60 * 24 * 30)] // a month ahead
    [InlineData(60 * 60 * 2)]       // two hours ahead
    [InlineData(-30 * 60)]          // in progress
    [InlineData(-(60 * 60 - 1))]    // 1 s before the end of a 1 h session that started 59 m 59 s ago
    public async Task A_practitioner_cancel_always_refunds_in_full_before_the_end(int startInSeconds)
    {
        var p = new RecordingProvider();
        await using var h = Life.Host(p);
        var id = await Life.Seed(h, BookingStatus.Confirmed, TimeSpan.FromSeconds(startInSeconds));
        var r = await Life.Cancel(h, id, Life.Practitioner, "demo-reason");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("CancelledByPractitioner", (await r.Body()).GetProperty("status").GetString());
        var pay = await Life.PaymentOf(h, id);
        Assert.Equal((PaymentStatus.Refunded, 10000L), (pay.Status, pay.RefundedMinor));
        Assert.Equal(10000, Assert.Single(p.Refunds).AmountMinor);
        Assert.Equal(10000, Assert.Single(await Life.RefundRows(h, id)).AmountMinor);
        Assert.Null((await Life.Row(h, id)).PayoutDueAt);
    }

    [Fact]
    public async Task A_practitioner_cancel_at_or_after_the_end_is_409()
    {
        var p = new RecordingProvider();
        await using var h = Life.Host(p);
        var id = await Life.Seed(h, BookingStatus.Confirmed, -TimeSpan.FromHours(1)); // ends exactly now
        var r = await Life.Cancel(h, id, Life.Practitioner);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("invalid_transition", await Code(r));
        Assert.Empty(p.Refunds);
        Assert.Equal(BookingStatus.Confirmed, (await Life.Row(h, id)).Status);
    }

    [Fact]
    public async Task A_third_party_or_a_stranger_gets_404_and_nothing_changes()
    {
        var p = new RecordingProvider();
        await using var h = Life.Host(p);
        var id = await Life.Seed(h, BookingStatus.Confirmed, H24);
        var r = await Life.Cancel(h, id, "demo-someone-else");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("not_found", await Code(r));
        Assert.Equal(HttpStatusCode.NotFound, (await Life.Cancel(h, Guid.NewGuid(), Life.Client)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Life.Cancel(h, id, null)).StatusCode);
        Assert.Empty(p.Refunds);
        Assert.Equal(BookingStatus.Confirmed, (await Life.Row(h, id)).Status);
    }

    [Theory]
    [MemberData(nameof(IllegalPairs))]
    public async Task Cancelling_from_any_status_but_Confirmed_is_409_invalid_transition(BookingStatus status, bool byClient)
    {
        var p = new RecordingProvider();
        await using var h = Life.Host(p);
        var id = await Life.Seed(h, status, H24 * 2);
        var r = await Life.Cancel(h, id, byClient ? Life.Client : Life.Practitioner);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("invalid_transition", await Code(r));
        Assert.Empty(p.Refunds);
        Assert.Equal(status, (await Life.Row(h, id)).Status);
        Assert.DoesNotContain(await Life.Ledger(h, id), l => l.Kind == LedgerKind.Refund);
    }

    public static TheoryData<BookingStatus, bool> IllegalPairs()
    {
        var d = new TheoryData<BookingStatus, bool>();
        foreach (var s in Enum.GetValues<BookingStatus>().Where(s => s != BookingStatus.Confirmed))
        { d.Add(s, true); d.Add(s, false); }
        return d; // nine statuses, both callers; the tenth (Confirmed) is the legal one covered above
    }

    [Fact]
    public async Task The_refund_is_requested_once_and_a_repeated_cancel_is_409()
    {
        var p = new RecordingProvider();
        await using var h = Life.Host(p);
        var id = await Life.Seed(h, BookingStatus.Confirmed, H24);
        Assert.Equal(HttpStatusCode.OK, (await Life.Cancel(h, id, Life.Client)).StatusCode);
        var again = await Life.Cancel(h, id, Life.Client);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Life.Cancel(h, id, Life.Practitioner)).StatusCode);
        Assert.Single(p.Refunds);
        Assert.Single(await Life.RefundRows(h, id));
    }

    [Fact]
    public async Task The_idempotency_key_is_stable_per_booking_and_reason_and_a_replayed_provider_confirmation_does_not_double_record()
    {
        var p = new RecordingProvider();
        await using var h = Life.Host(p);
        var id = await Life.Seed(h, BookingStatus.Confirmed, H24);
        Assert.Equal(HttpStatusCode.OK, (await Life.Cancel(h, id, Life.Client)).StatusCode);
        var key = Assert.Single(p.Refunds).IdempotencyKey;
        Assert.Equal(key, RefundCoordinator.IdempotencyKey(id, RefundReason.ClientCancelledEarly));
        Assert.NotEqual(key, RefundCoordinator.IdempotencyKey(id, RefundReason.PractitionerCancelled));
        Assert.NotEqual(key, RefundCoordinator.IdempotencyKey(Guid.NewGuid(), RefundReason.ClientCancelledEarly));

        // The provider's own RefundSucceeded webhook arrives after the Mock already recorded: it changes nothing.
        var body = Pay.EventJson("evt_demo_refund_1", "RefundSucceeded", "pay_" + id.ToString("N"), 10000);
        Assert.Equal(HttpStatusCode.OK, (await Pay.Webhook(h.ClientFor(null), body, null)).StatusCode);
        Assert.Single(await Life.RefundRows(h, id));
        Assert.Equal(10000, (await Life.PaymentOf(h, id)).RefundedMinor);
    }

    [Fact]
    public async Task For_a_provider_other_than_Mock_the_ledger_row_waits_for_the_RefundSucceeded_webhook()
    {
        var p = new RecordingProvider("Acme", "whsec-demo");
        await using var h = Life.Host(p, x => x.Settings["Payments:WebhookSecret"] = "whsec-demo");
        var id = await Life.Seed(h, BookingStatus.Confirmed, H24, provider: "Acme");
        var r = await Life.Cancel(h, id, Life.Practitioner);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Single(p.Refunds);
        Assert.Equal(BookingStatus.CancelledByPractitioner, (await Life.Row(h, id)).Status);
        Assert.Equal(0, (await Life.PaymentOf(h, id)).RefundedMinor);
        Assert.DoesNotContain(await Life.Ledger(h, id), l => l.Kind == LedgerKind.Refund);

        var body = Pay.EventJson("evt_demo_refund_2", "RefundSucceeded", "pay_" + id.ToString("N"), 10000);
        Assert.Equal(HttpStatusCode.OK, (await Pay.Webhook(h.ClientFor(null), body, Pay.Sign(body, "whsec-demo"), "acme")).StatusCode);
        Assert.Equal((PaymentStatus.Refunded, 10000L), ((await Life.PaymentOf(h, id)).Status, (await Life.PaymentOf(h, id)).RefundedMinor));
        Assert.Single(await Life.RefundRows(h, id));
    }

    [Fact]
    public async Task The_reason_is_trimmed_stored_and_never_logged()
    {
        const string secret = "demo-private-reason-ZXQ-98765";
        var p = new RecordingProvider();
        await using var h = Life.Host(p);
        var id = await Life.Seed(h, BookingStatus.Confirmed, H24);
        var r = await Life.Cancel(h, id, Life.Client, $"   {secret}   ");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(secret, (await Life.Row(h, id)).StatusReason);

        // A rejected cancel (409) and a too-long reason must not leak it either.
        var id2 = await Life.Seed(h, BookingStatus.Completed, H24);
        Assert.Equal(HttpStatusCode.Conflict, (await Life.Cancel(h, id2, Life.Client, secret)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Life.Cancel(h, id2, "demo-other", secret)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Life.Cancel(h, id, Life.Client, secret + new string('x', 500))).StatusCode);
        Assert.DoesNotContain(h.Logs.Lines, l => l.Contains("ZXQ-98765"));
        Assert.Contains(h.Logs.Lines, l => l.Contains(id.ToString())); // the transition itself is logged, by id
    }

    [Fact]
    public async Task A_blank_reason_is_stored_as_null_and_a_500_char_reason_is_accepted()
    {
        await using var h = Life.Host(new RecordingProvider());
        var a = await Life.Seed(h, BookingStatus.Confirmed, H24);
        var b = await Life.Seed(h, BookingStatus.Confirmed, H24);
        Assert.Equal(HttpStatusCode.OK, (await Life.Cancel(h, a, Life.Client, "   ")).StatusCode);
        Assert.Null((await Life.Row(h, a)).StatusReason);
        Assert.Equal(HttpStatusCode.OK, (await Life.Cancel(h, b, Life.Client, new string('r', 500))).StatusCode);
        Assert.Equal(500, (await Life.Row(h, b)).StatusReason!.Length);
    }

    [Fact]
    public async Task A_cancel_with_no_body_at_all_works()
    {
        await using var h = Life.Host(new RecordingProvider());
        var id = await Life.Seed(h, BookingStatus.Confirmed, H24);
        var r = await h.ClientFor(Life.Client).PostAsync($"/api/v1/bookings/{id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task The_full_path_works_from_a_booking_created_and_paid_through_the_API()
    {
        var p = new RecordingProvider();
        await using var h = Life.Host(p);
        await h.WorkWeek();
        var (id, _) = await Pay.CreateBooking(h, Life.Client); // Tuesday 10:00 UTC, 22 h away
        Assert.Equal(HttpStatusCode.OK, (await Pay.Simulate(h.ClientFor(Life.Client), id, "succeeded")).StatusCode);
        var r = await Life.Cancel(h, id, Life.Client);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("LateCancelledByClient", (await r.Body()).GetProperty("status").GetString()); // 22 h < 24 h
        Assert.Empty(p.Refunds);
    }

    [Fact]
    public async Task Transitions_run_the_registered_hooks_inside_the_transaction_and_a_failing_hook_rolls_everything_back()
    {
        var hook = new CapturingHook();
        var p = new RecordingProvider();
        await using var h = Life.Host(p, x => x.Configure += s => s.AddSingleton<IBookingTransitionHook>(hook));
        var id = await Life.Seed(h, BookingStatus.Confirmed, H24);
        Assert.Equal(HttpStatusCode.OK, (await Life.Cancel(h, id, Life.Client)).StatusCode);
        var seen = Assert.Single(hook.Seen);
        Assert.Equal((id, BookingStatus.Confirmed, BookingStatus.CancelledByClient, BookingTrigger.ClientCancels), seen);

        hook.Fail = true;
        var id2 = await Life.Seed(h, BookingStatus.Confirmed, H24);
        await Assert.ThrowsAnyAsync<Exception>(async () => { var r = await Life.Cancel(h, id2, Life.Client); r.EnsureSuccessStatusCode(); });
        Assert.Equal(BookingStatus.Confirmed, (await Life.Row(h, id2)).Status);
    }

    sealed class CapturingHook : IBookingTransitionHook
    {
        public List<(Guid, BookingStatus, BookingStatus, BookingTrigger)> Seen { get; } = [];
        public bool Fail { get; set; }
        public Task OnTransitionAsync(BookingTransitionContext c, CancellationToken ct)
        {
            if (Fail) throw new InvalidOperationException("demo hook failure");
            Seen.Add((c.Booking.Id, c.From, c.To, c.Trigger));
            return Task.CompletedTask;
        }
    }
}
