using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Gabinety.Llm;

/// <summary>
/// Reports the gateway state as health (COPILOT-SDK R5). Never Unhealthy: a missing sign-in or runtime is Degraded,
/// so <c>/health</c> stays HTTP 200 and the rest of the app keeps working.
/// </summary>
public sealed class LlmGatewayHealthCheck(ILlmGateway gateway) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = gateway.Status;
        var result = status.State switch
        {
            LlmGatewayState.Ready or LlmGatewayState.Fake => HealthCheckResult.Healthy(status.Detail),
            _ => HealthCheckResult.Degraded(status.Detail),
        };

        return Task.FromResult(result);
    }
}
