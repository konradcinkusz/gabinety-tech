extern alias DirectoryApp;

using DirectoryApp::Gabinety.Directory.Features.Verification;
using DirectoryApp::Gabinety.Directory.Features.Profiles;
using DirectoryApp::Gabinety.Directory.Persistence;
using Gabinety.DemoSeeder;
using Gabinety.Integration.Tests.AuthContract;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Integration.Tests.Seeder;

/// <summary>Stands in for api.github.com: a profile "has" the code the practitioner was asked to publish, which is what a real bio would show.</summary>
public sealed class EchoGithub(IServiceProvider services) : IGithubProfileLookup
{
    public async Task<GithubProfile> GetBioAsync(string login, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();
        var code = await db.Practitioners.AsNoTracking().Where(p => p.GithubChallengeLogin == login).Select(p => p.GithubChallengeCode).FirstOrDefaultAsync(ct);
        return code is null ? GithubProfile.NotFound : GithubProfile.Found("demo bio " + code);
    }
}

/// <summary>Suspending a practitioner asks Booking to cancel their bookings; this host has no Booking and the demo Suspended practitioner has none, so the call is accepted.</summary>
public sealed class NoBookings : DirectoryApp::Gabinety.Directory.Clients.IBookingClient
{
    public Task CancelFutureBookingsAsync(Guid practitionerId, CancellationToken ct) => Task.CompletedTask;
}

public sealed class DirectoryHost : WebApplicationFactory<DirectoryApp::Program>
{
    public const string Key = "demo-internal-key-for-seeder-tests";
    public const string Authority = "http://identity.demo.test";
    readonly DevIdentityHost _identity;

    public DirectoryHost(DevIdentityHost identity) => _identity = identity;

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("Internal:Key", Key);
        builder.UseSetting("Identity:Authority", Authority);
        var dbName = "seeder-" + Guid.NewGuid().ToString("N"); // the in-memory database is shared by name in one process: this host gets its own
        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<DbContextOptions<DirectoryDbContext>>();
            s.RemoveAll<DbContextOptions>();
            s.AddDbContext<DirectoryDbContext>(o => o.UseInMemoryDatabase(dbName));
            s.AddSingleton<IGithubProfileLookup>(sp => new EchoGithub(sp));
            s.AddSingleton<DirectoryApp::Gabinety.Directory.Clients.IBookingClient, NoBookings>();
            s.PostConfigure<RateLimiterOptions>(o => o.GlobalLimiter = null); // a whole seeding run and its second run exceed 60 requests a minute from one address
            s.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o => o.BackchannelHttpHandler = _identity.Server.CreateHandler());
            s.AddHttpClient(nameof(IEmailVerifiedLookup)).ConfigurePrimaryHttpMessageHandler(() => _identity.Server.CreateHandler());
        });
    }
}

public sealed class CountingHandler : DelegatingHandler
{
    public List<(HttpMethod Method, string Path)> Requests { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        lock (Requests) Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
        return base.SendAsync(request, ct);
    }
}

public sealed class CapturedLog : ISeedLog
{
    readonly List<string> _lines = [];
    public void Info(string line) { lock (_lines) _lines.Add(line); }
    public string Text { get { lock (_lines) return string.Join('\n', _lines); } }
}

public sealed class TestSeedHttp(DevIdentityHost identity, DirectoryHost directory) : ISeedHttp
{
    public CountingHandler DirectoryCalls { get; } = new();
    HttpClient? _directory;
    public HttpClient For(string service) => service switch
    {
        SeedService.Identity => identity.CreateClient(),
        SeedService.Directory => _directory ??= directory.CreateDefaultClient(DirectoryCalls),
        _ => throw new InvalidOperationException(service),
    };
}

/// <summary>One identity and one Directory, in process, seeded once by the first run. The tests then read the result and run the seeder again.</summary>
public sealed class SeederFixture : IAsyncLifetime
{
    public DevIdentityHost Identity { get; } = new();
    public DirectoryHost Directory { get; private set; } = null!;
    public SeedOptions Options { get; } = new("http://localhost", DirectoryHost.Key, DirectoryHost.Authority, SeedOptions.DevIdentityPassword);
    public CapturedLog FirstRunLog { get; } = new();
    public SeedContext FirstRun { get; private set; } = null!;
    public int FirstExitCode { get; private set; } = -1;

    public async Task InitializeAsync()
    {
        Directory = new DirectoryHost(Identity);
        (FirstExitCode, FirstRun) = await RunAsync(FirstRunLog);
    }

    public async Task<(int ExitCode, SeedContext Context)> RunAsync(ISeedLog log, TestSeedHttp? http = null)
    {
        http ??= new TestSeedHttp(Identity, Directory);
        var context = new SeedContext(Options, http, log, TimeProvider.System);
        var code = await SeederApp.RunAsync(Options, http, log, TimeProvider.System, CancellationToken.None, context);
        return (code, context);
    }

    public async Task<T> Db<T>(Func<DirectoryDbContext, Task<T>> work)
    {
        using var scope = Directory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<DirectoryDbContext>());
    }

    public Task DisposeAsync() { Directory.Dispose(); Identity.Dispose(); return Task.CompletedTask; }
}
