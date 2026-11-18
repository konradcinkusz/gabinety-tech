using System.Net.Http.Headers;
using System.Security.Cryptography;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Features.Analytics;
using Gabinety.Reputation.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Gabinety.Reputation.Tests.Analytics;

/// <summary>Booking's side of the join: the pages the test hands out, in order, and a record of what Reputation asked.</summary>
sealed class FakePairCounts : IPairCountsClient
{
    public List<Page<PairCountDto>> Pages { get; } = [];
    public List<(int Min, string? Cursor, int Limit)> Calls { get; } = [];
    public bool Unavailable { get; set; }

    public Task<Page<PairCountDto>> GetAsync(int minBookings, string? cursor, int limit, CancellationToken ct)
    {
        Calls.Add((minBookings, cursor, limit));
        if (Unavailable) throw new BookingUnavailableForAnalyticsException();
        var index = cursor is null ? 0 : int.Parse(cursor[1..]);
        return Task.FromResult(Pages[index]);
    }

    /// <summary>Pages are chained: page i points at "p{i+1}".</summary>
    public FakePairCounts Add(params PairCountDto[] items)
    {
        Pages.Add(new Page<PairCountDto>(items, null));
        if (Pages.Count > 1) Pages[^2] = Pages[^2] with { NextCursor = $"p{Pages.Count - 1}" };
        return this;
    }
}

sealed class AnalyticsLogSink : ILoggerProvider
{
    public System.Collections.Concurrent.ConcurrentQueue<string> Lines { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Sink(this);
    public void Dispose() { }

    sealed class Sink(AnalyticsLogSink owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            owner.Lines.Enqueue($"{formatter(state, exception)} {exception}");
    }
}

/// <summary>Reputation host with role-bearing tokens and Booking replaced by <see cref="FakePairCounts"/>. The InMemory database is shared by name: every test uses its own ids.</summary>
sealed class AnalyticsHost : WebApplicationFactory<Program>
{
    public const string RoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
    const string Audience = "reputation-test", Issuer = "https://issuer.demo.invalid";
    public const string ReviewText = "Synthetyczna opinia PARY-TEKST-NIE-DO-LISTY: spotkanie było rzeczowe.";
    readonly RSA _rsa = RSA.Create(2048);
    public FakePairCounts Booking { get; } = new();
    public AnalyticsLogSink Logs { get; } = new();
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
        foreach (var (k, v) in Settings) builder.UseSetting(k, v);
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IPairCountsClient>();
            s.AddSingleton<IPairCountsClient>(Booking);
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

    public static PairCountDto Pair(string client, Guid practitioner, int bookings) => new($"{client}:{practitioner}", client, practitioner, bookings);

    public static Review NewReview(string client, Guid practitioner, int rating, ReviewStatus status = ReviewStatus.Published) => new()
    {
        Id = Guid.CreateVersion7(), BookingId = Guid.NewGuid(), PractitionerId = practitioner, ClientUserId = client, Rating = (short)rating,
        Text = ReviewText, Status = status, HoldReasons = [], CreatedAt = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero),
        PublishedAt = status == ReviewStatus.Published ? new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero) : null,
    };

    public async Task Seed(params Review[] reviews)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReputationDbContext>();
        db.Reviews.AddRange(reviews);
        await db.SaveChangesAsync();
    }
}
