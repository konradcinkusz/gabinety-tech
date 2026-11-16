using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Gabinety.Llm;

public static class LlmServiceCollectionExtensions
{
    /// <summary>
    /// Registers the gateway chosen by <c>Llm:Provider</c> (<c>Copilot</c> or <c>Fake</c>), the <c>llm</c> health check and
    /// the <c>Llm</c> options, validated when the host starts.
    /// </summary>
    public static IHostApplicationBuilder AddLlmGateway(this IHostApplicationBuilder builder)
    {
        var provider = builder.Configuration["Llm:Provider"] ?? "Copilot";

        builder.Services.AddHealthChecks().AddCheck<LlmGatewayHealthCheck>("llm");
        builder.Services.AddOptions<LlmOptions>().BindConfiguration("Llm").ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<LlmOptions>, LlmOptionsValidator>();

        switch (provider.ToLowerInvariant())
        {
            case "copilot":
                // Placeholder: the real Copilot gateway replaces this registration in T-02.02.
                builder.Services.AddSingleton<ILlmGateway, PlaceholderCopilotGateway>();
                break;

            case "fake":
                builder.Services.AddSingleton<ILlmGateway>(sp =>
                {
                    var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
                    return new FakeLlmGateway(options.Seed, TimeSpan.FromMilliseconds(options.FakeLatencyMs), options.CustomModels);
                });
                break;

            default:
                throw new InvalidOperationException("Llm:Provider must be 'Copilot' or 'Fake'.");
        }

        return builder;
    }

    /// <summary>Stands in for the Copilot gateway until T-02.02: reports Unavailable, lists nothing, starts nothing.</summary>
    private sealed class PlaceholderCopilotGateway : ILlmGateway
    {
        public LlmGatewayStatus Status { get; } = new(LlmGatewayState.Unavailable, "Copilot gateway not implemented yet");

        public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<LlmModelInfo>>([]);

        public Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct) =>
            throw new LlmUnavailableException(Status.Detail);
    }
}
