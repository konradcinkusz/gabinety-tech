using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Features.Payouts;
using Gabinety.Booking.Jobs.Base;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.Booking.Payments.Stripe;
using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests.StripePayments;

/// <summary>
/// The whole Booking path with the real Stripe adapter over a recorded-shape double of the Stripe API: booking creation, signed webhooks, refunds, payouts and onboarding
/// (T-09.05). What this does NOT show is traffic to Stripe itself: see docs/operations/PAYMENTS-GO-LIVE.md.
/// </summary>
public sealed class StripeHostTests : IDisposable
{
    readonly Handler stripe;
    readonly CreateHost h;
    const string Client = "demo-client-1";
    // The InMemory database is shared by name inside one process: every test gets its own session and event ids, so tests cannot see each other's rows.
    readonly string tag = Guid.NewGuid().ToString("N")[..12];
    readonly string session;

    string Load(string fixture) => Fx.Load(fixture).Replace(Fx.SessionId, session).Replace("evt_1Qx0", $"evt_{tag}_");

    public StripeHostTests()
    {
        session = "cs_test_" + tag;
        stripe = new Handler()
            .On(HttpMethod.Post, "/v1/checkout/sessions", Load("response.checkout.session.create"))
            .On(HttpMethod.Get, $"/v1/checkout/sessions/{session}", Load("response.checkout.session.retrieve.paid.expanded"))
            .On(HttpMethod.Post, "/v1/refunds", Load("response.refund"))
            .On(HttpMethod.Post, "/v1/transfers", Load("response.transfer"))
            .On(HttpMethod.Post, "/v1/accounts", Load("response.account.created"))
            .On(HttpMethod.Get, "/v1/accounts/acct_1Qx0demo", Load("response.account.pending"))
            .On(HttpMethod.Post, "/v1/account_links", Load("response.account_link"));
        h = new CreateHost { MeetingUrl = "https://meet.demo.invalid/room-1" };
        h.Settings["Payments:Provider"] = "Stripe";
        h.Settings["Payments:Stripe:SecretKey"] = Fx.SecretKey;
        h.Settings["Payments:Stripe:WebhookSecret"] = Fx.WebhookSecret;
        h.Settings["Jobs:Enabled"] = "false";
        h.Configure = s =>
        {
            s.RemoveAll<IPaymentProvider>();
            s.AddSingleton<IPaymentProvider>(sp => Fx.Provider(stripe, h.Clock));
        };
        var original = h.Directory.Override;
        h.Directory.Override = (r, n) => r.RequestUri!.AbsolutePath == $"/internal/v1/practitioners/by-user/{h.OwnerUserId}"
            ? FakeDirectory.Json(new { practitionerId = h.Practitioner, status = "Approved" })
            : original!(r, n);
    }

    public void Dispose() => h.Dispose();

    Task MakeReady(bool ready = true, string provider = "Stripe") => h.Seed(db => db.PaymentAccounts.Add(new PaymentAccount
        { PractitionerId = h.Practitioner, Provider = provider, ProviderAccountId = "acct_1Qx0demo", Ready = ready, CreatedAt = h.Clock.GetUtcNow(), UpdatedAt = h.Clock.GetUtcNow() }));

    async Task<Guid> NewBooking(string start = "2026-11-18T10:00:00Z")
    {
        await h.WorkWeek();
        await MakeReady();
        var (id, paymentId) = await Pay.CreateBooking(h, Client, start);
        Assert.Equal(session, paymentId);
        return id;
    }

