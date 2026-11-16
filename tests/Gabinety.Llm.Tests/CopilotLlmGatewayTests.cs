using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gabinety.Llm.Tests;

/// <summary>Degraded paths only: no runtime, no sign-in, no network beyond a refused loopback port.</summary>
public class CopilotLlmGatewayTests
{
    private const string MissingCli = "/nonexistent/gabinety-test-copilot-cli";
    private const string FakeToken = "ghp_test_DO_NOT_LEAK_12345";

    [Fact]
    public async Task Start_with_missing_cli_path_is_Unavailable()
    {
        var gateway = NewGateway(new CopilotOptions { CliPath = MissingCli });

        await gateway.StartAsync(CancellationToken.None);

        Assert.Equal(LlmGatewayState.Unavailable, gateway.Status.State);
        Assert.StartsWith("Copilot runtime unavailable", gateway.Status.Detail);
    }

    [Fact]
    public async Task Start_with_refused_url_is_Unavailable()
    {
        // Port 1 on loopback refuses connections: fails fast without leaving the machine.
        var gateway = NewGateway(new CopilotOptions { CliUrl = "127.0.0.1:1", StartupTimeout = TimeSpan.FromSeconds(10) });

        await gateway.StartAsync(CancellationToken.None);

        Assert.Equal(LlmGatewayState.Unavailable, gateway.Status.State);
    }

    [Fact]
    public async Task Unavailable_gateway_refuses_models_and_conversations()
    {
        var gateway = NewGateway(new CopilotOptions { CliPath = MissingCli });
        await gateway.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<LlmUnavailableException>(() => gateway.ListModelsAsync(CancellationToken.None));
        await Assert.ThrowsAsync<LlmUnavailableException>(() => gateway.StartConversationAsync(
            new LlmConversationOptions("m", "system", TimeSpan.FromSeconds(5)), CancellationToken.None));
    }

    [Fact]
    public async Task Stop_before_any_start_is_harmless()
    {
        var gateway = NewGateway(new CopilotOptions());

        await gateway.StopAsync();

        Assert.Equal(LlmGatewayState.Unavailable, gateway.Status.State);
    }

    [Fact]
    public async Task The_github_token_is_absent_from_the_detail_and_from_ToString()
    {
        var options = new CopilotOptions { CliPath = MissingCli, GitHubToken = FakeToken };
        var gateway = NewGateway(options);

        await gateway.StartAsync(CancellationToken.None);

        Assert.DoesNotContain(FakeToken, gateway.Status.Detail);
        Assert.DoesNotContain(FakeToken, options.ToString());
    }

    [Fact]
    public async Task Host_with_missing_cli_starts_and_stops_without_failing()
    {
        using var host = HostWith(provider: null, cliPath: MissingCli).Build();

        await host.StartAsync();

        Assert.Equal(LlmGatewayState.Unavailable, host.Services.GetRequiredService<ILlmGateway>().Status.State);
        await host.StopAsync();
    }

    [Fact]
    public void Copilot_is_the_default_provider_and_fake_is_selectable()
    {
        using var copilot = HostWith(provider: null, cliPath: MissingCli).Build();
        using var fake = HostWith(provider: "Fake", cliPath: null).Build();

        Assert.IsType<CopilotLlmGateway>(copilot.Services.GetRequiredService<ILlmGateway>());
        Assert.IsType<FakeLlmGateway>(fake.Services.GetRequiredService<ILlmGateway>());
    }

    private static CopilotLlmGateway NewGateway(CopilotOptions options) =>
        new(Options.Create(options), NullLogger<CopilotLlmGateway>.Instance);

    private static HostApplicationBuilder HostWith(string? provider, string? cliPath)
    {
        var builder = Host.CreateApplicationBuilder();
        if (provider is not null) builder.Configuration["Llm:Provider"] = provider;
        if (cliPath is not null) builder.Configuration["Copilot:CliPath"] = cliPath;
        builder.AddLlmGateway();
        return builder;
    }
}
