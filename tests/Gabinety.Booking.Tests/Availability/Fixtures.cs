using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Gabinety.Booking.Clients;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Gabinety.Booking.Tests;

/// <summary>A TimeProvider the test moves by hand.</summary>
sealed class ManualClock : TimeProvider
{
    DateTimeOffset _now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan t) => _now += t;
}

/// <summary>Stands in for Directory: a user-to-practitioner map plus switches for "down" and "503".</summary>
sealed class FakeDirectory : HttpMessageHandler
{
    public const string Key = "k-test-DO-NOT-LEAK-1234";
    public const string BaseUrl = "http://directory.demo.invalid";

    public readonly Dictionary<string, Guid> Practitioners = new();
    public Func<HttpRequestMessage, int, HttpResponseMessage>? Override; // (request, call number from 1)
    public bool Down;
    public int Calls, ByUserCalls;
    public string? LastKeyHeader;

    public Guid Add(string userId) => Practitioners[userId] = Guid.NewGuid();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
    {
        var n = Interlocked.Increment(ref Calls);
        LastKeyHeader = r.Headers.TryGetValues("X-Internal-Key", out var v) ? v.Single() : null;
        if (Down) throw new HttpRequestException($"connection refused to {BaseUrl} with {Key}");
        if (Override is { } o) return Task.FromResult(o(r, n));
        var path = r.RequestUri!.AbsolutePath;
        if (path == "/alive") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        const string byUser = "/internal/v1/practitioners/by-user/";
        if (path.StartsWith(byUser))
        {
            Interlocked.Increment(ref ByUserCalls);
            return Task.FromResult(Practitioners.TryGetValue(Uri.UnescapeDataString(path[byUser.Length..]), out var id)
                ? Json(new { practitionerId = id, status = "Draft" })
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    public static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
}

/// <summary>Booking host with an in-memory RSA key as its "JWKS", the audience "booking-test" and a fake Directory.</summary>
sealed class BookingFactory : WebApplicationFactory<Program>
{
    public const string Audience = "booking-test", Issuer = "https://issuer.demo.invalid";
    readonly RSA _rsa = RSA.Create(2048);
    public FakeDirectory Directory { get; } = new();
    public ManualClock Clock { get; } = new();

    public HttpClient ClientFor(string? userId)
    {
        var c = CreateClient();
        if (userId != null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(userId));
        return c;
    }

    public string Token(string sub, params (string Type, string Value)[] claims)
    {
        var d = new SecurityTokenDescriptor
        {
            Issuer = Issuer, Audience = Audience, Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = sub },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = "k1" }, SecurityAlgorithms.RsaSha256),
        };
        foreach (var (t, v) in claims) d.Claims[t] = v;
        return new JsonWebTokenHandler().CreateToken(d);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("GABINETY_FAKE", "1"); // Mock payments run only in the fake profile (F-01)
        builder.UseSetting("Identity:Audience", Audience);
        builder.UseSetting("Backend:Directory", FakeDirectory.BaseUrl);
        builder.UseSetting("Internal:Keys:Directory", FakeDirectory.Key);
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IDirectoryClient>();
            s.AddSingleton<IDirectoryClient>(new DirectoryClient(
                new HttpClient(Directory) { BaseAddress = new Uri(FakeDirectory.BaseUrl) }, FakeDirectory.Key, retryDelay: TimeSpan.Zero));
            s.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                var cfg = new OpenIdConnectConfiguration { Issuer = Issuer };
                cfg.SigningKeys.Add(new RsaSecurityKey(_rsa) { KeyId = "k1" });
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(cfg);
            });
        });
    }

    protected override void Dispose(bool disposing) { if (disposing) _rsa.Dispose(); base.Dispose(disposing); }
}

static class Http
{
    public static Task<HttpResponseMessage> PutAvailability(this HttpClient c, object[] rules, object[]? exceptions = null) =>
        c.PutAsJsonAsync("/api/v1/me/availability", new { rules, exceptions = exceptions ?? [] });

    public static object Rule(int day, string start, string end) => new { isoWeekday = day, startLocal = start, endLocal = end };
    public static object Day(string date) => new { dateLocal = date };

    public static async Task<JsonElement> Body(this HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
}