    async Task<HttpResponseMessage> Webhook(string json, string? header = null, DateTimeOffset? signedAt = null)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var req = new HttpRequestMessage(HttpMethod.Post, "/webhooks/payments/stripe") { Content = new ByteArrayContent(body) };
        req.Content.Headers.ContentType = new("application/json");
        req.Headers.TryAddWithoutValidation("Stripe-Signature", header ?? Fx.Sign(body, signedAt ?? h.Clock.GetUtcNow()));
        return await h.CreateClient().SendAsync(req);
    }

    async Task<string> Outcome(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("outcome").GetString()!;
    Task<BookingStatus> StatusOf(Guid id) => h.Query(db => db.Bookings.Where(b => b.Id == id).Select(b => b.Status).SingleAsync());

    // ---- booking creation ----

    [Fact]
    public async Task A_booking_is_refused_while_the_practitioner_has_no_account_and_nothing_is_created_or_sent_to_Stripe()
    {
        await h.WorkWeek();
        var r = await CreateHost.Post(h.ClientFor(Client), h.Req("2026-11-18T10:00:00Z"), CreateHost.Key());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("practitioner_payments_not_ready", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Empty(await h.Bookings());
        Assert.Empty(stripe.Requests);
    }

    [Theory]
    [InlineData(false, "Stripe")]   // onboarding not finished
    [InlineData(true, "Acme")]      // a ready account at another provider is no account here
    public async Task An_account_that_is_not_ready_or_belongs_to_another_provider_does_not_count(bool ready, string provider)
    {
        await h.WorkWeek();
        await MakeReady(ready, provider);
        var r = await CreateHost.Post(h.ClientFor(Client), h.Req("2026-11-18T10:00:00Z"), CreateHost.Key());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Empty(stripe.Requests);
    }

    [Fact]
    public async Task With_a_ready_account_the_booking_is_created_with_a_Stripe_checkout_and_a_payment_row_for_the_session()
    {
        var id = await NewBooking();
        var p = await h.Query(db => db.Payments.AsNoTracking().SingleAsync(x => x.BookingId == id));
        Assert.Equal("Stripe", p.Provider);
        Assert.Equal(session, p.ProviderPaymentId);
        Assert.Equal(10000, p.AmountMinor);
        Assert.Equal(BookingStatus.PendingPayment, await StatusOf(id));
    }

    // ---- webhook ----

    [Fact]
    public async Task A_signed_paid_session_confirms_the_booking_once_and_a_redelivery_changes_nothing()
    {
        var id = await NewBooking();
        var paid = Load("event.checkout.session.completed.paid");

        var first = await Webhook(paid);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("Applied", await Outcome(first));
        Assert.Equal(BookingStatus.Confirmed, await StatusOf(id));
        Assert.Equal("Duplicate", await Outcome(await Webhook(paid)));
        Assert.Single(await h.Query(db => db.LedgerEntries.AsNoTracking().Where(l => l.BookingId == id && l.Kind == LedgerKind.ClientCharge).ToListAsync()));
    }

    [Fact]
    public async Task An_altered_body_a_wrong_secret_and_a_stale_timestamp_are_401_and_change_nothing_and_the_alert_names_the_event_id_only()
    {
        var id = await NewBooking();
        var paid = Load("event.checkout.session.completed.paid");
        var altered = paid.Replace("\"amount_total\": 10000", "\"amount_total\": 1");

        Assert.Equal(HttpStatusCode.Unauthorized, (await Webhook(altered, Fx.Sign(Encoding.UTF8.GetBytes(paid), h.Clock.GetUtcNow()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Webhook(paid, Fx.Sign(Encoding.UTF8.GetBytes(paid), h.Clock.GetUtcNow(), "whsec_other"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Webhook(paid, signedAt: h.Clock.GetUtcNow() - TimeSpan.FromMinutes(6))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Webhook(paid, "")).StatusCode);
        Assert.Equal(BookingStatus.PendingPayment, await StatusOf(id));
        Assert.Contains(h.Logs.Entries, e => e.EventId == PaymentWebhookFeature.SignatureFailedEventId && e.Level == LogLevel.Error && e.Line.Contains($"evt_{tag}_Paid"));
        Assert.DoesNotContain(h.Logs.Lines, l => l.Contains(Fx.WebhookSecret) || l.Contains(Fx.SecretKey));
    }

    [Fact]
    public async Task A_paid_event_for_another_amount_or_currency_is_Rejected_with_an_alert_and_the_booking_stays_unpaid()
    {
        var id = await NewBooking();
        var cheap = Load("event.checkout.session.completed.paid").Replace("\"amount_total\": 10000", "\"amount_total\": 9999");
        Assert.Equal("Rejected", await Outcome(await Webhook(cheap)));
        var euro = Load("event.checkout.session.async_payment_succeeded").Replace("\"currency\": \"pln\"", "\"currency\": \"eur\"");
        Assert.Equal("Rejected", await Outcome(await Webhook(euro)));
        Assert.Equal(BookingStatus.PendingPayment, await StatusOf(id));
        Assert.Contains(h.Logs.Lines, l => l.Contains("ALERT payment amount or currency mismatch"));
    }

    [Theory]
    [InlineData("event.checkout.session.completed.unpaid")]
    [InlineData("event.account.updated")]
    [InlineData("event.payment_intent.succeeded")]
    public async Task An_event_Booking_does_not_use_is_acknowledged_and_recorded_as_Ignored(string fixture)
    {
        var id = await NewBooking();
        var r = await Webhook(Load(fixture));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Ignored", await Outcome(r));
        Assert.Equal(BookingStatus.PendingPayment, await StatusOf(id));
        Assert.Equal(WebhookOutcome.Ignored, (await h.Query(db => db.WebhookEvents.AsNoTracking().SingleAsync(e => e.Provider == "Stripe" && e.EventId.StartsWith($"evt_{tag}_")))).Outcome);
    }

    [Fact]
    public async Task An_expired_session_expires_the_booking_and_an_event_for_a_session_nobody_knows_is_retried_later()
    {
        var id = await NewBooking();
        var unknown = Load("event.checkout.session.expired").Replace(session, "cs_test_unknown");
        var later = await Webhook(unknown);
        Assert.Equal(HttpStatusCode.Accepted, later.StatusCode);
        Assert.True(later.Headers.Contains("Retry-After"));

        Assert.Equal("Applied", await Outcome(await Webhook(Load("event.checkout.session.expired"))));
        Assert.Equal(BookingStatus.Expired, await StatusOf(id));
    }

    [Fact]
    public async Task A_payment_after_the_hold_ran_out_is_refunded_in_full_with_a_stable_key()
    {
        var id = await NewBooking();
        h.Clock.Advance(TimeSpan.FromMinutes(11)); // the hold is 10 minutes
        Assert.Equal("Applied", await Outcome(await Webhook(Load("event.checkout.session.completed.paid"))));

        Assert.Equal(BookingStatus.Expired, await StatusOf(id));
        var refund = Assert.Single(stripe.Requests, r => r.PathAndQuery == "/v1/refunds");
        Assert.Equal("10000", refund.Form["amount"]);
        Assert.StartsWith("late:", refund.Headers["Idempotency-Key"]);
        Assert.Equal(PaymentStatus.Refunded, (await h.Query(db => db.Payments.AsNoTracking().SingleAsync(p => p.BookingId == id))).Status);
    }

    [Fact]
    public async Task A_webhook_for_another_provider_name_is_404_and_simulate_is_403_with_a_real_provider()
    {
        await NewBooking();
        var r = await h.CreateClient().PostAsync("/webhooks/payments/mock", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        var sim = await Pay.Simulate(h.ClientFor(Client), Guid.NewGuid(), "succeeded");
        Assert.Equal(HttpStatusCode.Forbidden, sim.StatusCode);
    }

    // ---- refunds: asked of Stripe, written to the ledger when Stripe confirms ----

    [Fact]
    public async Task An_early_client_cancel_asks_Stripe_for_the_refund_and_the_ledger_row_waits_for_the_refund_webhook()
    {
        var id = await NewBooking();
        Assert.Equal("Applied", await Outcome(await Webhook(Load("event.checkout.session.completed.paid"))));

        var cancel = await Life.Cancel(h, id, Client);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(BookingStatus.CancelledByClient, await StatusOf(id));
        var refund = Assert.Single(stripe.Requests, r => r.PathAndQuery == "/v1/refunds");
        Assert.Equal($"refund:{id:N}:ClientCancelledEarly", refund.Headers["Idempotency-Key"]);
        Assert.Equal("pi_3Qx0demo", refund.Form["payment_intent"]);
        Assert.Empty(await h.Query(db => db.LedgerEntries.AsNoTracking().Where(l => l.BookingId == id && l.Kind == LedgerKind.Refund).ToListAsync())); // not before Stripe confirms

        Assert.Equal("Applied", await Outcome(await Webhook(Load("event.refund.created.succeeded"))));
        Assert.Equal("Ignored", await Outcome(await Webhook(Load("event.refund.updated.succeeded")))); // the second event of the same refund adds nothing
        Assert.Single(await h.Query(db => db.LedgerEntries.AsNoTracking().Where(l => l.BookingId == id && l.Kind == LedgerKind.Refund).ToListAsync()));
        Assert.Equal(PaymentStatus.Refunded, (await h.Query(db => db.Payments.AsNoTracking().SingleAsync(p => p.BookingId == id))).Status);
    }

    // ---- payout ----

    [Fact]
    public async Task A_due_completed_booking_is_paid_as_a_transfer_to_the_connected_account_and_once()
    {
        await MakeReady();
        var id = await Life.Seed(h, BookingStatus.Completed, TimeSpan.FromDays(-4), provider: "Stripe");
        await h.Seed(db =>
        {
            var b = db.Bookings.Single(x => x.Id == id);
            (b.FeeMinor, b.PayoutMinor, b.PayoutDueAt) = (1500, 8500, h.Clock.GetUtcNow() - TimeSpan.FromMinutes(1));
            db.Payments.Single(p => p.BookingId == id).ProviderPaymentId = session;
        });
        var job = Job();

        Assert.Equal(1, await job.PayDueAsync(h.Clock.GetUtcNow(), default));
        Assert.Equal(0, await job.PayDueAsync(h.Clock.GetUtcNow(), default));

        var t = Assert.Single(stripe.Requests, r => r.PathAndQuery == "/v1/transfers");
        Assert.Equal($"payout:{id:N}", t.Headers["Idempotency-Key"]);
        Assert.Equal("8500", t.Form["amount"]);
        Assert.Equal("acct_1Qx0demo", t.Form["destination"]);
        Assert.Equal("ch_3Qx0demo", t.Form["source_transaction"]);
        var row = await h.Query(db => db.Payouts.AsNoTracking().SingleAsync(p => p.BookingId == id));
        Assert.Equal((PayoutStatus.Sent, "tr_1Qx0demo", 1), (row.Status, row.ProviderRef, row.AttemptCount));
    }

    [Fact]
    public async Task A_payout_to_a_practitioner_without_an_account_makes_no_Stripe_call_and_is_counted_as_a_failed_attempt()
    {
        var id = await Life.Seed(h, BookingStatus.Completed, TimeSpan.FromDays(-4), provider: "Stripe");
        await h.Seed(db =>
        {
            var b = db.Bookings.Single(x => x.Id == id);
            (b.FeeMinor, b.PayoutMinor, b.PayoutDueAt) = (1500, 8500, h.Clock.GetUtcNow() - TimeSpan.FromMinutes(1));
        });
        await Assert.ThrowsAsync<JobFailedException>(() => Job().PayDueAsync(h.Clock.GetUtcNow(), default));
        Assert.DoesNotContain(stripe.Requests, r => r.Method == HttpMethod.Post);
        var row = await h.Query(db => db.Payouts.AsNoTracking().SingleAsync(p => p.BookingId == id));
        Assert.Equal((PayoutStatus.Failed, 1), (row.Status, row.AttemptCount));
    }

    ScopedPayoutJob Job() => new(h.Services.GetRequiredService<IServiceScopeFactory>(), h.Services.GetRequiredService<IPaymentProvider>(), h.Clock,
        h.Services.GetRequiredService<IConfiguration>(), h.Services.GetRequiredService<JobMetrics>(), h.Services.GetRequiredService<ILogger<Gabinety.Booking.Jobs.Payout.PayoutJob>>(), h.Practitioner);

    // ---- onboarding ----

    HttpClient Practitioner() => h.ClientFor(h.OwnerUserId);

    [Fact]
    public async Task Onboarding_creates_the_account_once_stores_only_its_id_and_hands_out_a_fresh_link_each_time()
    {
        var first = await Practitioner().PostAsync("/api/v1/me/payments/account/onboarding", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var body = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pending", body.GetProperty("state").GetString());
        Assert.StartsWith("https://connect.stripe.com/", body.GetProperty("onboardingUrl").GetString());

        await Practitioner().PostAsync("/api/v1/me/payments/account/onboarding", null);
        Assert.Single(stripe.Requests, r => r.PathAndQuery == "/v1/accounts" && r.Method == HttpMethod.Post);
        Assert.Equal(2, stripe.Requests.Count(r => r.PathAndQuery == "/v1/account_links"));
        var row = await h.Query(db => db.PaymentAccounts.AsNoTracking().SingleAsync(a => a.PractitionerId == h.Practitioner));
        Assert.Equal((h.Practitioner, "Stripe", "acct_1Qx0demo", false), (row.PractitionerId, row.Provider, row.ProviderAccountId, row.Ready));
        Assert.DoesNotContain(h.Logs.Lines, l => l.Contains("connect.stripe.com"));
    }

    [Fact]
    public async Task The_status_is_none_then_pending_then_ready_and_a_ready_account_lets_the_booking_through()
    {
        var c = Practitioner();
        Assert.Equal("none", (await (await c.GetAsync("/api/v1/me/payments/account")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());
        await c.PostAsync("/api/v1/me/payments/account/onboarding", null);
        Assert.Equal("pending", (await (await c.GetAsync("/api/v1/me/payments/account")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());

        stripe.On(HttpMethod.Get, "/v1/accounts/acct_1Qx0demo", Load("response.account.ready")); // the practitioner finished at Stripe
        var ready = await (await c.GetAsync("/api/v1/me/payments/account")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ready", ready.GetProperty("state").GetString());
        Assert.True((await h.Query(db => db.PaymentAccounts.AsNoTracking().SingleAsync(a => a.PractitionerId == h.Practitioner))).Ready);

        var again = await (await c.PostAsync("/api/v1/me/payments/account/onboarding", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ready", again.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, again.GetProperty("onboardingUrl").ValueKind);

        await h.WorkWeek();
        Assert.Equal(HttpStatusCode.Created, (await CreateHost.Post(h.ClientFor(Client), h.Req("2026-11-18T10:00:00Z"), CreateHost.Key())).StatusCode);
    }

    [Fact]
    public async Task Only_a_practitioner_can_onboard_and_an_unauthenticated_call_is_401()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await h.ClientFor("demo-somebody-else").PostAsync("/api/v1/me/payments/account/onboarding", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.ClientFor(null).GetAsync("/api/v1/me/payments/account")).StatusCode);
        Assert.Empty(stripe.Requests);
    }

    [Fact]
    public async Task A_Stripe_failure_during_onboarding_is_503_and_the_provider_answer_is_not_passed_on()
    {
        stripe.On(HttpMethod.Post, "/v1/accounts", Load("response.error.resource_missing"), HttpStatusCode.BadRequest);
        var r = await Practitioner().PostAsync("/api/v1/me/payments/account/onboarding", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.DoesNotContain("user text", await r.Content.ReadAsStringAsync());
        Assert.Empty(await h.Query(db => db.PaymentAccounts.Where(a => a.PractitionerId == h.Practitioner).ToListAsync()));
        Assert.Contains(h.Logs.Entries, e => e.Level == LogLevel.Error && e.Line.Contains("resource_missing"));
        Assert.DoesNotContain(h.Logs.Lines, l => l.Contains("user text"));
    }

    [Fact]
    public async Task With_the_Mock_provider_there_is_no_onboarding_and_a_booking_needs_no_account()
    {
        using var mock = new CreateHost { OwnerUserId = h.OwnerUserId };
        await mock.WorkWeek();
        Assert.Equal(HttpStatusCode.NotFound, (await mock.ClientFor(mock.OwnerUserId).GetAsync("/api/v1/me/payments/account")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CreateHost.Post(mock.ClientFor(Client), mock.Req("2026-11-18T10:00:00Z"), CreateHost.Key())).StatusCode);
    }

    // ---- payout date of a resolved booking ----

    [Fact]
    public async Task A_resolved_booking_falls_due_at_the_moment_of_the_resolution()
    {
        using var mock = new CreateHost { MeetingUrl = "https://meet.demo.invalid/room-1" };
        var (id, _) = await Adm.Seed(mock);
        var r = await Adm.Resolve(mock, id, Adm.Body("release"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var b = await mock.Query(db => db.Bookings.AsNoTracking().SingleAsync(x => x.Id == id));
        Assert.Equal(mock.Clock.GetUtcNow(), b.ResolvedAt);
        Assert.Equal(b.ResolvedAt, b.PayoutDueAt);
    }
}
