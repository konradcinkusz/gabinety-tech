using Gabinety.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Gabinety.Llm.Tests;

public class OptionsTests
{
    private const string Key = "sk-test-DO-NOT-LEAK-12345";

    private static CustomModelOptions Valid(Action<CustomModelOptions>? change = null)
    {
        var model = new CustomModelOptions
        {
            Id = "mine", DisplayName = "Mine", ProviderType = "openai", BaseUrl = "https://api.example.test/v1",
            Model = "wire-model", ApiKey = Key,
        };
        change?.Invoke(model);
        return model;
    }

    private static ValidateOptionsResult Validate(params CustomModelOptions[] models) =>
        new LlmOptionsValidator().Validate(null, new LlmOptions { CustomModels = [.. models] });

    private static string[] Failures(ValidateOptionsResult r) => [.. r.Failures ?? []];

    [Fact]
    public void AValidEntryPasses() => Assert.True(Validate(Valid()).Succeeded);

    [Fact]
    public void ADefaultOptionsObjectPasses() => Assert.True(new LlmOptionsValidator().Validate(null, new LlmOptions()).Succeeded);

    [Fact]
    public void AnIdWithAtIsRejectedAndTheMessageNamesTheSetting()
    {
        var result = Validate(Valid(c => c.Id = "x@y"));

        Assert.Contains("Llm:CustomModels[0].Id 'x@y' must not contain '@'", Failures(result));
    }

    [Fact]
    public void AnIdWithSpacesIsRejected() =>
        Assert.Contains(Failures(Validate(Valid(c => c.Id = "a b"))), f => f.StartsWith("Llm:CustomModels[0].Id") && f.Contains("spaces"));

    [Fact]
    public void DuplicateIdsAreRejectedIgnoringCase() =>
        Assert.Contains(Failures(Validate(Valid(c => c.Id = "m"), Valid(c => c.Id = "M"))),
            f => f.StartsWith("Llm:CustomModels[1].Id") && f.Contains("already used"));

    [Fact]
    public void ABadEntryNeverLeaksTheKeyOrTheUrl()
    {
        var result = Validate(Valid(c =>
        {
            c.Id = "x@y";
            c.ProviderType = "nope";
            c.BaseUrl = "ftp://user:" + Key + "@host/";
            c.Model = "";
            c.UsdPerMillionInput = 1;
        }));

        Assert.False(result.Succeeded);
        var all = string.Join("\n", Failures(result));
        Assert.Contains("Llm:CustomModels[0].BaseUrl", all);
        Assert.Contains("Llm:CustomModels[0].ProviderType", all);
        Assert.Contains("Llm:CustomModels[0].Model", all);
        Assert.DoesNotContain(Key, all);
        Assert.DoesNotContain("ftp://", all);
        Assert.DoesNotContain("user:", all);
    }

    [Fact]
    public void APriceMustComeAsACompletePair() =>
        Assert.Contains(Failures(Validate(Valid(c => c.UsdPerMillionOutput = 3))), f => f.Contains("UsdPerMillionInput") && f.Contains("together"));

    [Fact]
    public void ANegativePriceIsRejected() =>
        Assert.Contains(Failures(Validate(Valid(c => { c.UsdPerMillionInput = -1; c.UsdPerMillionOutput = 1; }))), f => f.Contains("UsdPerMillionInput"));

    [Fact]
    public void WireApiIsRequiredForOpenAiButNotForAnthropic()
    {
        Assert.Contains(Failures(Validate(Valid(c => c.WireApi = ""))), f => f.Contains("WireApi"));
        Assert.True(Validate(Valid(c => { c.ProviderType = "anthropic"; c.WireApi = ""; })).Succeeded);
    }

    [Fact]
    public void AnUnknownProviderAndANegativeLatencyAreRejectedByKeyName()
    {
        var result = new LlmOptionsValidator().Validate(null, new LlmOptions { Provider = "Bogus", FakeLatencyMs = -1 });

        Assert.Contains(Failures(result), f => f.StartsWith("Llm:Provider") && !f.Contains("Bogus"));
        Assert.Contains(Failures(result), f => f.StartsWith("Llm:FakeLatencyMs"));
    }

    [Fact]
    public void CustomModelToStringOmitsTheKey()
    {
        var text = Valid().ToString();

        Assert.DoesNotContain(Key, text);
        Assert.Contains("mine", text);
    }

    [Fact]
    public void LlmOptionsToStringOmitsTheKeys()
    {
        Assert.DoesNotContain(Key, new LlmOptions { CustomModels = [Valid()] }.ToString());
    }

    [Fact]
    public void StartupStopsOnABadCustomModelWithAMessageThatHasThePathAndNotTheKey()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Llm:Provider"] = "Fake",
            ["Llm:CustomModels:0:Id"] = "x@y",
            ["Llm:CustomModels:0:ProviderType"] = "openai",
            ["Llm:CustomModels:0:BaseUrl"] = "https://api.example.test",
            ["Llm:CustomModels:0:Model"] = "m",
            ["Llm:CustomModels:0:ApiKey"] = Key,
        });
        builder.AddLlmGateway();
        using var host = builder.Build();

        var ex = Assert.Throws<OptionsValidationException>(() => host.Services.GetRequiredService<IOptions<LlmOptions>>().Value);

        Assert.Contains("Llm:CustomModels[0].Id", ex.Message);
        Assert.DoesNotContain(Key, ex.Message);
    }

    private static IHost Build(string? provider)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        if (provider is not null)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Llm:Provider"] = provider });
        }

        builder.AddLlmGateway();
        return builder.Build();
    }

    [Fact]
    public async Task FakeProviderRegistersTheFakeAndTheLlmHealthCheck()
    {
        using var host = Build("Fake");

        var gateway = host.Services.GetRequiredService<ILlmGateway>();
        var health = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.IsType<FakeLlmGateway>(gateway);
        Assert.Equal(HealthStatus.Healthy, health.Entries["llm"].Status);
    }

    [Fact]
    public async Task CopilotIsTheDefaultAndItsPlaceholderIsDegradedNotUnhealthy()
    {
        using var host = Build(null);

        var health = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Degraded, health.Entries["llm"].Status);
    }

    [Fact]
    public void AnUnknownProviderStopsTheHostSetupWithAMessageNamingTheKey()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build("Bogus"));

        Assert.Contains("Llm:Provider", ex.Message);
        Assert.DoesNotContain("Bogus", ex.Message);
    }
}
