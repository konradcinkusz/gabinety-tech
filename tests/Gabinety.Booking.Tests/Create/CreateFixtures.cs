using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Gabinety.Booking.Clients;
using Gabinety.Booking.Persistence;
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

namespace Gabinety.Booking.Tests;

using Booking = Gabinety.Booking.Features.Bookings.Entities.Booking;

/// <summary>Collects every log line (message, structured values and exception) so a test can assert what was NOT logged.</summary>
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
/// Booking host for the create endpoint: signed tokens, a stub Directory whose offering the test can change after creation,
/// a manual clock (now = 2026-11-16 12:00 UTC, a Monday; Warsaw is UTC+1), a log sink and settable configuration.
/// </summary>
sealed class CreateHost : WebApplicationFactory<Program>
{
    public const string Audience = "booking-test", Issuer = "https://issuer.demo.invalid";
    public const string Tuesday10 = "2026-11-17T10:00:00Z"; // 11:00 Warsaw, inside the 09:00-17:00 work week
    readonly RSA _rsa = RSA.Create(2048);
    public FakeDirectory Directory { get; } = new();
    public ManualClock Clock { get; } = new();
    public LogSink Logs { get; } = new();
    public Dictionary<string, string?> Settings { get; } = new();

    public Guid Practitioner { get; } = Guid.NewGuid();
    public Guid Offering { get; } = Guid.NewGuid();
    public string OwnerUserId = "demo-practitioner-user";
    public string Title = "demo-offering";
    public int Minutes = 60;
    public long PriceMinor = 10000;
    public string Status = "Approved";
    public bool Active = true;

    public CreateHost()
    {
        Directory.Override = (r, _) => r.RequestUri!.AbsolutePath == $"/internal/v1/offerings/{Offering}"
            ? FakeDirectory.Json(new
            {
                offeringId = Offering, practitionerId = Practitioner, practitionerUserId = OwnerUserId, practitionerStatus = Status,
                title = Title, durationMinutes = Minutes, price = new { amountMinor = PriceMinor, currency = "PLN" },
                meetingUrl = (string?)null, isActive = Active,
            })
            : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
    }

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
        foreach (var (k, v) in Settings) builder.UseSetting(k, v);
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IDirectoryClient>();
            s.AddSingleton<IDirectoryClient>(new DirectoryClient(
                new HttpClient(Directory) { BaseAddress = new Uri(FakeDirectory.BaseUrl) }, FakeDirectory.Key, retryDelay: TimeSpan.Zero));
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

    public async Task Seed(Action<BookingDbContext> act)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        act(db);
        await db.SaveChangesAsync();
    }

    public async Task<T> Query<T>(Func<BookingDbContext, Task<T>> q)
    {
        using var scope = Services.CreateScope();
        return await q(scope.ServiceProvider.GetRequiredService<BookingDbContext>());
    }

    /// <summary>Mon-Fri 09:00-17:00 local for the practitioner.</summary>
    public Task WorkWeek() => Seed(db =>
    {
        for (short d = 1; d <= 5; d++)
            db.Rules.Add(new() { Id = Guid.NewGuid(), PractitionerId = Practitioner, IsoWeekday = d, StartLocal = new(9, 0), EndLocal = new(17, 0) });
    });

    public Task<List<Booking>> Bookings() => Query(db => db.Bookings.AsNoTracking().Where(b => b.PractitionerId == Practitioner).ToListAsync());

    public static async Task<HttpResponseMessage> Post(HttpClient c, object body, string? key)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bookings") { Content = JsonContent.Create(body) };
        if (key != null) req.Headers.Add("Idempotency-Key", key);
        return await c.SendAsync(req);
    }

    public object Req(string start = Tuesday10, string? text = null, Guid? offering = null) => new
    {
        offeringId = offering ?? Offering, startUtc = start,
        brief = new { text = text ?? "demo-brief: I need help with a first review of my plan", problemSlug = (string?)null, aiAssisted = false },
    };

    public static string Key() => Guid.NewGuid().ToString();
}
