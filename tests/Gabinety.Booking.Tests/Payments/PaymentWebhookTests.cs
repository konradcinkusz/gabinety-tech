using System.Net;
using BookingStatus = Gabinety.Billing.BookingStatus;
using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Booking.Tests;

public class PaymentWebhookTests
{
    const string Client = "demo-client-1";

    static async Task<CreateHost> Host(Action<CreateHost>? configure = null, bool secret = true)
    {
        var h = new CreateHost { MeetingUrl = "https://meet.demo.invalid/room-1" };
        if (secret) h.Settings["Payments:WebhookSecret"] = Pay.Secret;
        configure?.Invoke(h);
        await h.WorkWeek();
        return h;
    }

    static async Task<string> Outcome(HttpResponseMessage r) => (await r.Body()).GetProperty("outcome").GetString()!;
    static Task<Gabinety.Booking.Features.Bookings.Entities.Booking> Booking(CreateHost h, Guid id) => h.Query(db => db.Bookings.AsNoTracking().SingleAsync(b => b.Id == id));
    static Task<List<Gabinety.Booking.Features.Payments.LedgerEntry>> Ledger(CreateHost h, Guid id) => h.Query(db => db.LedgerEntries.AsNoTracking().Where(l => l.BookingId == id).ToListAsync());

    [Fact]
    public async Task A_valid_signature_confirms_the_booking_snapshots_the_meeting_url_and_writes_the_ClientCharge()
    {
        await using var h = await Host();
        var (id, pid) = await Pay.CreateBooking(h);
        var body = Pay.EventJson("evt_demo_1", "PaymentSucceeded", pid);
        var r = await Pay.Webhook(h.ClientFor(null), body, Pay.Sign(body));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Applied", await Outcome(r));
        var b = await Booking(h, id);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Equal("https://meet.demo.invalid/room-1", b.MeetingUrlSnapshot);
        Assert.Equal(h.Clock.GetUtcNow(), b.ConfirmedAt);
        Assert.Equal(PaymentStatus.Succeeded, (await h.Query(db => db.Payments.SingleAsync(p => p.BookingId == id))).Status);
        var ledger = Assert.Single(await Ledger(h, id));
        Assert.Equal((LedgerKind.ClientCharge, 10000L), (ledger.Kind, ledger.AmountMinor));
        Assert.Equal(Features.Payments.WebhookOutcome.Applied, WebhookOutcomeOf(h, "evt_demo_1"));
    }

    static Features.Payments.WebhookOutcome WebhookOutcomeOf(CreateHost h, string eventId) =>
        h.Query(db => db.WebhookEvents.Where(x => x.EventId == eventId).Select(x => x.Outcome).SingleAsync()).GetAwaiter().GetResult();

    [Fact]
    public async Task An_altered_body_or_a_wrong_or_missing_signature_is_401_and_changes_nothing()
    {
        await using var h = await Host();
        var (id, pid) = await Pay.CreateBooking(h);
        var body = Pay.EventJson("evt_demo_2", "PaymentSucceeded", pid);
        var altered = Pay.EventJson("evt_demo_2", "PaymentSucceeded", pid, amount: 1);
        var c = h.ClientFor(null);
        foreach (var r in new[] { await Pay.Webhook(c, altered, Pay.Sign(body)), await Pay.Webhook(c, body, Pay.Sign(body, "another-secret")), await Pay.Webhook(c, body, null), await Pay.Webhook(c, body, "not-hex") })
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal(BookingStatus.PendingPayment, (await Booking(h, id)).Status);
        Assert.Empty(await Ledger(h, id));
        Assert.Empty(await h.Query(db => db.WebhookEvents.Where(x => x.EventId == "evt_demo_2").ToListAsync()));
    }

