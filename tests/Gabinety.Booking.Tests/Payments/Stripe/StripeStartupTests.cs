using Gabinety.Booking.Payments.Abstractions;
using Gabinety.Booking.Payments.Mock;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gabinety.Booking.Tests.StripePayments;

/// <summary>Payments:Provider=Stripe needs both secrets, in every profile; the refusal names the setting to fix and never a value (T-09.05).</summary>
public class StripeStartupTests
{
    static IConfiguration Config(string? key, string? hook) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Payments:Stripe:SecretKey"] = key, ["Payments:Stripe:WebhookSecret"] = hook }).Build();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Both_secrets_present_and_well_formed_is_accepted_in_every_profile(bool nonDeployed) =>
        Assert.Null(PaymentsFeature.Problem("Stripe", nonDeployed, Config(Fx.SecretKey, Fx.WebhookSecret)));

    [Theory]
    [InlineData("rk_" + "live_restricted0123456789")]
    [InlineData("sk_" + "live_0123456789")]
    public void A_restricted_key_is_accepted_too(string key) => Assert.Null(PaymentsFeature.Problem("stripe", false, Config(key, Fx.WebhookSecret)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("pk_" + "test_publishable_key_is_not_a_secret")]
    [InlineData("whsec_this_is_the_wrong_setting")]
    public void A_missing_or_wrong_kind_of_secret_key_stops_the_host_and_names_the_setting(string? key)
    {
        var msg = PaymentsFeature.Problem("Stripe", false, Config(key, Fx.WebhookSecret))!;
        Assert.Contains("Payments__Stripe__SecretKey", msg);
        Assert.DoesNotContain(Fx.WebhookSecret, msg);
        if (!string.IsNullOrWhiteSpace(key)) Assert.DoesNotContain(key, msg);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sk_test_not_a_webhook_secret")]
    public void A_missing_or_wrong_kind_of_webhook_secret_stops_the_host_because_every_webhook_would_be_rejected(string? hook)
    {
        var msg = PaymentsFeature.Problem("Stripe", false, Config(Fx.SecretKey, hook))!;
        Assert.Contains("Payments__Stripe__WebhookSecret", msg);
        Assert.DoesNotContain(Fx.SecretKey, msg);
    }

    [Fact] public void Stripe_without_any_configuration_is_refused() => Assert.NotNull(PaymentsFeature.Problem("Stripe", false));

    [Fact]
    public void The_registered_provider_is_the_Stripe_adapter_with_a_working_http_client_and_Mock_is_still_refused_outside_the_fake_profile()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            { ["GABINETY_FAKE"] = "0", ["Payments:Provider"] = "Stripe", ["Payments:Stripe:SecretKey"] = Fx.SecretKey, ["Payments:Stripe:WebhookSecret"] = Fx.WebhookSecret });
        builder.Services.AddSingleton(TimeProvider.System);
        new PaymentsFeature().Add(builder);
        using var sp = builder.Services.BuildServiceProvider();
        var p = sp.GetRequiredService<IPaymentProvider>();
        Assert.Equal("Stripe", p.Name);
        Assert.IsNotType<MockPaymentProvider>(p);
        Assert.NotNull(PaymentsFeature.Problem("Mock", false));
    }
}
