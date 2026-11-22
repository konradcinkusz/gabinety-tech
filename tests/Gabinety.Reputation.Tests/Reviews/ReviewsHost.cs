using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Gabinety.Reputation.Clients;
using Gabinety.Reputation.Jobs.SummaryPush;
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

namespace Gabinety.Reputation.Tests.Reviews;

/// <summary>A TimeProvider the test moves by hand (now = 2026-11-16 12:00 UTC).</summary>
sealed class ReviewsClock : TimeProvider
{
    DateTimeOffset _now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan t) => _now += t;
}

/// <summary>Collects every log line (message, structured values and exception) so a test can assert what was NOT logged.</summary>
sealed class ReviewsLogSink : ILoggerProvider
{
    public ConcurrentQueue<string> Lines { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Sink(this, categoryName);
    public void Dispose() { }

    sealed class Sink(ReviewsLogSink owner, string category) : ILogger
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

/// <summary>Stands in for Booking: reviewability answers per booking id, plus switches for "down" and a free-form override.</summary>
sealed class FakeBooking : HttpMessageHandler
{
    public const string Key = "k-test-DO-NOT-LEAK-5678";
    public const string BaseUrl = "http://booking.demo.invalid";

    public readonly ConcurrentDictionary<Guid, (bool Reviewable, string ClientUserId, Guid PractitionerId)> Bookings = new();
    public Func<HttpRequestMessage, int, HttpResponseMessage>? Override; // (request, call number from 1)
    public bool Down;
    public int Calls;
    public string? LastKeyHeader;
    public string? LastPath;

    public Guid Add(string clientUserId, Guid practitionerId, bool reviewable = true)
    {
        var id = Guid.NewGuid();
        Bookings[id] = (reviewable, clientUserId, practitionerId);
        return id;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
    {
        var n = Interlocked.Increment(ref Calls);
        LastKeyHeader = r.Headers.TryGetValues("X-Internal-Key", out var v) ? v.Single() : null;
        LastPath = r.RequestUri!.AbsolutePath;
        if (Down) throw new HttpRequestException($"connection refused to {BaseUrl} with {Key}");
        if (Override is { } o) return Task.FromResult(o(r, n));
        const string prefix = "/internal/v1/bookings/", suffix = "/reviewability";
        var path = r.RequestUri!.AbsolutePath;
        if (path.StartsWith(prefix) && path.EndsWith(suffix) && Guid.TryParse(path[prefix.Length..^suffix.Length], out var id) && Bookings.TryGetValue(id, out var b))
            return Task.FromResult(Json(new { reviewable = b.Reviewable, clientUserId = b.ClientUserId, practitionerId = b.PractitionerId, completedAt = "2026-11-15T10:00:00Z" }));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    public static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
}

/// <summary>Reputation host: signed tokens, a fake Booking, a manual clock and a log sink. The InMemory database is shared by name: filter by your own ids.</summary>
sealed class ReviewsHost : WebApplicationFactory<Program>
{
    public const string Audience = "reputation-test", Issuer = "https://issuer.demo.invalid";
    public const string GoodText = "Synthetyczna opinia: spotkanie było rzeczowe i pomocne, polecam.";
    readonly RSA _rsa = RSA.Create(2048);
    public FakeBooking Booking { get; } = new();
    public ReviewsClock Clock { get; } = new();
    /// <summary>Stands in for Directory: records the pushes the job makes (the real client has no address here).</summary>
    public RecordingReputationClient Directory { get; } = new();
    public ReviewsLogSink Logs { get; } = new();
    public Dictionary<string, string?> Settings { get; } = new();

    public HttpClient ClientFor(string? userId)
    {
        var c = CreateClient();
        if (userId != null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(userId));
        return c;
    }

    string Token(string sub) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer, Audience = Audience, Expires = DateTime.UtcNow.AddMinutes(5),
        Claims = new Dictionary<string, object> { ["sub"] = sub },
        SigningCredentials = new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = "k1" }, SecurityAlgorithms.RsaSha256),
    });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Identity:Audience", Audience);
        builder.UseSetting("Backend:Booking", FakeBooking.BaseUrl);
        builder.UseSetting("Internal:Keys:Booking", FakeBooking.Key);
        foreach (var (k, v) in Settings) builder.UseSetting(k, v);
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IBookingClient>();
            s.AddSingleton<IBookingClient>(new BookingClient(
                new HttpClient(Booking) { BaseAddress = new Uri(FakeBooking.BaseUrl) }, FakeBooking.Key, retryDelay: TimeSpan.Zero));
            s.RemoveAll<IDirectoryReputationClient>();
            s.AddSingleton<IDirectoryReputationClient>(Directory);
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

    public async Task Seed(Action<ReputationDbContext> act)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReputationDbContext>();
        act(db);
        await db.SaveChangesAsync();
    }

    public async Task<T> Query<T>(Func<ReputationDbContext, Task<T>> q)
    {
        using var scope = Services.CreateScope();
        return await q(scope.ServiceProvider.GetRequiredService<ReputationDbContext>());
    }

    public Task<List<Review>> ReviewsOf(Guid practitionerId) => Query(db => db.Reviews.AsNoTracking().Where(r => r.PractitionerId == practitionerId).ToListAsync());
    public Task<RatingSummary?> SummaryOf(Guid practitionerId) => Query(db => db.RatingSummaries.AsNoTracking().SingleOrDefaultAsync(s => s.PractitionerId == practitionerId));

    public static Task<HttpResponseMessage> Post(HttpClient c, Guid bookingId, int rating = 5, string? text = null) =>
        c.PostAsJsonAsync("/api/v1/reviews", new { bookingId, rating, text = text ?? GoodText });
}