    /// <summary>The Error entries of the webhook feature: what an alert would be wired to (SPEC 8).</summary>
    static List<(Microsoft.Extensions.Logging.LogLevel Level, int EventId, string Line)> WebhookErrors(CreateHost h) =>
        [.. h.Logs.Entries.Where(e => e.Line.StartsWith("Gabinety.Booking.Features.Payments.PaymentWebhookFeature:") && e.Level == Microsoft.Extensions.Logging.LogLevel.Error)];

    [Fact]
    public async Task A_signature_failure_is_an_Error_alert_with_a_stable_event_id_and_the_provider_event_id_and_still_401()
    {
        await using var h = await Host();
        var (_, pid) = await Pay.CreateBooking(h);
        var body = Pay.EventJson("evt_demo_sig-1", "PaymentSucceeded", pid);
        var r = await Pay.Webhook(h.ClientFor(null), body, Pay.Sign(body, "another-secret"));

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        var e = Assert.Single(WebhookErrors(h));
        Assert.Equal(Features.Payments.PaymentWebhookFeature.SignatureFailedEventId, e.EventId);
        Assert.Contains("ALERT", e.Line);
        Assert.Contains("evt_demo_sig-1", e.Line);
        Assert.DoesNotContain(h.Logs.Entries, x => x.Level == Microsoft.Extensions.Logging.LogLevel.Warning && x.Line.Contains("signature is invalid"));
    }

    [Theory]
    [InlineData("""{"eventId":"evt\nforged ALERT line","kind":"PaymentSucceeded"}""")]            // a newline: log forging
    [InlineData("""{"eventId":"evt_trailing\n","kind":"PaymentSucceeded"}""")]                      // a trailing newline must not pass as "end of string"
    [InlineData("""{"eventId":"evt demo with spaces","kind":"PaymentSucceeded"}""")]
    [InlineData("""{"eventId":"<script>alert(1)</script>","kind":"PaymentSucceeded"}""")]
    [InlineData("""{"eventId":"evt_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","kind":"x"}""")] // longer than 64
    [InlineData("""{"eventId":"","kind":"PaymentSucceeded"}""")]
    [InlineData("""{"eventId":12345,"kind":"PaymentSucceeded"}""")]                              // not a string
    [InlineData("""{"kind":"PaymentSucceeded"}""")]
    [InlineData("""[1,2,3]""")]
    [InlineData("""not json at all {""")]
    public async Task An_unsafe_or_unreadable_event_id_is_replaced_by_a_fixed_marker_and_nothing_of_the_body_is_logged(string body)
    {
        await using var h = await Host();
        const string sig = "deadbeef-SIGNATURE-NOT-TO-LOG";
        var r = await Pay.Webhook(h.ClientFor(null), body, sig);

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        var e = Assert.Single(WebhookErrors(h));
        Assert.Equal(Features.Payments.PaymentWebhookFeature.SignatureFailedEventId, e.EventId);
        Assert.Contains(Features.Payments.PaymentWebhookFeature.NoEventIdMarker, e.Line);
        Assert.DoesNotContain("forged", e.Line);
        Assert.DoesNotContain("evt_trailing", e.Line);
        Assert.DoesNotContain("script", e.Line);
        Assert.DoesNotContain("AAAAAAAA", e.Line);
        Assert.DoesNotContain("spaces", e.Line);
        Assert.DoesNotContain("12345", e.Line);
        Assert.DoesNotContain("PaymentSucceeded", e.Line);
        Assert.DoesNotContain("SIGNATURE-NOT-TO-LOG", string.Join('\n', h.Logs.Lines));
        Assert.DoesNotContain("another-secret", string.Join('\n', h.Logs.Lines));
        Assert.DoesNotContain(Pay.Secret, string.Join('\n', h.Logs.Lines));
    }

    [Fact]
    public async Task Other_fields_of_an_unverified_body_never_reach_a_log()
    {
        await using var h = await Host();
        var body = """{"eventId":"evt_demo_pii","brief":"BRIEF-NIE-DO-LOGU","email":"demo@example.invalid","kind":"PaymentSucceeded"}""";
        Assert.Equal(HttpStatusCode.Unauthorized, (await Pay.Webhook(h.ClientFor(null), body, Pay.Sign(body, "another-secret"))).StatusCode);
        var all = string.Join('\n', h.Logs.Lines);
        Assert.Contains("evt_demo_pii", all);
        Assert.DoesNotContain("BRIEF-NIE-DO-LOGU", all);
        Assert.DoesNotContain("demo@example.invalid", all);
    }

