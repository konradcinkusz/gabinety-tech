using System.Text;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.Booking.Payments.Stripe;

namespace Gabinety.Booking.Tests.StripePayments;

/// <summary>The documented Stripe-Signature scheme and the event mapping (T-09.05, CONTRACTS 4.3).</summary>
public class StripeWebhookTests
{
    static readonly byte[] Paid = Fx.Bytes("event.checkout.session.completed.paid");
    static StripePaymentProvider P(DateTimeOffset? now = null) => Fx.Provider(new Handler(), new FixedClock(now ?? Fx.Now));

    // ---- signature ----

    [Fact] public void A_valid_signature_is_accepted() => Assert.NotNull(P().VerifyWebhook(Paid, Fx.Sign(Paid, Fx.Now)));

    [Fact]
    public void An_altered_body_is_rejected_even_by_one_byte()
    {
        var header = Fx.Sign(Paid, Fx.Now);
        var altered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Paid).Replace("\"amount_total\": 10000", "\"amount_total\": 100"));
        Assert.NotEqual(Paid.Length, altered.Length);
        Assert.Null(P().VerifyWebhook(altered, header));
        var flipped = (byte[])Paid.Clone();
        flipped[^3] ^= 1;
        Assert.Null(P().VerifyWebhook(flipped, header));
    }

    [Fact]
    public void A_pretty_printed_or_reserialised_body_does_not_verify_because_the_signature_is_over_the_raw_bytes()
    {
        var header = Fx.Sign(Paid, Fx.Now);
        var reserialised = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonDocument.Parse(Paid).RootElement));
        Assert.Null(P().VerifyWebhook(reserialised, header));
    }

    [Fact] public void A_signature_made_with_another_secret_is_rejected() => Assert.Null(P().VerifyWebhook(Paid, Fx.Sign(Paid, Fx.Now, "whsec_someone_else")));

    [Fact]
    public void A_changed_timestamp_breaks_the_signature_because_the_timestamp_is_signed()
    {
        var parts = Fx.Sign(Paid, Fx.Now).Split(',');
        var forged = $"t={Fx.Now.ToUnixTimeSeconds() + 1},{parts[1]}";
        Assert.Null(P().VerifyWebhook(Paid, forged));
    }

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    [InlineData(3600, false)]
    public void A_signature_older_than_five_minutes_is_a_replay_and_is_rejected(int ageSeconds, bool ok)
    {
        var signedAt = Fx.Now - TimeSpan.FromSeconds(ageSeconds);
        Assert.Equal(ok, P().VerifyWebhook(Paid, Fx.Sign(Paid, signedAt)) is not null);
    }

    [Theory]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void A_timestamp_from_the_future_is_bounded_by_the_same_tolerance(int aheadSeconds, bool ok) =>
        Assert.Equal(ok, P().VerifyWebhook(Paid, Fx.Sign(Paid, Fx.Now + TimeSpan.FromSeconds(aheadSeconds))) is not null);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("t=,v1=")]
    [InlineData("t=abc,v1=00")]
    [InlineData("v1=0000000000000000000000000000000000000000000000000000000000000000")]            // no timestamp
    [InlineData("t=1790000000")]                                                                     // no signature
    [InlineData("t=1790000000,v1=zz00000000000000000000000000000000000000000000000000000000000000")] // not hex
    [InlineData("t=1790000000,v1=00")]                                                               // wrong length
    public void A_missing_or_malformed_header_is_rejected_without_an_exception(string? header) => Assert.Null(P().VerifyWebhook(Paid, header));

    [Fact]
    public void Only_the_v0_scheme_is_not_enough()
    {
        var h = Fx.Sign(Paid, Fx.Now, scheme: "v0");
        Assert.Null(P().VerifyWebhook(Paid, h));
    }

    [Fact]
    public void While_a_secret_is_rolled_any_one_of_several_v1_signatures_passes()
    {
        var good = Fx.Sign(Paid, Fx.Now);
        var old = Fx.Sign(Paid, Fx.Now, "whsec_rolled_out").Split(',')[1];
        Assert.NotNull(P().VerifyWebhook(Paid, good + "," + old));
        Assert.NotNull(P().VerifyWebhook(Paid, old.Replace("v1=", "v1=") + "," + good));
        Assert.Null(P().VerifyWebhook(Paid, Fx.Sign(Paid, Fx.Now, "x") + "," + old));
    }

    [Fact]
    public void A_provider_built_with_an_empty_secret_accepts_nothing()
    {
        var p = new StripePaymentProvider(new StripeApi(new HttpClient(new Handler()), Fx.SecretKey), "", new FixedClock(Fx.Now));
        Assert.Null(p.VerifyWebhook(Paid, Fx.Sign(Paid, Fx.Now, "")));
        Assert.False(p.WebhookSecretConfigured);
    }

    [Fact] public void The_adapter_names_the_header_it_reads() => Assert.Equal("Stripe-Signature", P().SignatureHeader);

    // ---- mapping ----

    static WebhookEvent Map(string fixture)
    {
        var body = Fx.Bytes(fixture);
        return P().VerifyWebhook(body, Fx.Sign(body, Fx.Now))!;
    }

    [Theory]
    [InlineData("event.checkout.session.completed.paid", "evt_1Qx0Paid")]
    [InlineData("event.checkout.session.async_payment_succeeded", "evt_1Qx0AsyncOk")]
    public void A_paid_session_is_PaymentSucceeded_with_the_session_id_the_amount_and_an_upper_case_currency(string fixture, string eventId)
    {
        var e = Map(fixture);
        Assert.Equal(new WebhookEvent(eventId, WebhookEventKind.PaymentSucceeded, Fx.SessionId, 10000, "PLN"), e);
    }

    [Theory]
    [InlineData("event.checkout.session.async_payment_failed", "evt_1Qx0AsyncNo")]
    [InlineData("event.checkout.session.expired", "evt_1Qx0Expired")]
    public void A_failed_or_expired_session_is_PaymentFailed(string fixture, string eventId) =>
        Assert.Equal(new WebhookEvent(eventId, WebhookEventKind.PaymentFailed, Fx.SessionId, 10000, "PLN"), Map(fixture));

    [Theory]
    [InlineData("event.refund.created.succeeded", "evt_1Qx0RefundCreated")]
    [InlineData("event.refund.updated.succeeded", "evt_1Qx0RefundUpdated")]
    public void A_succeeded_refund_Booking_asked_for_is_RefundSucceeded_for_the_payment_it_carries(string fixture, string eventId) =>
        Assert.Equal(new WebhookEvent(eventId, WebhookEventKind.RefundSucceeded, Fx.SessionId, 10000, "PLN"), Map(fixture));

    [Theory]
    [InlineData("event.checkout.session.completed.unpaid")]     // BLIK and other delayed methods: completed does not mean paid
    [InlineData("event.refund.created.pending")]                // not succeeded yet
    [InlineData("event.refund.created.no-metadata")]            // a refund made in the Dashboard is not Booking's to record
    [InlineData("event.refund.failed")]
    [InlineData("event.account.updated")]
    [InlineData("event.payment_intent.succeeded")]              // the session event is the one Booking uses; this would count a payment twice
    public void Every_other_event_is_Unknown_and_keeps_its_event_id_so_it_is_recorded_as_Ignored(string fixture)
    {
        var e = Map(fixture);
        Assert.Equal(WebhookEventKind.Unknown, e.Kind);
        Assert.StartsWith("evt_", e.EventId);
        Assert.Null(e.ProviderPaymentId);
    }

    [Fact]
    public void A_verified_body_that_is_not_an_event_is_Unknown_with_no_id_never_an_exception()
    {
        foreach (var raw in new[] { "[]", "\"x\"", "{}", "{\"id\":\"evt_1\",\"type\":\"checkout.session.completed\"}", "{\"id\":\"evt_1\",\"type\":\"checkout.session.completed\",\"data\":{\"object\":5}}", "not json" })
        {
            var body = Encoding.UTF8.GetBytes(raw);
            var e = P().VerifyWebhook(body, Fx.Sign(body, Fx.Now))!;
            Assert.Equal(WebhookEventKind.Unknown, e.Kind);
        }
    }

    [Fact]
    public void The_amount_of_a_refund_event_is_the_refund_amount_not_the_charge()
    {
        var body = Encoding.UTF8.GetBytes(Fx.Load("event.refund.created.succeeded").Replace("\"amount\": 10000", "\"amount\": 2500"));
        var e = P().VerifyWebhook(body, Fx.Sign(body, Fx.Now))!;
        Assert.Equal(2500, e.AmountMinor);
    }
}
