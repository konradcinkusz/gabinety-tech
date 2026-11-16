using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Gabinety.Assistant.Features.Health;

/// <summary>Last probe result, shared across the transient check instances.</summary>
public sealed class JwksCache
{
    public (HealthCheckResult Result, DateTimeOffset At)? Last;
}

/// <summary>Degraded when Identity:Authority is empty or its JWKS cannot be fetched or has no keys. Failures are cached at most 30 s.</summary>
public sealed class IdentityHealthCheck(HttpClient http, IConfiguration config, JwksCache cache, TimeProvider time) : IHealthCheck
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    static readonly TimeSpan FailureTtl = TimeSpan.FromSeconds(30), SuccessTtl = TimeSpan.FromMinutes(1);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var authority = config["Identity:Authority"]?.TrimEnd('/');
        if (string.IsNullOrEmpty(authority)) return HealthCheckResult.Degraded("authority not configured");

        var now = time.GetUtcNow();
        if (cache.Last is { } last && now - last.At < (last.Result.Status == HealthStatus.Healthy ? SuccessTtl : FailureTtl))
            return last.Result;

        var result = await Probe(authority, ct);
        cache.Last = (result, now);
        return result;
    }

    async Task<HealthCheckResult> Probe(string authority, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        try
        {
            using var discovery = JsonDocument.Parse(await http.GetStringAsync($"{authority}/.well-known/openid-configuration", cts.Token));
            var jwksUri = discovery.RootElement.TryGetProperty("jwks_uri", out var u) ? u.GetString() : null;
            if (string.IsNullOrEmpty(jwksUri)) return HealthCheckResult.Degraded("no jwks_uri");
            using var jwks = JsonDocument.Parse(await http.GetStringAsync(jwksUri, cts.Token));
            return jwks.RootElement.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Array && keys.GetArrayLength() > 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Degraded("jwks empty");
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return HealthCheckResult.Degraded("jwks unreachable"); // no exception text: it can carry the URL
        }
    }
}
