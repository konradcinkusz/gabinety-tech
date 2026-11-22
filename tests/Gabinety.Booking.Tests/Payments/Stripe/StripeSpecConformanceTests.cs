using System.Text.Json;
using System.Text.RegularExpressions;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.Booking.Payments.Stripe;

namespace Gabinety.Booking.Tests.StripePayments;

/// <summary>
/// The field names the adapter sends and reads are Stripe's, not ours: every request field must exist in the OpenAPI spec (stripe/openapi latest/openapi.spec3.json, extracted by
/// scripts/stripe-openapi-extract.py into Fixtures/openapi-fields.json), and every recorded response and event fixture must use only properties the spec defines.
/// </summary>
public partial class StripeSpecConformanceTests
{
    static readonly JsonElement Spec = JsonDocument.Parse(Fx.Load("openapi-fields")).RootElement;

    /// <summary><c>line_items[0][price_data][unit_amount]</c> becomes <c>line_items[].price_data.unit_amount</c>; a free-form map key (<c>metadata[x]</c>) becomes <c>metadata.*</c>.</summary>
    internal static string Normalise(string key)
    {
        var s = IndexRegex().Replace(key, "\u0001");
        s = NameRegex().Replace(s, ".$1");
        return s.Replace("\u0001", "[]");
    }

    [GeneratedRegex(@"\[\d+\]")] private static partial Regex IndexRegex();
    [GeneratedRegex(@"\[([^\]]+)\]")] private static partial Regex NameRegex();

    static bool Known(string endpoint, string normalised)
    {
        var fields = Spec.GetProperty("requests").GetProperty(endpoint).EnumerateArray().Select(e => e.GetString()!).ToHashSet();
        if (fields.Contains(normalised)) return true;
        var dot = normalised.LastIndexOf('.');
        return dot > 0 && fields.Contains(normalised[..dot] + ".*"); // a free-form map
    }

    [Fact] public void The_extracted_spec_is_the_one_the_adapter_pins() => Assert.Equal(StripeApi.ApiVersion, Spec.GetProperty("specVersion").GetString());

    [Fact]
    public async Task Every_request_field_the_adapter_sends_exists_in_the_OpenAPI_spec()
    {
        var h = new Handler()
            .On(HttpMethod.Post, "/v1/checkout/sessions", Fx.Load("response.checkout.session.create"))
            .On(HttpMethod.Get, $"/v1/checkout/sessions/{Fx.SessionId}", Fx.Load("response.checkout.session.retrieve.paid.expanded"))
            .On(HttpMethod.Post, "/v1/refunds", Fx.Load("response.refund")).On(HttpMethod.Post, "/v1/transfers", Fx.Load("response.transfer"))
            .On(HttpMethod.Post, "/v1/accounts", Fx.Load("response.account.created")).On(HttpMethod.Post, "/v1/account_links", Fx.Load("response.account_link"));
        var p = Fx.Provider(h);
        var booking = Guid.NewGuid();
        await p.CreateCheckoutAsync(new CheckoutRequest(booking, 5000, "PLN", "demo", "https://demo.invalid/b"), default);
        await p.RefundAsync(new RefundRequest(Fx.SessionId, 100, "k1"), default);
        await p.PayoutAsync(new PayoutRequest(Guid.NewGuid(), 100, "PLN", "k2", "acct_1", Fx.SessionId, booking), default);
        await p.CreateAccountAsync(Guid.NewGuid(), default);
        await p.CreateOnboardingLinkAsync("acct_1", "https://x.invalid/r", "https://x.invalid/f", default);

        var posts = h.Requests.Where(r => r.Method == HttpMethod.Post).ToList();
        Assert.Equal(5, posts.Count);
        var unknown = posts.SelectMany(r => r.Form.Keys.Select(k => (r.PathAndQuery, Key: k, Path: Normalise(k)))).Where(x => !Known(x.PathAndQuery, x.Path)).ToList();
        Assert.True(unknown.Count == 0, "not in the OpenAPI spec: " + string.Join(", ", unknown.Select(u => $"{u.PathAndQuery} {u.Key}")));
    }

    [Theory]
    [InlineData("line_items[0][price_data][unit_amount]", "line_items[].price_data.unit_amount")]
    [InlineData("allowed_payment_method_types[1]", "allowed_payment_method_types[]")]
    [InlineData("metadata[booking_id]", "metadata.booking_id")]
    [InlineData("payment_intent_data[metadata][booking_id]", "payment_intent_data.metadata.booking_id")]
    [InlineData("controller[stripe_dashboard][type]", "controller.stripe_dashboard.type")]
    [InlineData("amount", "amount")]
    public void The_key_normaliser_matches_the_extractor(string key, string expected) => Assert.Equal(expected, Normalise(key));

    [Fact]
    public void The_check_is_not_vacuous_an_invented_field_is_unknown_and_a_real_one_is_known()
    {
        Assert.False(Known("/v1/refunds", "refund_the_lot"));
        Assert.True(Known("/v1/refunds", "reverse_transfer"));
        Assert.True(Known("/v1/refunds", "metadata.provider_payment_id"));
        Assert.False(Known("/v1/transfers", "application_fee_amount"));
    }

    [Theory]
    [InlineData("event.checkout.session.completed.paid", "checkout.session")]
    [InlineData("event.checkout.session.expired", "checkout.session")]
    [InlineData("event.refund.created.succeeded", "refund")]
    [InlineData("event.refund.failed", "refund")]
    [InlineData("response.checkout.session.create", "checkout.session")]
    [InlineData("response.checkout.session.retrieve.paid", "checkout.session")]
    [InlineData("response.refund", "refund")]
    [InlineData("response.transfer", "transfer")]
    [InlineData("response.account.ready", "account")]
    [InlineData("response.account_link", "account_link")]
    public void A_recorded_fixture_uses_only_properties_the_spec_defines(string fixture, string objectType)
    {
        var root = JsonDocument.Parse(Fx.Load(fixture)).RootElement;
        var (obj, schema) = fixture.StartsWith("event.") ? (root.GetProperty("data").GetProperty("object"), objectType) : (root, objectType);
        var allowed = Spec.GetProperty("objects").GetProperty(schema).EnumerateArray().Select(e => e.GetString()!).ToHashSet();
        var bad = obj.EnumerateObject().Select(p => p.Name).Where(n => !allowed.Contains(n)).ToList();
        Assert.True(bad.Count == 0, $"{fixture}: not in the spec: {string.Join(", ", bad)}");
        if (fixture.StartsWith("event."))
        {
            var envelope = Spec.GetProperty("objects").GetProperty("event").EnumerateArray().Select(e => e.GetString()!).ToHashSet();
            Assert.All(root.EnumerateObject().Select(p => p.Name), n => Assert.Contains(n, envelope));
            Assert.Contains(root.GetProperty("type").GetString(), Spec.GetProperty("events").EnumerateArray().Select(e => e.GetString()));
        }
    }

    [Fact]
    public void The_event_names_the_mapper_listens_to_are_in_the_spec()
    {
        var events = Spec.GetProperty("events").EnumerateArray().Select(e => e.GetString()).ToHashSet();
        foreach (var name in new[] { "checkout.session.completed", "checkout.session.async_payment_succeeded", "checkout.session.async_payment_failed", "checkout.session.expired", "refund.created", "refund.updated" })
            Assert.Contains(name, events);
    }
}
