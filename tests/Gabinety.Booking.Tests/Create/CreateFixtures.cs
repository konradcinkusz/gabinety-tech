using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Gabinety.Booking.Clients;
using Gabinety.Booking.Payments.Abstractions;
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
    public ConcurrentQueue<(LogLevel Level, int EventId, string Line)> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Sink(this, categoryName);
    public void Dispose() { }

    sealed class Sink(LogSink owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> kv ? string.Join(",", kv.Select(p => $"{p.Key}={p.Value}")) : "";
            var line = $"{category}: {formatter(state, exception)} [{values}] {exception}";
            owner.Lines.Enqueue(line);
            owner.Entries.Enqueue((logLevel, eventId.Id, line));
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
    /// <summary>Starts in the fake profile (<c>GABINETY_FAKE=1</c>), the only one that may run the Mock payment provider (F-01); a test of a deployed profile calls <see cref="AsDeployed"/>.</summary>
    public Dictionary<string, string?> Settings { get; } = new() { ["GABINETY_FAKE"] = "1" };

    public Guid Practitioner { get; } = Guid.NewGuid();
    public Guid Offering { get; } = Guid.NewGuid();
    public string OwnerUserId = "demo-practitioner-user";
    public string Title = "demo-offering";
    public int Minutes = 60;
    public long PriceMinor = 10000;
    public string Status = "Approved";
    public bool Active = true;
    public string? MeetingUrl;
    public Action<IServiceCollection>? Configure;
    public string PractitionerName = "Demo Practitioner", PractitionerSlug = "demo-practitioner";
    /// <summary>The <c>name</c> claim the tokens carry (authservice puts the e-mail there); null: no such claim.</summary>
    public string? NameClaim;
    public bool DirectoryDown;
    /// <summary>The <c>email</c> claim of a user's token. A user without an entry gets <c>{sub}@demo.invalid</c>; an empty string leaves the claim out.</summary>
    public Dictionary<string, string> Emails { get; } = new();

    /// <summary>
    /// A deployed profile (not fake, optionally an <c>Environment:Kind</c>). Mock cannot run there and no vendor adapter exists yet (T-09.05), so the host is given a
    /// recording stand-in named "Acme" over the registered provider; tests of the guard itself do not use this.
    /// </summary>
    public CreateHost AsDeployed(string? kind = null)
    {
        Settings["GABINETY_FAKE"] = "0";
        if (kind is not null) Settings["Environment:Kind"] = kind;
        Configure += s => { s.RemoveAll<IPaymentProvider>(); s.AddSingleton<IPaymentProvider>(new RecordingProvider("Acme")); };
        return this;
    }

    public CreateHost()
    {
        Directory.Override = (r, _) => r.RequestUri!.AbsolutePath == $"/internal/v1/offerings/{Offering}"
            ? FakeDirectory.Json(new
            {
                offeringId = Offering, practitionerId = Practitioner, practitionerUserId = OwnerUserId, practitionerStatus = Status,
                title = Title, durationMinutes = Minutes, price = new { amountMinor = PriceMinor, currency = "PLN" },
                meetingUrl = MeetingUrl, isActive = Active,
            })
            : r.RequestUri!.AbsolutePath == $"/internal/v1/practitioners/{Practitioner}"
                ? (DirectoryDown ? new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
                    : FakeDirectory.Json(new { practitionerId = Practitioner, displayName = PractitionerName, slug = PractitionerSlug }))
            : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
    }

    /// <summary><paramref name="roles"/> become the WS-Federation role claim authservice issues (<c>Admin</c>, <c>SuperAdmin</c>).</summary>
    public HttpClient ClientFor(string? userId, params string[] roles)
    {
        var c = CreateClient();
        if (userId != null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(userId, roles));
        return c;
    }

    string Token(string sub, string[] roles)
    {
        var claims = new Dictionary<string, object> { ["sub"] = sub };
        if (NameClaim is not null) claims["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"] = NameClaim;
        var email = Emails.TryGetValue(sub, out var e) ? e : $"{sub}@demo.invalid";
        if (email.Length > 0) claims["email"] = email;
        if (roles.Length > 0) claims["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"] = roles.Length == 1 ? roles[0] : roles;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer, Audience = Audience, Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = claims,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = "k1" }, SecurityAlgorithms.RsaSha256),
        });
    }

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
            Configure?.Invoke(s);
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