    [Fact]
    public async Task A_valid_signature_logs_no_signature_alert()
    {
        await using var h = await Host();
        var (_, pid) = await Pay.CreateBooking(h);
        var body = Pay.EventJson("evt_demo_ok", "PaymentSucceeded", pid);
        Assert.Equal(HttpStatusCode.OK, (await Pay.Webhook(h.ClientFor(null), body, Pay.Sign(body))).StatusCode);
        Assert.Empty(WebhookErrors(h));
    }

    [Fact]
    public async Task The_same_event_id_twice_changes_nothing_the_second_time_and_answers_Duplicate()
    {
        await using var h = await Host();
        var (id, pid) = await Pay.CreateBooking(h);
        var body = Pay.EventJson("evt_demo_3", "PaymentSucceeded", pid);
        var c = h.ClientFor(null);
        Assert.Equal("Applied", await Outcome(await Pay.Webhook(c, body, Pay.Sign(body))));
        Assert.Equal("Duplicate", await Outcome(await Pay.Webhook(c, body, Pay.Sign(body))));
        Assert.Single(await Ledger(h, id));
        Assert.Single(await h.Query(db => db.WebhookEvents.Where(x => x.EventId == "evt_demo_3").ToListAsync()));
    }

    [Fact]
    public async Task An_amount_or_currency_mismatch_is_Rejected_recorded_and_raises_an_alert_log()
    {
        await using var h = await Host();
        var (id, pid) = await Pay.CreateBooking(h);
        var c = h.ClientFor(null);
        foreach (var (evt, amount, currency) in new[] { ("evt_demo_4a", 9999L, "PLN"), ("evt_demo_4b", 10000L, "EUR") })
        {
            var body = Pay.EventJson(evt, "PaymentSucceeded", pid, amount, currency);
            Assert.Equal("Rejected", await Outcome(await Pay.Webhook(c, body, Pay.Sign(body))));
            Assert.Equal(Features.Payments.WebhookOutcome.Rejected, WebhookOutcomeOf(h, evt));
        }
        Assert.Equal(BookingStatus.PendingPayment, (await Booking(h, id)).Status);
        Assert.Empty(await Ledger(h, id));
        Assert.Contains(h.Logs.Lines, l => l.Contains("ALERT") && l.Contains(id.ToString()));
    }

