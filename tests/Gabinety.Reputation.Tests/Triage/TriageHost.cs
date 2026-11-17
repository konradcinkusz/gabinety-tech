using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Gabinety.Contracts.Assistant;
using Gabinety.Reputation.Clients;
using Gabinety.Reputation.Persistence;
using Gabinety.Reputation.Tests.Reviews;
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

namespace Gabinety.Reputation.Tests.Triage;

/// <summary>An Assistant the test controls. Records every call (the counting stub of the Accept criteria).</summary>
sealed class FakeAssistant : IAssistantClient
{
    public ConcurrentQueue<(int Rating, string Text)> Calls { get; } = new();
    public Func<int, string, AssistantTriageResult> Answer { get; set; } = (_, _) => new AssistantTriageResult.Verdict(TriageVerdict.None, [], "demo-model");
    public Task<AssistantTriageResult> TriageAsync(int rating, string text, CancellationToken ct)
    {
        Calls.Enqueue((rating, text));
        return Task.FromResult(Answer(rating, text));
    }
}

/// <summary>
/// Reputation host with a fake Booking and a fake Assistant, a hand-moved clock, a log sink and its OWN InMemory database (unique name), so no other
/// test class sees these reviews.
/// </summary>
sealed class TriageHost : WebApplicationFactory<Program>
{
    const string Audience = "reputation-test", Issuer = "https://issuer.demo.invalid";
    public const string GoodText = "Synthetyczna opinia TRIAGE-TEKST-NIE-DO-LOGU: spotkanie było rzeczowe i pomocne.";
    readonly RSA _rsa = RSA.Create(2048);
    readonly string _db = "triage-" + Guid.NewGuid();
    public FakeBooking Booking { get; } = new();
    public ReviewsClock Clock { get; } = new();
    public ReviewsLogSink Logs { get; } = new();
    public FakeAssistant Assistant { get; } = new();

    public HttpClient ClientFor(string userId)
    {
        var c = CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer, Audience = Audience, Expires = DateTime.UtcNow.AddMinutes(5), Claims = new Dictionary<string, object> { ["sub"] = userId },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = "k1" }, SecurityAlgorithms.RsaSha256),
        }));
        return c;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Identity:Audience", Audience);
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IBookingClient>();
            s.AddSingleton<IBookingClient>(new BookingClient(new HttpClient(Booking) { BaseAddress = new Uri(FakeBooking.BaseUrl) }, FakeBooking.Key, retryDelay: TimeSpan.Zero));
            s.RemoveAll<IAssistantClient>();
            s.AddSingleton<IAssistantClient>(Assistant);
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(Clock);
            s.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
            s.RemoveAll<DbContextOptions<ReputationDbContext>>();
            s.RemoveAll<ReputationDbContext>();
            s.AddDbContext<ReputationDbContext>(o => o.UseInMemoryDatabase(_db));
            s.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                var cfg = new OpenIdConnectConfiguration { Issuer = Issuer };
                cfg.SigningKeys.Add(new RsaSecurityKey(_rsa) { KeyId = "k1" });
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(cfg);
            });
        });
    }

    protected override void Dispose(bool disposing) { if (disposing) _rsa.Dispose(); base.Dispose(disposing); }

    public async Task<T> Query<T>(Func<ReputationDbContext, Task<T>> q)
    {
        using var scope = Services.CreateScope();
        return await q(scope.ServiceProvider.GetRequiredService<ReputationDbContext>());
    }

    public Task<List<Review>> ReviewsOf(Guid p) => Query(db => db.Reviews.AsNoTracking().Where(r => r.PractitionerId == p).ToListAsync());
    public Task<RatingSummary?> SummaryOf(Guid p) => Query(db => db.RatingSummaries.AsNoTracking().SingleOrDefaultAsync(s => s.PractitionerId == p));
    public Task<List<ModerationEvent>> EventsOf(Guid reviewId) => Query(db => db.ModerationEvents.AsNoTracking().Where(e => e.ReviewId == reviewId).OrderBy(e => e.At).ThenBy(e => e.Kind).ToListAsync());

    /// <summary>A reviewable booking for a new user; returns (user, practitioner, booking).</summary>
    public (string User, Guid Practitioner, Guid Booking) NewBooking()
    {
        var user = "demo-" + Guid.NewGuid().ToString("N")[..8];
        var pract = Guid.NewGuid();
        return (user, pract, Booking.Add(user, pract));
    }

    public Task<HttpResponseMessage> Post(string user, Guid booking, int rating = 5, string? text = null) =>
        ClientFor(user).PostAsJsonAsync("/api/v1/reviews", new { bookingId = booking, rating, text = text ?? GoodText });
}
