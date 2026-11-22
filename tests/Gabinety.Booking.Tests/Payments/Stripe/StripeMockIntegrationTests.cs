using System.Net;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.Booking.Payments.Stripe;

namespace Gabinety.Booking.Tests.StripePayments;

/// <summary>
/// The Stripe adapter against the official <c>stripe/stripe-mock</c> container (pinned), which serves Stripe's OpenAPI spec and answers 400 to a request whose fields or types the spec does not know.
/// It is the proof, without an account, that the requests are well formed for the real API: create-checkout, refund, transfer, connected account, account link and account read.
/// It does not prove behaviour: stripe-mock returns canned objects, sends no webhooks and moves no money. CI's integration job runs it; Docker is not available everywhere.
/// </summary>
[Trait("tier", "integration")]
public sealed class StripeMockIntegrationTests : IAsyncLifetime
{
    /// <summary>Pinned: the newest tag at the time of writing (Docker Hub, 2026-10-01). Raise it deliberately; a spec newer than the tag's may make a field of the adapter unknown to the mock.</summary>
    const string Image = "stripe/stripe-mock:v0.206.0";
    const int Port = 12111;
    IContainer container = null!;
    RecordingHandler recorder = null!;
    StripePaymentProvider provider = null!;

    sealed class RecordingHandler : DelegatingHandler
    {
        public List<(HttpMethod Method, string Path, HttpStatusCode Status)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var r = await base.SendAsync(request, ct);
            lock (Calls) Calls.Add((request.Method, request.RequestUri!.AbsolutePath, r.StatusCode));
            return r;
        }
    }

    public async Task InitializeAsync()
    {
        container = new ContainerBuilder(Image)
            .WithPortBinding(Port, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Listening for HTTP"))
            .Build();
        await container.StartAsync();
        recorder = new RecordingHandler { InnerHandler = new HttpClientHandler() };
        var http = new HttpClient(recorder) { BaseAddress = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(Port)}") };
        provider = new StripePaymentProvider(new StripeApi(http, "sk_test_123"), Fx.WebhookSecret, new FixedClock(Fx.Now));
    }

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact]
    public async Task Create_checkout_is_accepted_by_the_API_shape_and_returns_a_session_id_and_a_url()
    {
        var booking = Guid.NewGuid();
        var r = await provider.CreateCheckoutAsync(new CheckoutRequest(booking, 12500, "PLN", "demo-offering ć", "https://demo.invalid/bookings/" + booking), default);
        Assert.StartsWith("cs_", r.ProviderPaymentId);
        Assert.StartsWith("http", r.CheckoutUrl);
        Assert.Contains((HttpMethod.Post, "/v1/checkout/sessions", HttpStatusCode.OK), recorder.Calls);
    }

    [Fact]
    public async Task A_refund_and_a_transfer_are_accepted_by_the_API_shape()
    {
        var refund = await provider.PostRefundAsync("pi_123", 2500, "cs_123", $"refund:{Guid.NewGuid():N}:test", default);
        Assert.StartsWith("re_", refund);
        var transfer = await provider.PostTransferAsync("acct_123", "ch_123", 8500, "booking_123", "cs_123", $"payout:{Guid.NewGuid():N}", default);
        Assert.StartsWith("tr_", transfer);
        Assert.Contains((HttpMethod.Post, "/v1/refunds", HttpStatusCode.OK), recorder.Calls);
        Assert.Contains((HttpMethod.Post, "/v1/transfers", HttpStatusCode.OK), recorder.Calls);
    }

    [Fact]
    public async Task An_express_account_an_onboarding_link_and_the_account_read_are_accepted_by_the_API_shape()
    {
        var id = await provider.CreateAccountAsync(Guid.NewGuid(), default);
        Assert.StartsWith("acct_", id);
        Assert.StartsWith("http", await provider.CreateOnboardingLinkAsync(id, "https://demo.invalid/return", "https://demo.invalid/refresh", default));
        var status = await provider.GetAccountStatusAsync(id, default);
        Assert.Equal(id, status.AccountId);
        Assert.All(recorder.Calls, c => Assert.Equal(HttpStatusCode.OK, c.Status));
    }

    [Fact]
    public async Task The_check_is_not_vacuous_the_mock_refuses_a_field_the_spec_does_not_know()
    {
        var api = new StripeApi(new HttpClient(new HttpClientHandler()) { BaseAddress = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(Port)}") }, "sk_test_123");
        var e = await Assert.ThrowsAsync<PaymentProviderException>(() =>
            api.PostAsync("/v1/refunds", new StripeForm().Add("payment_intent", "pi_123").Add("refund_the_lot", "yes"), "k", default));
        Assert.Equal(400, e.Status);
    }
}
