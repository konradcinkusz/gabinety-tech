using System.Collections.Concurrent;
using Gabinety.Contracts.Assistant;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Clients;
using Gabinety.Reputation.Jobs;
using Gabinety.Reputation.Persistence;
using Gabinety.Reputation.Tests.Moderation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Gabinety.Reputation.Tests.Triage;

/// <summary>An Assistant the test controls. Records every call; the review text is the key a test counts by.</summary>
sealed class FakeAssistant : IAssistantClient
{
    public ConcurrentQueue<(int Rating, string Text)> Calls { get; } = new();
    public Func<int, string, AssistantTriageResult> Answer { get; set; } = (_, _) => new AssistantTriageResult.Verdict(TriageVerdict.None, [], "demo-model");
    public Task<AssistantTriageResult> TriageAsync(int rating, string text, CancellationToken ct)
    {
        Calls.Enqueue((rating, text));
        return Task.FromResult(Answer(rating, text));
    }
    public int CallsFor(string text) => Calls.Count(c => c.Text == text);
}

/// <summary>
/// Reputation host with the Assistant replaced, a hand-moved clock, a log sink and its OWN InMemory database (a unique name), so the job
/// never sees another test class's reviews and no other test sees this job's writes.
/// </summary>
sealed class TriageHost : WebApplicationFactory<Program>
{
    public ModerationClock Clock { get; } = new();
    public ModerationLogSink Logs { get; } = new();
    public FakeAssistant Assistant { get; } = new();
    public Dictionary<string, string?> Settings { get; } = new();
    readonly string _db = "triage-" + Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Identity:Audience", "reputation-test");
        foreach (var (k, v) in Settings) builder.UseSetting(k, v);
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(Clock);
            s.RemoveAll<IAssistantClient>();
            s.AddSingleton<IAssistantClient>(Assistant);
            s.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
            s.RemoveAll<DbContextOptions<ReputationDbContext>>();
            s.RemoveAll<ReputationDbContext>();
            s.AddDbContext<ReputationDbContext>(o => o.UseInMemoryDatabase(_db));
        });
    }

    public TriageJob Job => Services.GetRequiredService<TriageJob>();
    public Task<int> Run() => Job.RunOnceAsync(default);

    public static Review Held(string text, DateTimeOffset? at = null, int rating = 4) => new()
    {
        Id = Guid.CreateVersion7(), BookingId = Guid.NewGuid(), PractitionerId = Guid.NewGuid(), ClientUserId = "demo-reviewer", Rating = (short)rating,
        Text = text, Status = ReviewStatus.Held, HoldReasons = ["contains_link"], CreatedAt = at ?? new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero),
    };

    public static string UniqueText() => $"Synthetyczna opinia {Guid.NewGuid():N} do triage.";

    public async Task Seed(params Review[] reviews) => await Query(async db => { db.Reviews.AddRange(reviews); await db.SaveChangesAsync(); return 0; });

    public async Task<T> Query<T>(Func<ReputationDbContext, Task<T>> q)
    {
        using var scope = Services.CreateScope();
        return await q(scope.ServiceProvider.GetRequiredService<ReputationDbContext>());
    }

    public Task<Review> ReviewOf(Guid id) => Query(db => db.Reviews.AsNoTracking().SingleAsync(r => r.Id == id));
    public Task<List<ModerationEvent>> TriageEventsOf(Guid id) =>
        Query(db => db.ModerationEvents.AsNoTracking().Where(e => e.ReviewId == id && e.Actor == "triage").OrderBy(e => e.At).ToListAsync());
}