    [Fact]
    public async Task Unsigned_is_accepted_in_Mock_mode_and_rejected_in_any_other_mode()
    {
        await using var mock = await Host(secret: false);
        var (_, pid) = await Pay.CreateBooking(mock);
        var body = Pay.EventJson("evt_demo_5", "PaymentSucceeded", pid);
        Assert.Equal(HttpStatusCode.OK, (await Pay.Webhook(mock.ClientFor(null), body, null)).StatusCode);

        await using var other = await Host(h => h.Configure = s => { s.RemoveAll<Gabinety.Booking.Payments.Abstractions.IPaymentProvider>(); s.AddSingleton<Gabinety.Booking.Payments.Abstractions.IPaymentProvider>(new RecordingProvider("Acme")); }, secret: false);
        var (id, pid2) = await Pay.CreateBooking(other);
        var body2 = Pay.EventJson("evt_demo_5b", "PaymentSucceeded", pid2);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Pay.Webhook(other.ClientFor(null), body2, null, "acme")).StatusCode);
        Assert.Equal(BookingStatus.PendingPayment, (await Booking(other, id)).Status);
    }

    [Fact]
    public async Task Simulate_is_403_unless_the_provider_is_Mock_and_runs_the_same_handler_when_it_is()
    {
        await using var h = await Host(secret: false);
        var (id, _) = await Pay.CreateBooking(h);
        var c = h.ClientFor(Client);
        var r = await Pay.Simulate(c, id, "succeeded");
        Assert.Equal("Applied", await Outcome(r));
        Assert.Equal(BookingStatus.Confirmed, (await Booking(h, id)).Status);
        Assert.Equal("Duplicate", await Outcome(await Pay.Simulate(c, id, "succeeded")));
        Assert.Equal((HttpStatusCode)422, (await Pay.Simulate(c, id, "maybe")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Pay.Simulate(c, Guid.NewGuid(), "succeeded")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Pay.Simulate(h.ClientFor(null), id, "succeeded")).StatusCode);

        var (id2, _) = await Pay.CreateBooking(h, "demo-client-2", "2026-11-17T13:00:00Z");
        Assert.Equal("Applied", await Outcome(await Pay.Simulate(h.ClientFor("demo-client-2"), id2, "failed")));
        Assert.Equal(BookingStatus.Expired, (await Booking(h, id2)).Status);

        await using var other = await Host(x => x.Configure = s => { s.RemoveAll<Gabinety.Booking.Payments.Abstractions.IPaymentProvider>(); s.AddSingleton<Gabinety.Booking.Payments.Abstractions.IPaymentProvider>(new RecordingProvider("Acme")); }, secret: false);
        var (id3, _) = await Pay.CreateBooking(other);
        Assert.Equal(HttpStatusCode.Forbidden, (await Pay.Simulate(other.ClientFor(Client), id3, "succeeded")).StatusCode);
        Assert.Equal(BookingStatus.PendingPayment, (await Booking(other, id3)).Status);
    }

    [Fact]
    public async Task The_meeting_url_is_absent_before_confirmation_and_present_after_for_a_party_only()
    {
        await using var h = await Host(secret: false);
        var (id, _) = await Pay.CreateBooking(h);
        async Task<System.Text.Json.JsonElement> View(string user) => await (await h.ClientFor(user).GetAsync($"/api/v1/bookings/{id}")).Body();

        var before = await View(Client);
        Assert.Equal("PendingPayment", before.GetProperty("status").GetString());
        Assert.False(before.TryGetProperty("meetingUrl", out var m0) && m0.ValueKind == System.Text.Json.JsonValueKind.String);

        await Pay.Simulate(h.ClientFor(Client), id, "succeeded");
        foreach (var party in new[] { Client, h.OwnerUserId })
            Assert.Equal("https://meet.demo.invalid/room-1", (await View(party)).GetProperty("meetingUrl").GetString());

        var stranger = await h.ClientFor("demo-stranger").GetAsync($"/api/v1/bookings/{id}");
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.DoesNotContain("meet.demo.invalid", await stranger.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.ClientFor(null).GetAsync($"/api/v1/bookings/{id}")).StatusCode);

        // The brief reaches the practitioner only once Confirmed, the client always.
        Assert.Equal(System.Text.Json.JsonValueKind.Object, (await View(h.OwnerUserId)).GetProperty("brief").ValueKind);
    }

    [Fact]
    public async Task The_brief_is_hidden_from_the_practitioner_before_confirmation()
    {
        await using var h = await Host(secret: false);
        var (id, _) = await Pay.CreateBooking(h);
        var v = await (await h.ClientFor(h.OwnerUserId).GetAsync($"/api/v1/bookings/{id}")).Body();
        Assert.False(v.TryGetProperty("brief", out var b) && b.ValueKind == System.Text.Json.JsonValueKind.Object);
        var mine = await (await h.ClientFor(Client).GetAsync($"/api/v1/bookings/{id}")).Body();
        Assert.Equal(System.Text.Json.JsonValueKind.Object, mine.GetProperty("brief").ValueKind);
    }

    [Fact]
    public async Task A_payment_that_succeeds_after_the_hold_expired_is_refunded_in_full_and_recorded()
    {
        var provider = new RecordingProvider();
        await using var h = await Host(x => x.Configure = s => { s.RemoveAll<Gabinety.Booking.Payments.Abstractions.IPaymentProvider>(); s.AddSingleton<Gabinety.Booking.Payments.Abstractions.IPaymentProvider>(provider); }, secret: false);
        var (id, pid) = await Pay.CreateBooking(h);
        h.Clock.Advance(TimeSpan.FromMinutes(11)); // past the 10 minute hold, before any job expired it
        var body = Pay.EventJson("evt_demo_6", "PaymentSucceeded", pid);
        Assert.Equal("Applied", await Outcome(await Pay.Webhook(h.ClientFor(null), body, null)));

        Assert.Equal(BookingStatus.Expired, (await Booking(h, id)).Status);
        var refund = Assert.Single(provider.Refunds);
        Assert.Equal((pid, 10000L), (refund.ProviderPaymentId, refund.AmountMinor));
        var payment = await h.Query(db => db.Payments.SingleAsync(p => p.BookingId == id));
        Assert.Equal((PaymentStatus.Refunded, 10000L), (payment.Status, payment.RefundedMinor));
        Assert.Equal([LedgerKind.ClientCharge, LedgerKind.Refund], (await Ledger(h, id)).OrderBy(l => l.Kind).Select(l => l.Kind).ToArray());
        Assert.Equal(2, (await Ledger(h, id)).Count);
        Assert.DoesNotContain(await h.Bookings(), b => b.Id == id && b.Status == BookingStatus.Confirmed);
    }

    [Fact]
    public async Task A_failed_payment_expires_the_booking_and_frees_the_slot()
    {
        await using var h = await Host();
        var (id, pid) = await Pay.CreateBooking(h);
        var body = Pay.EventJson("evt_demo_7", "PaymentFailed", pid, null);
        var r = await Pay.Webhook(h.ClientFor(null), body, Pay.Sign(body));
        Assert.True(r.IsSuccessStatusCode, r.StatusCode + " " + await r.Content.ReadAsStringAsync());
        Assert.Equal("Applied", await Outcome(r));
        Assert.Equal(BookingStatus.Expired, (await Booking(h, id)).Status);
        Assert.Equal(PaymentStatus.Failed, (await h.Query(db => db.Payments.SingleAsync(p => p.BookingId == id))).Status);
        Assert.Equal(HttpStatusCode.Created, (await CreateHost.Post(h.ClientFor("demo-client-2"), h.Req(), CreateHost.Key())).StatusCode);
    }

    [Fact]
    public async Task A_webhook_for_a_booking_that_does_not_exist_yet_is_202_with_Retry_After_and_is_not_recorded()
    {
        await using var h = await Host();
        var body = Pay.EventJson("evt_demo_8", "PaymentSucceeded", "mock_" + Guid.NewGuid().ToString("N"));
        var r = await Pay.Webhook(h.ClientFor(null), body, Pay.Sign(body));
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        Assert.True(r.Headers.RetryAfter is not null);
        Assert.Empty(await h.Query(db => db.WebhookEvents.Where(x => x.EventId == "evt_demo_8").ToListAsync()));
    }

    [Fact]
    public async Task An_unknown_event_is_ignored_and_recorded_Ignored()
    {
        await using var h = await Host();
        var body = Pay.EventJson("evt_demo_9", "SomethingNew", null);
        var r = await Pay.Webhook(h.ClientFor(null), body, Pay.Sign(body));
        Assert.Equal("Ignored", await Outcome(r));
        Assert.Equal(Features.Payments.WebhookOutcome.Ignored, WebhookOutcomeOf(h, "evt_demo_9"));
    }

    [Fact]
    public async Task RefundSucceeded_writes_the_Refund_ledger_rows_up_to_the_payment_amount_and_no_further()
    {
        await using var h = await Host();
        var (id, pid) = await Pay.CreateBooking(h);
        var c = h.ClientFor(null);
        async Task<string> Send(string evt, string kind, long amount) { var b = Pay.EventJson(evt, kind, pid, amount); return await Outcome(await Pay.Webhook(c, b, Pay.Sign(b))); }
        Assert.Equal("Applied", await Send("evt_demo_10a", "PaymentSucceeded", 10000));
        Assert.Equal("Applied", await Send("evt_demo_10b", "RefundSucceeded", 4000));
        Assert.Equal(PaymentStatus.PartiallyRefunded, (await h.Query(db => db.Payments.SingleAsync(p => p.BookingId == id))).Status);
        Assert.Equal("Rejected", await Send("evt_demo_10c", "RefundSucceeded", 7000));
        Assert.Equal("Applied", await Send("evt_demo_10d", "RefundSucceeded", 6000));
        var payment = await h.Query(db => db.Payments.SingleAsync(p => p.BookingId == id));
        Assert.Equal((PaymentStatus.Refunded, 10000L), (payment.Status, payment.RefundedMinor));
        Assert.Equal(10000L, (await Ledger(h, id)).Where(l => l.Kind == LedgerKind.Refund).Sum(l => l.AmountMinor));
    }

    [Fact]
    public async Task A_wrong_provider_in_the_path_is_404_and_a_body_over_64_KB_is_413()
    {
        await using var h = await Host();
        var c = h.ClientFor(null);
        Assert.Equal(HttpStatusCode.NotFound, (await Pay.Webhook(c, "{}", null, "stripe")).StatusCode);
        var big = new string('x', 64 * 1024 + 1);
        Assert.Equal((HttpStatusCode)413, (await Pay.Webhook(c, big, Pay.Sign(big))).StatusCode);
    }

    [Fact]
    public async Task Directory_down_at_confirmation_is_503_with_Retry_After_and_the_event_can_be_retried()
    {
        await using var h = await Host();
        var (id, pid) = await Pay.CreateBooking(h);
        var body = Pay.EventJson("evt_demo_11", "PaymentSucceeded", pid);
        h.Directory.Down = true;
        var r = await Pay.Webhook(h.ClientFor(null), body, Pay.Sign(body));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.True(r.Headers.RetryAfter is not null);
        Assert.Equal(BookingStatus.PendingPayment, (await Booking(h, id)).Status);
        h.Directory.Down = false;
        Assert.Equal("Applied", await Outcome(await Pay.Webhook(h.ClientFor(null), body, Pay.Sign(body))));
    }

    [Fact]
    public async Task The_secret_the_signature_and_the_meeting_url_never_reach_a_log_line()
    {
        await using var h = await Host();
        var (_, pid) = await Pay.CreateBooking(h);
        var body = Pay.EventJson("evt_demo_12", "PaymentSucceeded", pid);
        var sig = Pay.Sign(body);
        var c = h.ClientFor(null);
        await Pay.Webhook(c, body, sig);
        await Pay.Webhook(c, body, "00" + sig[2..]); // an invalid one logs a rejection
        Assert.NotEmpty(h.Logs.Lines);
        Assert.All(h.Logs.Lines, l =>
        {
            Assert.DoesNotContain(Pay.Secret, l);
            Assert.DoesNotContain(sig, l);
            Assert.DoesNotContain("meet.demo.invalid", l);
        });
    }

    [Theory]
    [InlineData("""{"eventId":"e","kind":"PaymentFailed","amountMinor":null,"currency":null}""")]
    [InlineData("""{"eventId":5,"kind":7,"amountMinor":"x"}""")]
    [InlineData("[1,2]")]
    public void A_signed_body_of_an_odd_shape_is_an_event_not_an_exception(string json)
    {
        var raw = System.Text.Encoding.UTF8.GetBytes(json);
        var e = new Gabinety.Booking.Payments.Mock.MockPaymentProvider("http://x", Pay.Secret).VerifyWebhook(raw, Pay.Sign(json));
        Assert.NotNull(e);
        Assert.Null(e.AmountMinor);
    }
}
