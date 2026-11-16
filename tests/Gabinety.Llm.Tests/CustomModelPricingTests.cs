namespace Gabinety.Llm.Tests;

public class CustomModelPricingTests
{
    private const string Secret = "sk-test-DO-NOT-LEAK-12345";

    [Fact]
    public void AUsdPairIsTheTokenPricesPerMillionTokensInUsd()
    {
        var prices = CustomModelPricing.TokenPrices(Model(c => (c.UsdPerMillionInput, c.UsdPerMillionOutput) = (3.0, 15.0)));

        Assert.Equal(new LlmTokenPrices(1_000_000, 3.0, 15.0, null), prices);
    }

    [Fact]
    public void AModelWithNoPriceHasNoTokenPrices() => Assert.Null(CustomModelPricing.TokenPrices(Model()));

    [Fact]
    public void AnIncompletePairIsNoPrice()
    {
        Assert.Null(CustomModelPricing.TokenPrices(Model(c => c.UsdPerMillionInput = 3.0)));
        Assert.Null(CustomModelPricing.TokenPrices(Model(c => c.UsdPerMillionOutput = 15.0)));
    }

    [Fact]
    public void ACustomModelIsListedWithCustomTrueItsDisplayNameAndItsPrices()
    {
        var info = CustomModelPricing.ToModelInfo(Model(c =>
        {
            c.DisplayName = "Tiny";
            (c.UsdPerMillionInput, c.UsdPerMillionOutput) = (1.0, 2.0);
        }));

        Assert.True(info.Custom);
        Assert.True(info.Enabled);
        Assert.Equal("tiny", info.Id);
        Assert.Equal("Tiny", info.DisplayName);
        Assert.Equal(new LlmTokenPrices(1_000_000, 1.0, 2.0, null), info.Pricing);
    }

    [Fact]
    public void ACustomModelWithoutAPriceIsListedWithoutPricingAndFallsBackToItsIdAsName()
    {
        var info = CustomModelPricing.ToModelInfo(Model());

        Assert.True(info.Custom);
        Assert.Null(info.Pricing);
        Assert.Equal("tiny", info.DisplayName);
    }

    [Fact]
    public void TheApiKeyIsNotInTheListingOrTheToString()
    {
        var model = Model(c => c.ApiKey = Secret);

        Assert.DoesNotContain(Secret, CustomModelPricing.ToModelInfo(model).ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, model.ToString(), StringComparison.Ordinal);
    }

    internal static CustomModelOptions Model(Action<CustomModelOptions>? change = null)
    {
        var model = new CustomModelOptions
        {
            Id = "tiny",
            ProviderType = "anthropic",
            BaseUrl = "https://api.anthropic.com",
            Model = "claude-sonnet-5-5",
        };
        change?.Invoke(model);
        return model;
    }
}
