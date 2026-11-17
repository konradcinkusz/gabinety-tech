extern alias BookingApp;
extern alias ReputationApp;

using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.DemoSeeder;
using Gabinety.Integration.Tests.AuthContract;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ReputationClients = ReputationApp::Gabinety.Reputation.Clients;
using ReputationDb = ReputationApp::Gabinety.Reputation.Persistence;

namespace Gabinety.Integration.Tests.Seeder;

/// <summary>The Reputation service in process, in the fake profile, on its own InMemory database and a Booking it reaches in process (reviewability). No Assistant is configured, so triage answers "unavailable" and a clean review is published.</summary>
public sealed class ReputationHost : WebApplicationFactory<ReputationApp::Program>
{
    public const string Key = "demo-reputation-internal-key-for-seeder-tests";
    readonly DevIdentityHost _identity;
    readonly BookingHost _booking;

    public ReputationHost(DevIdentityHost identity, BookingHost booking) { _identity = identity; _booking = booking; }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("Internal:Key", Key);
        builder.UseSetting("Identity:Authority", DirectoryHost.Authority);
        builder.UseSetting("GABINETY_FAKE", "1");
        var dbName = "seeder-reputation-" + Guid.NewGuid().ToString("N");
        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<DbContextOptions<ReputationDb.ReputationDbContext>>();
            s.RemoveAll(typeof(Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<ReputationDb.ReputationDbContext>));
            s.RemoveAll<ReputationDb.ReputationDbContext>();
            s.AddDbContext<ReputationDb.ReputationDbContext>(o => o.UseInMemoryDatabase(dbName));
            s.RemoveAll<ReputationClients.IBookingClient>();
            s.AddSingleton<ReputationClients.IBookingClient>(new ReputationClients.BookingClient(_booking.CreateDefaultClient(), BookingHost.Key, retryDelay: TimeSpan.Zero));
            s.PostConfigure<RateLimiterOptions>(o => o.GlobalLimiter = null);
            s.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o => o.BackchannelHttpHandler = _identity.Server.CreateHandler());
        });
    }

    public async Task<T> Db<T>(Func<ReputationDb.ReputationDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ReputationDb.ReputationDbContext>());
    }

    /// <summary>Every review row as plain values, ordered by booking id, so two snapshots compare with Assert.Equal.</summary>
    public Task<List<ReviewRow>> Reviews() => Db(async db =>
        (await db.Reviews.AsNoTracking().ToListAsync()).OrderBy(r => r.BookingId)
            .Select(r => new ReviewRow(r.Id, r.BookingId, r.PractitionerId, r.ClientUserId, r.Rating, r.Text, r.Status.ToString(), r.CreatedAt)).ToList());

    public Task<List<(Guid Practitioner, int Count, int Sum, long Version)>> Summaries() => Db(async db =>
        (await db.RatingSummaries.AsNoTracking().ToListAsync()).OrderBy(s => s.PractitionerId).Select(s => (s.PractitionerId, s.Count, s.Sum, s.Version)).ToList());

    public Task<int> ModerationEvents() => Db(db => db.ModerationEvents.CountAsync());
}

public sealed record ReviewRow(Guid Id, Guid BookingId, Guid PractitionerId, string ClientUserId, int Rating, string Text, string Status, DateTimeOffset CreatedAt);

/// <summary>Request log of the Reputation client the seeder uses; a test can fail the requests it names.</summary>
public sealed class ReputationCalls : DelegatingHandler
{
    public List<(HttpMethod Method, string Path)> Requests { get; } = [];
    public Func<HttpRequestMessage, bool>? FailWhen { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        lock (Requests) Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
        if (FailWhen?.Invoke(request) == true) throw new HttpRequestException("scripted failure");
        return base.SendAsync(request, ct);
    }
}

public sealed class ReputationSeedHttp(WorldSeedHttp inner, ReputationHost reputation, ReputationCalls calls) : ISeedHttp
{
    HttpClient? _reputation;
    public HttpClient For(string service) => service == SeedService.Reputation ? _reputation ??= reputation.CreateDefaultClient(calls) : inner.For(service);
}

/// <summary>Identity, Directory, Booking and Reputation in process. The Directory and Booking steps are run by the test; so is the Reputation step.</summary>
public sealed class ReputationWorld : IAsyncDisposable
{
    public BookingWorld Booking { get; } = new();
    public ReputationHost Reputation { get; }
    public ReputationCalls Calls { get; } = new();
    public SeedContext Context { get; private set; } = null!;

    public ReputationWorld()
    {
        Reputation = new ReputationHost(Booking.Identity, Booking.Booking);
        Http = new ReputationSeedHttp(Booking.Http, Reputation, Calls);
    }

    public ISeedHttp Http { get; }
    public CapturedLog Log => Booking.Log;

    SeedOptions _options = null!;

    /// <summary>The options a run uses: Booking's plus Reputation's url and key.</summary>
    public SeedOptions WithReputation => _options ??= new SeedOptions("http://localhost", DirectoryHost.Key, DirectoryHost.Authority, SeedOptions.DevIdentityPassword)
        { BookingUrl = "http://localhost", BookingKey = BookingHost.Key, ReputationUrl = "http://localhost", ReputationKey = ReputationHost.Key };

    public SeedContext NewContext(SeedOptions? options = null) => new(options ?? WithReputation, Http, Log, TimeProvider.System);

    /// <summary>The real Directory and Booking steps (Booking with the completion stand-in running by the caller), leaving a context the Reputation step can use.</summary>
    public async Task SeedDirectoryAndBookingAsync(bool withCompletionJob = true)
    {
        _ = Booking.Booking.Services;
        _ = Reputation.Services;
        Context = NewContext();
        await new DirectorySeedStep().RunAsync(Context, CancellationToken.None);
        await BookingWorld.Step(withCompletionJob ? null : TimeSpan.Zero).RunAsync(Context, CancellationToken.None);
    }

    public static ReputationSeedStep Step() => new();

    /// <summary>What a visitor reads: the practitioner's published reviews through the public API.</summary>
    public async Task<List<JsonElement>> PublishedFor(Guid practitionerId)
    {
        using var http = Reputation.CreateClient();
        var page = await http.GetFromJsonAsync<JsonElement>($"/api/v1/practitioners/{practitionerId}/reviews?limit=20");
        return page.GetProperty("items").EnumerateArray().Select(i => i.Clone()).ToList();
    }

    public async ValueTask DisposeAsync() { Reputation.Dispose(); await Booking.DisposeAsync(); }
}
