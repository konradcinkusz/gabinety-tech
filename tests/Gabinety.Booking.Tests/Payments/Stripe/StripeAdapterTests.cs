using System.Net;
using System.Text.Json;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.Booking.Payments.Stripe;

namespace Gabinety.Booking.Tests.StripePayments;

/// <summary>What the adapter sends to Stripe (bodies, headers, idempotency keys) and how it reads the answers, against the recorded response shapes.</summary>
public class StripeAdapterTests
{
    static readonly Guid Booking = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    static readonly Guid Practitioner = Guid.Parse("11111111-2222-3333-4444-555555555555");

    static Handler Stripe() => new Handler()
        .On(HttpMethod.Post, "/v1/checkout/sessions", Fx.Load("response.checkout.session.create"))
        .On(HttpMethod.Get, $"/v1/checkout/sessions/{Fx.SessionId}", Fx.Load("response.checkout.session.retrieve.paid"))
        .On(HttpMethod.Post, "/v1/refunds", Fx.Load("response.refund"))
        .On(HttpMethod.Post, "/v1/transfers", Fx.Load("response.transfer"))
        .On(HttpMethod.Post, "/v1/accounts", Fx.Load("response.account.created"))
        .On(HttpMethod.Get, "/v1/accounts/acct_1Qx0demo", Fx.Load("response.account.ready"))
        .On(HttpMethod.Post, "/v1/account_links", Fx.Load("response.account_link"));

    static CheckoutRequest Checkout(long amount = 10000, string currency = "PLN", string title = "demo-offering") =>
        new(Booking, amount, currency, title, "https://demo.invalid/bookings/" + Booking);

    // ---- checkout ----

    [Fact]
    public async Task Checkout_posts_a_payment_mode_session_in_zloty_with_the_booking_the_idempotency_key_and_the_documented_headers()
    {
        var h = Stripe();
        var r = await Fx.Provider(h).CreateCheckoutAsync(Checkout(), default);

        Assert.Equal(Fx.SessionId, r.ProviderPaymentId);
        Assert.StartsWith("https://checkout.stripe.com/", r.CheckoutUrl);
        var q = Assert.Single(h.Requests);
        Assert.Equal(HttpMethod.Post, q.Method);
        Assert.Equal($"checkout:{Booking:N}", q.Headers["Idempotency-Key"]); // stable per booking: a retry is the same session
        Assert.Equal($"Bearer {Fx.SecretKey}", q.Headers["Authorization"]);
        Assert.Equal(StripeApi.ApiVersion, q.Headers["Stripe-Version"]);
        Assert.Equal("payment", q.Form["mode"]);
        Assert.Equal("10000", q.Form["line_items[0][price_data][unit_amount]"]);
        Assert.Equal("pln", q.Form["line_items[0][price_data][currency]"]);
        Assert.Equal("1", q.Form["line_items[0][quantity]"]);
        Assert.Equal(Booking.ToString(), q.Form["client_reference_id"]);
        Assert.Equal(Booking.ToString(), q.Form["metadata[booking_id]"]);
        Assert.Equal(Booking.ToString(), q.Form["payment_intent_data[metadata][booking_id]"]);
        Assert.Equal($"booking_{Booking:N}", q.Form["payment_intent_data[transfer_group]"]);
        Assert.Equal("card", q.Form["allowed_payment_method_types[0]"]);
        Assert.Equal("blik", q.Form["allowed_payment_method_types[1]"]);
        Assert.Equal((Fx.Now + TimeSpan.FromMinutes(35)).ToUnixTimeSeconds().ToString(), q.Form["expires_at"]); // Stripe's minimum life is 30 minutes
        Assert.Equal($"https://demo.invalid/bookings/{Booking}", q.Form["success_url"]);
    }

    [Fact]
    public async Task Checkout_does_not_ask_for_a_destination_or_a_fee_the_platform_keeps_the_charge_and_transfers_later()
    {
        var h = Stripe();
        await Fx.Provider(h).CreateCheckoutAsync(Checkout(), default);
        Assert.DoesNotContain(h.Requests[0].Form.Keys, k => k.Contains("transfer_data") || k.Contains("application_fee") || k.Contains("on_behalf_of"));
    }

