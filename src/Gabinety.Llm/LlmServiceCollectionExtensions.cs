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
                builder.Services.AddOptions<CopilotOptions>().BindConfiguration("Copilot");
                builder.Services.AddSingleton<CopilotLlmGateway>();
                builder.Services.AddSingleton<ILlmGateway>(sp => sp.GetRequiredService<CopilotLlmGateway>());
                builder.Services.AddHostedService<CopilotClientHostedService>();
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
}
