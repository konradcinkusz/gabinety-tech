using Gabinety.Assistant.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Gabinety.Assistant.Features.Health;

/// <summary>Reports the optional dependencies by name, Healthy or Degraded, never Unhealthy (SPEC 6.0); the llm check comes from AddLlmGateway(). /health stays the kernel's plain probe; /health/details names the checks.</summary>
public sealed class HealthFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<JwksCache>();
        builder.Services.AddHttpClient<IdentityHealthCheck>(c => c.Timeout = IdentityHealthCheck.Timeout);
        builder.Services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database")
            .AddCheck<IdentityHealthCheck>("identity");
    }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/health/details", async (HealthCheckService health, CancellationToken ct) =>
        {
            var report = await health.CheckHealthAsync(ct);
            return Results.Ok(new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() }),
            });
        }).AllowAnonymous();
}

/// <summary>Healthy on InMemory, or when migrations are Done and the connection works; Degraded otherwise.</summary>
public sealed class DatabaseHealthCheck(AssistantDbContext db, MigrationState migrations) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (!db.Database.IsRelational()) return HealthCheckResult.Healthy("in-memory");
        if (migrations.Status != MigrationStatus.Done) return HealthCheckResult.Degraded($"migrations {migrations.Status}");
        try { return await db.Database.CanConnectAsync(ct) ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded("cannot connect"); }
        catch (Exception) when (!ct.IsCancellationRequested) { return HealthCheckResult.Degraded("cannot connect"); }
    }
}