    [Theory]
    [InlineData("EUR")]
    [InlineData("USD")]
    public async Task Only_zloty_is_charged(string currency) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Fx.Provider(Stripe()).CreateCheckoutAsync(Checkout(currency: currency), default));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_non_positive_amount_is_never_sent(long amount)
    {
        var h = Stripe();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Fx.Provider(h).CreateCheckoutAsync(Checkout(amount), default));
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task A_long_offering_title_is_cut_to_what_the_product_name_allows_and_form_characters_survive_encoding()
    {
        var h = Stripe();
        await Fx.Provider(h).CreateCheckoutAsync(Checkout(title: "A&B=C+ć " + new string('x', 400)), default);
        var name = h.Requests[0].Form["line_items[0][price_data][product_data][name]"];
        Assert.Equal(200, name.Length);
        Assert.StartsWith("A&B=C+ć ", name);
    }

    // ---- refund ----

    [Fact]
    public async Task A_refund_finds_the_payment_intent_of_the_session_and_carries_the_callers_key_the_amount_and_the_payment_for_the_webhook()
    {
        var h = Stripe();
        var r = await Fx.Provider(h).RefundAsync(new RefundRequest(Fx.SessionId, 10000, $"refund:{Booking:N}:ClientCancelledEarly"), default);

        Assert.Equal("re_3Qx0demo", r.ProviderRefundId);
        Assert.Equal(HttpMethod.Get, h.Requests[0].Method);
        var q = h.Requests[1];
        Assert.Equal($"refund:{Booking:N}:ClientCancelledEarly", q.Headers["Idempotency-Key"]);
        Assert.Equal("pi_3Qx0demo", q.Form["payment_intent"]);
        Assert.Equal("10000", q.Form["amount"]);
        Assert.Equal(Fx.SessionId, q.Form["metadata[provider_payment_id]"]);
        // nothing was transferred yet (the payout waits for the dispute window), so there is no transfer to reverse and no application fee to give back
        Assert.DoesNotContain(q.Form.Keys, k => k is "reverse_transfer" or "refund_application_fee");
    }

    [Fact]
    public async Task A_refund_of_an_unpaid_session_fails_instead_of_refunding_something_else()
    {
        var h = Stripe().On(HttpMethod.Get, $"/v1/checkout/sessions/{Fx.SessionId}", Fx.Load("response.checkout.session.retrieve.unpaid"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Fx.Provider(h).RefundAsync(new RefundRequest(Fx.SessionId, 1, "k"), default));
        Assert.DoesNotContain(h.Requests, r => r.Method == HttpMethod.Post);
    }

    // ---- payout (a transfer) ----

    [Fact]
    public async Task A_payout_is_a_transfer_of_price_minus_fee_to_the_connected_account_from_the_charge_with_the_callers_key()
    {
        var h = Stripe().On(HttpMethod.Get, $"/v1/checkout/sessions/{Fx.SessionId}", Fx.Load("response.checkout.session.retrieve.paid.expanded"));
        var r = await Fx.Provider(h).PayoutAsync(new PayoutRequest(Practitioner, 8500, "PLN", $"payout:{Booking:N}", "acct_1Qx0demo", Fx.SessionId, Booking), default);

        Assert.Equal("tr_1Qx0demo", r.ProviderPayoutId);
        Assert.Contains("expand%5B%5D=payment_intent", h.Requests[0].PathAndQuery);
        var q = h.Requests[1];
        Assert.Equal("/v1/transfers", q.PathAndQuery);
        Assert.Equal($"payout:{Booking:N}", q.Headers["Idempotency-Key"]);
        Assert.Equal("8500", q.Form["amount"]);
        Assert.Equal("pln", q.Form["currency"]);
        Assert.Equal("acct_1Qx0demo", q.Form["destination"]);
        Assert.Equal("ch_3Qx0demo", q.Form["source_transaction"]); // funds come from this charge, so they need not be available yet
        Assert.Equal($"booking_{Booking:N}", q.Form["transfer_group"]);
    }

    [Theory]
    [InlineData(null, "cs_x")]
    [InlineData("", "cs_x")]
    [InlineData("acct_1", null)]
    [InlineData("acct_1", "")]
    public async Task A_payout_without_an_account_or_a_payment_is_refused_before_any_call(string? account, string? payment)
    {
        var h = Stripe();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Fx.Provider(h).PayoutAsync(new PayoutRequest(Practitioner, 100, "PLN", "payout:x", account, payment, Booking), default));
        Assert.Empty(h.Requests);
    }

    // ---- connected accounts ----

    [Fact]
    public async Task An_express_account_is_created_in_Poland_with_transfers_requested_and_one_key_per_practitioner()
    {
        var h = Stripe();
        Assert.Equal("acct_1Qx0demo", await Fx.Provider(h).CreateAccountAsync(Practitioner, default));
        var q = Assert.Single(h.Requests);
        Assert.Equal($"account:{Practitioner:N}", q.Headers["Idempotency-Key"]);
        Assert.Equal("express", q.Form["controller[stripe_dashboard][type]"]);
        Assert.Equal("application", q.Form["controller[fees][payer]"]);
        Assert.Equal("application", q.Form["controller[losses][payments]"]);
        Assert.Equal("stripe", q.Form["controller[requirement_collection]"]);
        Assert.Equal("PL", q.Form["country"]);
        Assert.Equal("true", q.Form["capabilities[transfers][requested]"]);
        Assert.Equal(Practitioner.ToString(), q.Form["metadata[practitioner_id]"]);
    }

    [Fact]
    public async Task An_onboarding_link_is_requested_for_the_account_with_both_urls_and_a_fresh_key_each_time()
    {
        var h = Stripe();
        var p = Fx.Provider(h);
        Assert.StartsWith("https://connect.stripe.com/", await p.CreateOnboardingLinkAsync("acct_1Qx0demo", "https://demo.invalid/return", "https://demo.invalid/refresh", default));
        await p.CreateOnboardingLinkAsync("acct_1Qx0demo", "https://demo.invalid/return", "https://demo.invalid/refresh", default);
        Assert.Equal("account_onboarding", h.Requests[0].Form["type"]);
        Assert.Equal("acct_1Qx0demo", h.Requests[0].Form["account"]);
        Assert.Equal("https://demo.invalid/return", h.Requests[0].Form["return_url"]);
        Assert.Equal("https://demo.invalid/refresh", h.Requests[0].Form["refresh_url"]);
        Assert.NotEqual(h.Requests[0].Headers["Idempotency-Key"], h.Requests[1].Headers["Idempotency-Key"]);
    }

    [Theory]
    [InlineData("response.account.ready", ConnectedAccountState.Ready)]
    [InlineData("response.account.pending", ConnectedAccountState.Pending)]
    [InlineData("response.account.created", ConnectedAccountState.Pending)]
    public async Task An_account_is_ready_only_when_details_are_in_payouts_are_on_and_transfers_are_active(string fixture, ConnectedAccountState expected)
    {
        var h = Stripe().On(HttpMethod.Get, "/v1/accounts/acct_1Qx0demo", Fx.Load(fixture));
        Assert.Equal(expected, (await Fx.Provider(h).GetAccountStatusAsync("acct_1Qx0demo", default)).State);
    }

    [Fact]
    public void Readiness_needs_every_one_of_the_three_conditions()
    {
        static bool Ready(string json) => StripePaymentProvider.IsReady(JsonDocument.Parse(json).RootElement);
        Assert.True(Ready("{\"details_submitted\":true,\"payouts_enabled\":true,\"capabilities\":{\"transfers\":\"active\"}}"));
        Assert.False(Ready("{\"details_submitted\":false,\"payouts_enabled\":true,\"capabilities\":{\"transfers\":\"active\"}}"));
        Assert.False(Ready("{\"details_submitted\":true,\"payouts_enabled\":false,\"capabilities\":{\"transfers\":\"active\"}}"));
        Assert.False(Ready("{\"details_submitted\":true,\"payouts_enabled\":true,\"capabilities\":{\"transfers\":\"inactive\"}}"));
        Assert.False(Ready("{\"details_submitted\":true,\"payouts_enabled\":true}"));
    }

    // ---- every write has a key; errors carry no message ----

    [Fact]
    public async Task Every_write_call_the_adapter_makes_carries_an_idempotency_key()
    {
        var h = Stripe().On(HttpMethod.Get, $"/v1/checkout/sessions/{Fx.SessionId}", Fx.Load("response.checkout.session.retrieve.paid.expanded"));
        var p = Fx.Provider(h);
        await p.CreateCheckoutAsync(Checkout(), default);
        await p.RefundAsync(new RefundRequest(Fx.SessionId, 1, "refund:k"), default);
        await p.PayoutAsync(new PayoutRequest(Practitioner, 1, "PLN", "payout:k", "acct_1Qx0demo", Fx.SessionId, Booking), default);
        await p.CreateAccountAsync(Practitioner, default);
        await p.CreateOnboardingLinkAsync("acct_1Qx0demo", "https://x.invalid/r", "https://x.invalid/f", default);
        var writes = h.Requests.Where(r => r.Method == HttpMethod.Post).ToList();
        Assert.Equal(5, writes.Count);
        Assert.All(writes, w => Assert.False(string.IsNullOrWhiteSpace(w.Headers["Idempotency-Key"]), w.PathAndQuery));
        Assert.All(h.Requests.Where(r => r.Method == HttpMethod.Get), r => Assert.False(r.Headers.ContainsKey("Idempotency-Key")));
    }

    [Fact]
    public async Task A_Stripe_error_becomes_an_exception_with_the_status_and_the_code_and_never_the_message()
    {
        var h = Stripe().On(HttpMethod.Post, "/v1/refunds", Fx.Load("response.error.resource_missing"), HttpStatusCode.NotFound)
                        .On(HttpMethod.Get, $"/v1/checkout/sessions/{Fx.SessionId}", Fx.Load("response.checkout.session.retrieve.paid"));
        var e = await Assert.ThrowsAsync<PaymentProviderException>(() => Fx.Provider(h).RefundAsync(new RefundRequest(Fx.SessionId, 1, "k"), default));
        Assert.Equal(404, e.Status);
        Assert.Equal("resource_missing", e.Code);
        Assert.DoesNotContain("user text", e.Message);
        Assert.DoesNotContain(Fx.SecretKey, e.ToString());
    }

    [Fact]
    public async Task An_unreadable_success_body_is_an_error_not_a_null_result()
    {
        var h = new Handler().On(HttpMethod.Post, "/v1/checkout/sessions", "<html>gateway</html>");
        await Assert.ThrowsAsync<PaymentProviderException>(() => Fx.Provider(h).CreateCheckoutAsync(Checkout(), default));
    }

    [Fact]
    public async Task A_response_without_the_id_is_an_error()
    {
        var h = new Handler().On(HttpMethod.Post, "/v1/checkout/sessions", "{\"object\":\"checkout.session\"}");
        await Assert.ThrowsAsync<PaymentProviderException>(() => Fx.Provider(h).CreateCheckoutAsync(Checkout(), default));
    }

    [Fact]
    public async Task A_cancelled_call_is_cancelled_the_budget_of_the_caller_reaches_the_request()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Fx.Provider(Stripe()).CreateCheckoutAsync(Checkout(), cts.Token));
    }

    [Fact]
    public void The_adapter_declares_what_the_host_needs_to_know_about_it()
    {
        var p = Fx.Provider(new Handler());
        Assert.Equal("Stripe", p.Name);
        Assert.True(p.RequiresPractitionerAccount);
        Assert.True(p.WebhookSecretConfigured);
        Assert.IsAssignableFrom<IConnectedAccountProvider>(p);
    }
}
