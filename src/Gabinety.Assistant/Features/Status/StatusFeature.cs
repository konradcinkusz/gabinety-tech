using Gabinety.Assistant.Policy;
using Gabinety.Assistant.Usage;
using Gabinety.Contracts.Assistant;
using Gabinety.Llm;
using Gabinety.ServiceDefaults;

namespace Gabinety.Assistant.Features.Status;

/// <summary>Maps the gateway state, the traffic policy and the daily budget to the public status. Never throws and never creates a conversation.</summary>
public sealed class AssistantStatusService(ILlmGateway gateway, ITrafficPolicy policy, BudgetGuard budget)
{
    static readonly string[] All = ["brief", "review-triage"];

    public async Task<AssistantStatus> GetAsync(CancellationToken ct)
    {
        var state = gateway.Status.State;
        if (state == LlmGatewayState.Unavailable) return new(AssistantState.Unavailable, []);
        if (state == LlmGatewayState.NotAuthenticated) return new(AssistantState.NotAuthenticated, []);

        try
        {
            if (await budget.CheckAsync(ct) == BudgetDecision.OverBudget) return new(AssistantState.Disabled, []);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return new(AssistantState.Disabled, []); // spend cannot be checked: fail closed
        }

        if (state == LlmGatewayState.Fake) return new(AssistantState.Fake, All);
        return await policy.SelectModelAsync(state, ct) is null ? new(AssistantState.Disabled, []) : new(AssistantState.Ready, All);
    }
}

public sealed class StatusFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) => builder.Services.AddScoped<AssistantStatusService>();

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/assistant/status", async (AssistantStatusService status, CancellationToken ct) =>
            Results.Ok(await status.GetAsync(ct))).AllowAnonymous();
}
