using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Gabinety.Booking.Notifications.Smtp;

/// <summary>
/// What <c>Notifications:Provider=Smtp</c> resolved to at startup: the real sender, or the log sender with a stated reason (P8). The reason names keys, never values.
/// </summary>
public sealed record EmailProviderState(bool Configured, string Detail);

/// <summary>The optional dependency "email": Healthy when the SMTP sender is in use, Degraded (never Unhealthy) when it fell back to the log sender.</summary>
public sealed class EmailHealthCheck(EmailProviderState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        Task.FromResult(state.Configured ? HealthCheckResult.Healthy("smtp") : HealthCheckResult.Degraded(state.Detail));
}
