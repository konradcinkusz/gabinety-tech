using System.Net;
using System.Net.Http.Json;
using Gabinety.Assistant.Policy;
using Gabinety.Contracts.Assistant;
using Gabinety.Llm;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Gabinety.Assistant.Tests.Policy;

public class PolicyTests
{
    static TrafficPolicy Policy(ILlmGateway g, bool require = true, string? model = null) =>
        new(g, Options.Create(new AssistantOptions { RequireApiKeyModelsForTraffic = require, Model = model }));

    static WebApplicationFactory<Program> Host(ILlmGateway g, bool? require = null, string? budget = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Llm:Provider", "Fake");
            if (require is { } r) b.UseSetting("Assistant:RequireApiKeyModelsForTraffic", r.ToString());
            if (budget is not null) b.UseSetting("Assistant:DailyBudgetUsd", budget);
            b.ConfigureServices(s => { s.RemoveAll<ILlmGateway>(); s.AddSingleton(g); });
        });

    static async Task<AssistantStatus> Status(WebApplicationFactory<Program> f)
    {
        var r = await f.CreateClient().GetAsync("/api/v1/assistant/status");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<AssistantStatus>())!;
    }

    static readonly LlmModelInfo[] CopilotOnly = [CountingGateway.Copilot("demo-copilot-a"), CountingGateway.Copilot("demo-copilot-b")];

    [Fact]
    public async Task Defaults_with_a_copilot_only_list_allow_no_model_status_is_Disabled_and_no_session_was_created()
    {
        var g = new CountingGateway(LlmGatewayState.Ready, CopilotOnly);
        var p = Policy(g);
        Assert.All(CopilotOnly, m => Assert.False(p.IsAllowed(LlmGatewayState.Ready, m)));
        Assert.Null(await p.SelectModelAsync(LlmGatewayState.Ready, CancellationToken.None));

        using var f = Host(g);
        var s = await Status(f);
        Assert.Equal(AssistantState.Disabled, s.State);
        Assert.Empty(s.Features);
        Assert.Equal(0, g.StartConversationCalls);
    }

    [Fact]
    public async Task Fake_gateway_allows_its_models_and_status_is_Fake()
    {
        var fake = new FakeLlmGateway();
        var p = Policy(fake);
        var models = await fake.ListModelsAsync(default);
        Assert.All(models.Where(m => !m.Custom), m => Assert.True(p.IsAllowed(LlmGatewayState.Fake, m)));
        Assert.Equal("fake-a", (await p.SelectModelAsync(LlmGatewayState.Fake, default))!.Id);

        using var f = Host(fake);
        var s = await Status(f);
        Assert.Equal(AssistantState.Fake, s.State);
        Assert.Equal(["brief", "review-triage"], s.Features);
    }

    [Fact]
    public async Task A_custom_model_on_a_Ready_gateway_is_allowed_and_status_is_Ready()
    {
        var g = new CountingGateway(LlmGatewayState.Ready, [CountingGateway.Copilot("demo-copilot-a"), CountingGateway.Custom("demo-custom")]);
        Assert.True(Policy(g).IsAllowed(LlmGatewayState.Ready, CountingGateway.Custom("demo-custom")));
        Assert.Equal("demo-custom", (await Policy(g).SelectModelAsync(LlmGatewayState.Ready, default))!.Id);

        using var f = Host(g);
        var s = await Status(f);
        Assert.Equal(AssistantState.Ready, s.State);
        Assert.Equal(["brief", "review-triage"], s.Features);
        Assert.Equal(0, g.StartConversationCalls);
    }

    [Fact]
    public async Task The_preferred_model_is_chosen_first_when_allowed_and_a_disabled_model_is_never_chosen()
    {
        var g = new CountingGateway(LlmGatewayState.Ready,
            [CountingGateway.Custom("demo-c1"), CountingGateway.Custom("demo-c2"), CountingGateway.Custom("demo-off", enabled: false)]);
        Assert.Equal("demo-c2", (await Policy(g, model: "demo-c2").SelectModelAsync(LlmGatewayState.Ready, default))!.Id);
        Assert.Equal("demo-c1", (await Policy(g, model: "demo-off").SelectModelAsync(LlmGatewayState.Ready, default))!.Id);
        Assert.Equal("demo-c1", (await Policy(g, model: "demo-copilot-a").SelectModelAsync(LlmGatewayState.Ready, default))!.Id);
    }

    [Fact]
    public async Task Relaxing_RequireApiKeyModelsForTraffic_allows_copilot_models()
    {
        var g = new CountingGateway(LlmGatewayState.Ready, CopilotOnly);
        Assert.True(Policy(g, require: false).IsAllowed(LlmGatewayState.Ready, CopilotOnly[0]));
        using var f = Host(g, require: false);
        Assert.Equal(AssistantState.Ready, (await Status(f)).State);
    }

    [Theory]
    [InlineData(LlmGatewayState.NotAuthenticated, AssistantState.NotAuthenticated)]
    [InlineData(LlmGatewayState.Unavailable, AssistantState.Unavailable)]
    public async Task NotAuthenticated_and_Unavailable_are_reported_as_they_are_without_listing_models(LlmGatewayState gateway, AssistantState expected)
    {
        var g = new CountingGateway(gateway, [CountingGateway.Custom("demo-custom")]);
        using var f = Host(g);
        var s = await Status(f);
        Assert.Equal(expected, s.State);
        Assert.Empty(s.Features);
        Assert.Equal(0, g.ListModelsCalls);
    }

    [Fact]
    public async Task A_listing_that_throws_gives_Disabled_without_an_exception()
    {
        var g = new CountingGateway(LlmGatewayState.Ready, []) { OnList = _ => throw new LlmUnavailableException("boom") };
        Assert.Null(await Policy(g).SelectModelAsync(LlmGatewayState.Ready, default));
        using var f = Host(g);
        Assert.Equal(AssistantState.Disabled, (await Status(f)).State);
    }

    [Fact]
    public async Task A_listing_that_exceeds_three_seconds_gives_Disabled_and_does_not_hang()
    {
        var g = new CountingGateway(LlmGatewayState.Ready, [CountingGateway.Custom("demo-custom")])
        { OnList = _ => Task.Delay(TimeSpan.FromSeconds(30)) }; // ignores the token on purpose
        using var f = Host(g);
        var started = DateTime.UtcNow;
        Assert.Equal(AssistantState.Disabled, (await Status(f)).State);
        Assert.InRange(DateTime.UtcNow - started, TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(15));
        Assert.Equal(0, g.StartConversationCalls);
    }

    [Fact]
    public async Task A_reached_budget_makes_the_status_Disabled_for_a_Ready_and_a_Fake_gateway()
    {
        foreach (var g in new ILlmGateway[] { new CountingGateway(LlmGatewayState.Ready, [CountingGateway.Custom("demo-custom")]), new FakeLlmGateway() })
        {
            using var f = Host(g, budget: "0.01");
            using (var scope = f.Services.CreateScope())
            {
                var rec = scope.ServiceProvider.GetRequiredService<Gabinety.Assistant.Usage.UsageRecorder>();
                await rec.RecordAsync(Gabinety.Assistant.Usage.AssistantFeature.Brief, "demo-custom", Gabinety.Assistant.Usage.ProviderKind.Custom,
                    1000, 1000, null, 0.01m, 10, Gabinety.Assistant.Usage.UsageOutcome.Ok, "demo-user-1");
            }
            Assert.Equal(AssistantState.Disabled, (await Status(f)).State);
        }
    }
}
