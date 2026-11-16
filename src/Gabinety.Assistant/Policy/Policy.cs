using Gabinety.Llm;
using Gabinety.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Gabinety.Assistant.Policy;

/// <summary>Configuration section <c>Assistant</c> (COPILOT-SDK 7, 8). Read at startup; a change needs a restart.</summary>
public sealed class AssistantOptions
{
    /// <summary>Only models with Custom = true serve public traffic (the fake gateway's models excepted). Default true.</summary>
    public bool RequireApiKeyModelsForTraffic { get; set; } = true;

    /// <summary>Preferred model id; chosen first when allowed.</summary>
    public string? Model { get; set; }

    /// <summary>The UTC day's spend (sum of usd_estimate) at which new work is refused.</summary>
    public decimal DailyBudgetUsd { get; set; } = 5m;

    /// <summary>SECRET. Salt of the user hash. Unset: a per-process random salt (hashes then differ across restarts).</summary>
    public string? UserHashSalt { get; set; }

    public override string ToString() => $"Assistant (RequireApiKeyModelsForTraffic {RequireApiKeyModelsForTraffic}, DailyBudgetUsd {DailyBudgetUsd})";
}

/// <summary>Decides, for a request that a public user caused, whether a model may be used. Llm offers mechanism; this is policy.</summary>
public interface ITrafficPolicy
{
    bool IsAllowed(LlmGatewayState gateway, LlmModelInfo model);

    /// <summary>The preferred model when allowed, else the first allowed enabled model; null when none or when listing fails or takes over 3 s. Never creates a conversation.</summary>
    Task<LlmModelInfo?> SelectModelAsync(LlmGatewayState gateway, CancellationToken ct);
}

public sealed class TrafficPolicy(ILlmGateway gateway, IOptions<AssistantOptions> options) : ITrafficPolicy
{
    public static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(3);
    readonly bool _requireCustom = options.Value.RequireApiKeyModelsForTraffic;
    readonly string? _preferred = options.Value.Model;

    public bool IsAllowed(LlmGatewayState state, LlmModelInfo model) =>
        state == LlmGatewayState.Fake || model.Custom || !_requireCustom;

    public async Task<LlmModelInfo?> SelectModelAsync(LlmGatewayState state, CancellationToken ct)
    {
        IReadOnlyList<LlmModelInfo> models;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ListTimeout);
            // WaitAsync too: a gateway that ignores its token must not hold the caller.
            models = await gateway.ListModelsAsync(cts.Token).WaitAsync(ListTimeout, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null; // failure or timeout: no allowed model; the message is not kept (it may echo configuration)
        }

        var allowed = models.Where(m => m.Enabled && IsAllowed(state, m)).ToList();
        return allowed.FirstOrDefault(m => _preferred is not null && m.Id == _preferred) ?? allowed.FirstOrDefault();
    }
}

public sealed class PolicyFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<AssistantOptions>().BindConfiguration("Assistant");
        builder.Services.AddSingleton<ITrafficPolicy, TrafficPolicy>();
    }

    public void Map(IEndpointRouteBuilder endpoints) { }
}
