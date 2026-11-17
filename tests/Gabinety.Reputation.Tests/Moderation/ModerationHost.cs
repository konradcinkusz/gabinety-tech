using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Gabinety.Reputation.Tests.Moderation;

sealed class ModerationClock : TimeProvider
{
    DateTimeOffset _now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan t) => _now += t;
}

/// <summary>Collects every log line so a test can assert that review text and moderator notes never reach a log.</summary>
sealed class ModerationLogSink : ILoggerProvider
{
    public System.Collections.Concurrent.ConcurrentQueue<string> Lines { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Sink(this, categoryName);
    public void Dispose() { }

    sealed class Sink(ModerationLogSink owner, string category) : ILogger
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

/// <summary>Reputation host with role-bearing tokens (the WS-Federation role claim authservice issues), a manual clock and a log sink. The InMemory database is shared by name: filter by your own ids.</summary>
sealed class ModerationHost : WebApplicationFactory<Program>
{
    public const string RoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
    const string Audience = "reputation-test", Issuer = "https://issuer.demo.invalid";
    public const string ReviewText = "Synthetyczna opinia MODERACJA-TEKST-NIE-DO-LOGU: spotkanie było rzeczowe.";
    readonly RSA _rsa = RSA.Create(2048);
    public ModerationClock Clock { get; } = new();
    public ModerationLogSink Logs { get; } = new();
    public Dictionary<string, string?> Settings { get; } = new();

    public HttpClient As(string? sub, string? role = null)
    {
        var c = CreateClient();
        if (sub is null) return c;
        var claims = new Dictionary<string, object> { ["sub"] = sub };
        if (role is not null) claims[RoleClaim] = role;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer, Audience = Audience, Expires = DateTime.UtcNow.AddMinutes(5), Claims = claims,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = "k1" }, SecurityAlgorithms.RsaSha256),
        }));
        return c;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Identity:Audience", Audience);
        builder.UseSetting("Backend:Booking", "http://booking.demo.invalid");
        builder.UseSetting("Internal:Keys:Booking", "k-test-moderation");
        foreach (var (k, v) in Settings) builder.UseSetting(k, v);
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(Clock);
            s.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
            s.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                var cfg = new OpenIdConnectConfiguration { Issuer = Issuer };
                cfg.SigningKeys.Add(new RsaSecurityKey(_rsa) { KeyId = "k1" });
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(cfg);
            });
        });
    }

    protected override void Dispose(bool disposing) { if (disposing) _rsa.Dispose(); base.Dispose(disposing); }

    public static Review NewReview(Guid practitionerId, ReviewStatus status, int rating = 5, DateTimeOffset? at = null) => new()
    {
        Id = Guid.CreateVersion7(), BookingId = Guid.NewGuid(), PractitionerId = practitionerId, ClientUserId = "demo-reviewer", Rating = (short)rating,
        Text = ReviewText, Status = status, HoldReasons = status == ReviewStatus.Held ? ["spam"] : [],
        CreatedAt = at ?? new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero),
        PublishedAt = status == ReviewStatus.Published ? at ?? new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero) : null,
    };

    /// <summary>Seeds reviews and a rating summary that matches the Published ones (what the create path would have written).</summary>
    public async Task Seed(Guid practitionerId, params Review[] reviews)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReputationDbContext>();
        db.Reviews.AddRange(reviews);
        var pub = reviews.Where(r => r.Status == ReviewStatus.Published).ToList();
        if (pub.Count > 0)
            db.RatingSummaries.Add(new RatingSummary { PractitionerId = practitionerId, Count = pub.Count, Sum = pub.Sum(r => (int)r.Rating), Version = pub.Count });
        db.ModerationEvents.AddRange(reviews.Select(r => new ModerationEvent
            { Id = Guid.CreateVersion7(), ReviewId = r.Id, Kind = ModerationEventKind.Created, Actor = "system", At = r.CreatedAt }));
        await db.SaveChangesAsync();
    }

    public async Task<T> Query<T>(Func<ReputationDbContext, Task<T>> q)
    {
        using var scope = Services.CreateScope();
        return await q(scope.ServiceProvider.GetRequiredService<ReputationDbContext>());
    }

    public Task<Review> ReviewOf(Guid id) => Query(db => db.Reviews.AsNoTracking().SingleAsync(r => r.Id == id));
    public Task<RatingSummary?> SummaryOf(Guid p) => Query(db => db.RatingSummaries.AsNoTracking().SingleOrDefaultAsync(s => s.PractitionerId == p));
    public Task<List<ModerationEvent>> EventsOf(Guid reviewId) =>
        Query(db => db.ModerationEvents.AsNoTracking().Where(e => e.ReviewId == reviewId).OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync());

    /// <summary>The summary a recount of the Published rows gives.</summary>
    public async Task<(int Count, int Sum)> Recount(Guid p)
    {
        var rows = await Query(db => db.Reviews.AsNoTracking().Where(r => r.PractitionerId == p && r.Status == ReviewStatus.Published).ToListAsync());
        return (rows.Count, rows.Sum(r => (int)r.Rating));
    }

    public static Task<HttpResponseMessage> Publish(HttpClient c, Guid id) => c.PostAsync($"/api/v1/admin/reviews/{id}/publish", null);
    public static Task<HttpResponseMessage> Reject(HttpClient c, Guid id, string? reason = "Naruszenie regulaminu serwisu") =>
        c.PostAsJsonAsync($"/api/v1/admin/reviews/{id}/reject", new { reason });

    /// <summary>All admin list items of a status for one practitioner (pages through the shared InMemory database).</summary>
    public static async Task<List<JsonElement>> ListAll(HttpClient c, Guid practitionerId, string? status = null)
    {
        var items = new List<JsonElement>(); string? cursor = null;
        do
        {
            var url = "/api/v1/admin/reviews?limit=50" + (status is null ? "" : $"&status={status}") + (cursor is null ? "" : $"&cursor={cursor}");
            var r = await c.GetAsync(url);
            r.EnsureSuccessStatusCode();
            var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
            items.AddRange(doc.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("practitionerId").GetGuid() == practitionerId).Select(i => i.Clone()));
            cursor = doc.TryGetProperty("nextCursor", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        } while (cursor is not null);
        return items;
    }
}
