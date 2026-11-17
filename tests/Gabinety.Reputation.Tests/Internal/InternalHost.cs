using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Features.Internal.TestClock;
using Gabinety.Reputation.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Gabinety.Reputation.Tests;

sealed class ManualClock : TimeProvider
{
    DateTimeOffset _now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan t) => _now += t;
}

/// <summary>Collects every log line (message, structured values, exception) so a test can assert that a key, a URL or a review text never reaches a log.</summary>
sealed class LogSink : ILoggerProvider
{
    public ConcurrentQueue<string> Lines { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Sink(this, categoryName);
    public void Dispose() { }

    sealed class Sink(LogSink owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> kv ? string.Join(",", kv.Select(p => $"{p.Key}={p.Value}")) : "";
            owner.Lines.Enqueue($"{category}: {formatter(state, exception)} [{values}] {exception}");
        }
    }
}

/// <summary>
/// Reputation host for the internal endpoints and the summary push: its OWN in-memory database (the demo reset removes every demo- row, so it must not
/// share "reputationdb" with parallel tests), a manual clock (wrapped by the offset clock when the fake profile asks for it), a log sink, settable configuration.
/// </summary>
sealed class InternalHost : WebApplicationFactory<Program>
{
    public const string Key = "k-internal-DO-NOT-LEAK-5678";
    public const string Text = "Synthetyczna opinia TEKST-NIE-DO-LOGU: spotkanie było rzeczowe.";
    public const string Reply = "Odpowiedź ODPOWIEDZ-NIE-DO-LOGU, dziękuję za opinię.";
    public const string Note = "Notatka NOTATKA-NIE-DO-LOGU moderatora.";
    readonly string _db = "internal-" + Guid.NewGuid().ToString("N");
    public ManualClock Clock { get; } = new();
    public LogSink Logs { get; } = new();
    public Dictionary<string, string?> Settings { get; } = new() { ["Internal:Key"] = Key };

    /// <summary>A fake-profile host: <c>GABINETY_FAKE=1</c> and the offset clock registered over the manual one, as in a real fake-profile start.</summary>
    public static InternalHost Fake()
    {
        var h = new InternalHost();
        h.Settings["GABINETY_FAKE"] = "1";
        h.Wrap = true;
        return h;
    }

    public bool Wrap;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (k, v) in Settings) builder.UseSetting(k, v);
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(Clock);
            if (Wrap) s.AddTestClock();
            s.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
            s.RemoveAll<DbContextOptions<ReputationDbContext>>();
            s.RemoveAll(typeof(IDbContextOptionsConfiguration<ReputationDbContext>));
            s.RemoveAll<ReputationDbContext>();
            s.AddDbContext<ReputationDbContext>(o => o.UseInMemoryDatabase(_db));
        });
    }

    public async Task Seed(Action<ReputationDbContext> add)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReputationDbContext>();
        add(db);
        await db.SaveChangesAsync();
    }

    public async Task<T> Query<T>(Func<ReputationDbContext, Task<T>> q)
    {
        using var scope = Services.CreateScope();
        return await q(scope.ServiceProvider.GetRequiredService<ReputationDbContext>());
    }

    public Task<Review> ReviewOf(Guid id) => Query(db => db.Reviews.AsNoTracking().SingleAsync(r => r.Id == id));
    public Task<RatingSummary?> SummaryOf(Guid p) => Query(db => db.RatingSummaries.AsNoTracking().SingleOrDefaultAsync(s => s.PractitionerId == p));
    public Task<List<ModerationEvent>> EventsOf(Guid reviewId) => Query(db => db.ModerationEvents.AsNoTracking().Where(e => e.ReviewId == reviewId).ToListAsync());

    public static Review NewReview(Guid practitioner, string reviewer, int rating = 5, ReviewStatus status = ReviewStatus.Published, bool withExtras = false) => new()
    {
        Id = Guid.CreateVersion7(), BookingId = Guid.NewGuid(), PractitionerId = practitioner, ClientUserId = reviewer, Rating = (short)rating, Text = Text,
        Status = status, HoldReasons = status == ReviewStatus.Held ? ["spam"] : [], CreatedAt = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero),
        PublishedAt = status == ReviewStatus.Published ? new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero) : null,
        ReplyText = withExtras ? Reply : null, RepliedAt = withExtras ? new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero) : null,
        ModerationNote = withExtras ? Note : null, ModeratedBy = withExtras ? "demo-admin" : null,
    };

    public static ModerationEvent NewEvent(Guid review, ModerationEventKind kind, string? note = null, string actor = "system") =>
        new() { Id = Guid.CreateVersion7(), ReviewId = review, Kind = kind, Actor = actor, Note = note, At = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero) };

    public Task<HttpResponseMessage> PostRaw(string path, string json, string? key = Key) => Send(HttpMethod.Post, path, key, json);

    public Task<HttpResponseMessage> Send(HttpMethod method, string path, string? key = Key, string? json = null)
    {
        var req = new HttpRequestMessage(method, path);
        if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (key != null) req.Headers.Add("X-Internal-Key", key);
        return CreateClient().SendAsync(req);
    }
}

static class HttpFx
{
    public static async Task<JsonElement> Body(this HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
}
