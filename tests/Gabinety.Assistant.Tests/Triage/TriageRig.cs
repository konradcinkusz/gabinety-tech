using Gabinety.Assistant.Persistence;
using Gabinety.Assistant.Tests.Brief;
using Gabinety.Assistant.Tests.Usage;
using Gabinety.Assistant.Usage;
using Gabinety.Llm;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Gabinety.Assistant.Tests.Triage;

/// <summary>An Assistant host for the internal triage endpoint: X-Internal-Key, fake clock, optional gateway and settings.
/// Every rig owns a private InMemory database (the host's own is registered under one shared name, which all hosts of a process would otherwise see), so Rows() is exactly this rig's rows.</summary>
public sealed class TriageRig : IDisposable
{
    public const string Key = "demo-internal-key-not-a-secret";
    public readonly FakeClock Clock = new(new DateTimeOffset(2100, 1, 1, 12, 0, 0, TimeSpan.Zero));
    public readonly CaptureLogger Logs = new();
    public readonly WebApplicationFactory<Program> Factory;
    readonly string _dbName = "triage-" + Guid.NewGuid();

    public TriageRig(ILlmGateway? gateway = null, params (string Key, string Value)[] settings)
    {
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Llm:Provider", "Fake");
            b.UseSetting("Internal:Key", Key);
            foreach (var (k, v) in settings) b.UseSetting(k, v);
            b.ConfigureLogging(l => l.AddProvider(Logs));
            b.ConfigureServices(s =>
            {
                s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(Clock);
                s.RemoveAll<DbContextOptions<AssistantDbContext>>(); s.RemoveAll<DbContextOptions>();
                s.AddDbContext<AssistantDbContext>(o => o.UseInMemoryDatabase(_dbName));
                if (gateway is not null) { s.RemoveAll<ILlmGateway>(); s.AddSingleton(gateway); }
            });
        });
    }

    public Task<HttpResponseMessage> Post(object body, string? key = Key)
    {
        var c = Factory.CreateClient();
        if (key is not null) c.DefaultRequestHeaders.Add("X-Internal-Key", key);
        return c.PostAsJsonAsync("/internal/v1/assistant/review-triage", body);
    }

    public Task<HttpResponseMessage> Post(int rating, string text, string? key = Key) => Post(new { rating, text }, key);

    public async Task<List<UsageRow>> Rows()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AssistantDbContext>().Usage.OrderBy(u => u.At).ToListAsync();
    }

    public void Dispose() => Factory.Dispose();
}
