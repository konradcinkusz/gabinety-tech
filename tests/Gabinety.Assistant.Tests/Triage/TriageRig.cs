using System.Security.Cryptography;
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
/// The InMemory database is shared by name in one process, so each rig owns a random UTC day far in the future and reads only that day's rows.</summary>
public sealed class TriageRig : IDisposable
{
    public const string Key = "demo-internal-key-not-a-secret";
    public readonly FakeClock Clock = new(new DateTimeOffset(2100, 1, 1, 12, 0, 0, TimeSpan.Zero).AddDays(RandomNumberGenerator.GetInt32(0, 20_000)));
    public readonly CaptureLogger Logs = new();
    public readonly WebApplicationFactory<Program> Factory;
    readonly DateTimeOffset _dayStart;

    public TriageRig(ILlmGateway? gateway = null, params (string Key, string Value)[] settings)
    {
        _dayStart = new DateTimeOffset(Clock.Now.UtcDateTime.Date, TimeSpan.Zero);
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Llm:Provider", "Fake");
            b.UseSetting("Internal:Key", Key);
            foreach (var (k, v) in settings) b.UseSetting(k, v);
            b.ConfigureLogging(l => l.AddProvider(Logs));
            b.ConfigureServices(s =>
            {
                s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(Clock);
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
        var from = _dayStart; var to = _dayStart.AddDays(1);
        return await scope.ServiceProvider.GetRequiredService<AssistantDbContext>().Usage
            .Where(u => u.At >= from && u.At < to).OrderBy(u => u.At).ToListAsync();
    }

    public void Dispose() => Factory.Dispose();
}
