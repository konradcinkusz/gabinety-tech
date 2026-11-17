using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Gabinety.Reputation.Clients;
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

/// <summary>Stands in for Directory's by-user lookup: a user id maps to a practitioner id, anyone else is 404; switches for "down" and a free-form override.</summary>
sealed class FakeDirectory : HttpMessageHandler
{
    public const string Key = "k-directory-DO-NOT-LEAK-9876";
    public const string BaseUrl = "http://directory.demo.invalid";

    public readonly ConcurrentDictionary<string, Guid> Practitioners = new();
    public Func<HttpRequestMessage, int, HttpResponseMessage>? Override; // (request, call number from 1)
    public bool Down;
    public int Calls;
    public string? LastKeyHeader;
    public string? LastPath;

    public Guid Add(string userId) => Practitioners[userId] = Guid.NewGuid();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
    {
        var n = Interlocked.Increment(ref Calls);
        LastKeyHeader = r.Headers.TryGetValues("X-Internal-Key", out var v) ? v.Single() : null;
        LastPath = r.RequestUri!.AbsolutePath;
        if (Down) throw new HttpRequestException($"connection refused to {BaseUrl} with {Key}");
        if (Override is { } o) return Task.FromResult(o(r, n));
        const string prefix = "/internal/v1/practitioners/by-user/";
        var path = r.RequestUri!.AbsolutePath;
        if (path.StartsWith(prefix) && Practitioners.TryGetValue(Uri.UnescapeDataString(path[prefix.Length..]), out var id))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { practitionerId = id, status = "Approved" }) });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

/// <summary>Reputation host for the reply route: signed tokens, a fake Directory, a manual clock and a log sink. The InMemory database is shared by name: use your own ids.</summary>
sealed class ReplyHost : WebApplicationFactory<Program>
{
    public const string Audience = "reputation-test", Issuer = "https://issuer.demo.invalid";
    public const string GoodReply = "Dziękuję za opinię, synthetyczna odpowiedź gabinetu.";
    readonly RSA _rsa = RSA.Create(2048);
    public FakeDirectory Directory { get; } = new();
    public ReviewsClock Clock { get; } = new();
    public ReviewsLogSink Logs { get; } = new();

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
        builder.UseSetting("Backend:Directory", FakeDirectory.BaseUrl);
        builder.UseSetting("Internal:Keys:Directory", FakeDirectory.Key);
        foreach (var (k, v) in ExtraSettings) builder.UseSetting(k, v);
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IDirectoryClient>();
            s.AddSingleton<IDirectoryClient>(new DirectoryClient(
                new HttpClient(Directory) { BaseAddress = new Uri(FakeDirectory.BaseUrl + "/") }, FakeDirectory.Key, retryDelay: TimeSpan.Zero));
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

    public Dictionary<string, string?> ExtraSettings { get; } = new();

    protected override void Dispose(bool disposing) { if (disposing) _rsa.Dispose(); base.Dispose(disposing); }

    public async Task<Guid> SeedReview(Guid practitionerId, Gabinety.Contracts.Reputation.ReviewStatus status = Gabinety.Contracts.Reputation.ReviewStatus.Published, string? reply = null)
    {
        var id = Guid.NewGuid();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReputationDbContext>();
        db.Reviews.Add(new Review
        {
            Id = id, BookingId = Guid.NewGuid(), PractitionerId = practitionerId, ClientUserId = "demo-reviewer", Rating = 5,
            Text = "Synthetyczna opinia o długości powyżej dwudziestu znaków.", Status = status,
            CreatedAt = Clock.GetUtcNow(), PublishedAt = status == Gabinety.Contracts.Reputation.ReviewStatus.Published ? Clock.GetUtcNow() : null,
            ReplyText = reply, RepliedAt = reply is null ? null : Clock.GetUtcNow(),
        });
        await db.SaveChangesAsync();
        return id;
    }

    public async Task<T> Query<T>(Func<ReputationDbContext, Task<T>> q)
    {
        using var scope = Services.CreateScope();
        return await q(scope.ServiceProvider.GetRequiredService<ReputationDbContext>());
    }

    public Task<Review> ReviewOf(Guid id) => Query(db => db.Reviews.AsNoTracking().SingleAsync(r => r.Id == id));
    public Task<List<ModerationEvent>> EventsOf(Guid id) => Query(db => db.ModerationEvents.AsNoTracking().Where(e => e.ReviewId == id).ToListAsync());

    public static Task<HttpResponseMessage> Reply(HttpClient c, Guid reviewId, string? text = null) =>
        c.PostAsJsonAsync($"/api/v1/reviews/{reviewId}/reply", new { text = text ?? GoodReply });
}
