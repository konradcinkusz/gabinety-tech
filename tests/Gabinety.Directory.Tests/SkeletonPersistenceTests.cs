using Gabinety.Directory.Features.Health;
using Gabinety.Directory.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Gabinety.Directory.Tests;

sealed class StubHandler(Func<Uri, (int Status, string Body)> respond) : HttpMessageHandler
{
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        var (s, b) = respond(r.RequestUri!);
        return Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)s) { Content = new StringContent(b) });
    }
}

sealed class ManualClock : TimeProvider
{
    DateTimeOffset _now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan t) => _now += t;
}

public class SkeletonMigrationStateTests
{
    [Fact]
    public async Task On_in_memory_the_migration_service_is_a_no_op_and_the_state_becomes_Done()
    {
        using var f = new WebApplicationFactory<Program>();
        f.CreateClient();
        var state = f.Services.GetRequiredService<MigrationState>();
        await state.WaitForCompletionAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(MigrationStatus.Done, state.Status);
    }
}

public class SkeletonIdentityCheckTests
{
    static IdentityHealthCheck Make(StubHandler h, ManualClock clock, string? authority = "http://idp.demo.invalid") =>
        new(new HttpClient(h), new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Identity:Authority"] = authority }).Build(), new JwksCache(), clock);

    static (int, string) Good(Uri u) => u.AbsolutePath.EndsWith("openid-configuration")
        ? (200, """{"jwks_uri":"http://idp.demo.invalid/jwks"}""")
        : (200, """{"keys":[{"kty":"RSA","kid":"k1","n":"AQAB","e":"AQAB"}]}""");

    [Fact]
    public async Task Healthy_when_the_jwks_has_keys() =>
        Assert.Equal(HealthStatus.Healthy, (await Make(new StubHandler(Good), new ManualClock()).CheckHealthAsync(new())).Status);

    [Fact]
    public async Task Degraded_when_the_jwks_is_empty() =>
        Assert.Equal(HealthStatus.Degraded, (await Make(new StubHandler(u =>
            u.AbsolutePath.EndsWith("openid-configuration") ? (200, """{"jwks_uri":"http://idp.demo.invalid/jwks"}""") : (200, """{"keys":[]}""")),
            new ManualClock()).CheckHealthAsync(new())).Status);

    [Fact]
    public async Task Degraded_on_http_error_and_failure_is_not_cached_beyond_30_seconds()
    {
        var up = false;
        var h = new StubHandler(u => up ? Good(u) : (500, ""));
        var clock = new ManualClock();
        var check = Make(h, clock);
        Assert.Equal(HealthStatus.Degraded, (await check.CheckHealthAsync(new())).Status);
        var calls = h.Calls;
        Assert.Equal(HealthStatus.Degraded, (await check.CheckHealthAsync(new())).Status);
        Assert.Equal(calls, h.Calls); // within 30 s the failure is served from the cache
        up = true;
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new())).Status);
    }
}

[Trait("tier", "integration")]
public class SkeletonMigrationIntegrationTests
{
    static WebApplicationFactory<Program> Host(string cs) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseSetting("Database:Provider", "PostgreSQL");
        b.UseSetting("ConnectionStrings:directorydb", cs);
    });

    static async Task<List<string>> History(string cs)
    {
        await using var c = new NpgsqlConnection(cs);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("""select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId" collate "C" """, c);
        await using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await r.ReadAsync()) rows.Add(r.GetString(0));
        return rows;
    }

    // The migrations already released, in order. Never edit or reorder these; a new migration is NOT added here: the applied history must equal the assembly's own list
    // (a missing, extra or reordered migration fails) and must start with this pinned prefix (an edited, renamed or reordered released one fails).
    static readonly string[] Shipped = ["20261116000000_T-03.01_Baseline", "20261116100000_T-03.02_Practitioners", "20261116120000_T-03.03_Taxonomy", "20261116140000_T-03.04_Offerings", "20261116150000_T-03.05_Verification", "20261122100000_T-03.09_ContactEmail"];
    static readonly string[] Expected = [.. new DirectoryDbContextFactory().CreateDbContext([]).Database.GetMigrations()];

    static void AssertHistory(IReadOnlyList<string> applied)
    {
        Assert.Equal(Expected, applied);                                  // everything in the assembly, applied once, in order
        Assert.Equal(Shipped, applied.Take(Shipped.Length).ToArray());    // the released part is untouched
    }

    [Fact]
    public async Task Migration_runs_once_and_a_second_start_is_a_no_op()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        var cs = pg.GetConnectionString();

        using (var first = Host(cs))
        {
            first.CreateClient();
            var state = first.Services.GetRequiredService<MigrationState>();
            await state.WaitForCompletionAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(MigrationStatus.Done, state.Status);
        }
        AssertHistory(await History(cs));

        using (var second = Host(cs))
        {
            second.CreateClient();
            var state = second.Services.GetRequiredService<MigrationState>();
            await state.WaitForCompletionAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(MigrationStatus.Done, state.Status);
        }
        AssertHistory(await History(cs));
    }
}
