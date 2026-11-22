extern alias DirectoryApp;

using System.Net.Http.Json;
using System.Text.Json;
using DirectoryApp::Gabinety.Directory.Persistence;
using Gabinety.DemoSeeder;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Integration.Tests.Seeder;

/// <summary>T-08.01a: the seeder against an in-process Directory (InMemory) and the DevIdentity. The expected values are written here, not read from the seeder's catalog.</summary>
public class DemoSeederTests(SeederFixture fx) : IClassFixture<SeederFixture>
{
    // Expected by the task: ranking inputs make F (the most reviewed) first and C (new) late, not A to F.
    static readonly string[] ApprovedInRankOrder =
        ["demo-filip-syntetyczny", "demo-bartosz-testowy", "demo-dariusz-fikcyjny", "demo-anna-demowska", "demo-celina-przykladowska", "demo-ewa-wzorcowa"];

    static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    async Task<List<(string Slug, string Status)>> Rows() =>
        (await fx.Db(db => db.Practitioners.AsNoTracking().Select(p => new { p.Slug, p.Status }).ToListAsync())).Select(p => (p.Slug, p.Status.ToString())).ToList();

    [Fact]
    public void The_first_run_succeeds_and_logs_its_steps()
    {
        Assert.True(fx.FirstExitCode == 0, fx.FirstRunLog.Text);
        Assert.Contains("step directory: done", fx.FirstRunLog.Text);
    }

    [Fact]
    public async Task Counts_per_status_are_as_planned()
    {
        var rows = await Rows();
        Assert.Equal(10, rows.Count);
        Assert.Equal(6, rows.Count(r => r.Status == "Approved"));
        Assert.Equal(1, rows.Count(r => r.Status == "Submitted"));
        Assert.Equal(1, rows.Count(r => r.Status == "Draft"));
        Assert.Equal(1, rows.Count(r => r.Status == "Suspended"));
        Assert.Equal(1, rows.Count(r => r.Status == "Rejected"));
    }

    [Fact]
    public async Task Approved_practitioners_cover_at_least_three_categories_that_exist_in_the_taxonomy()
    {
        using var http = fx.Directory.CreateClient();
        var taxonomy = await http.GetFromJsonAsync<JsonElement>("/api/v1/taxonomy", J);
        var known = taxonomy.GetProperty("problems").EnumerateArray().Concat(taxonomy.GetProperty("techs").EnumerateArray()).Select(c => c.GetProperty("slug").GetString()!).ToHashSet();

        var all = await fx.Db(db => (from o in db.Offerings.AsNoTracking()
                                    join p in db.Practitioners.AsNoTracking() on o.PractitionerId equals p.Id
                                    select new { o.ProblemSlug, o.TechSlugs, p.Status }).ToListAsync());
        var offerings = all.Where(x => x.Status.ToString() == "Approved").ToList();
        Assert.True(offerings.Select(o => o.ProblemSlug).Distinct().Count() >= 3);
        Assert.All(offerings, o => { Assert.Contains(o.ProblemSlug, known); Assert.All(o.TechSlugs, t => Assert.Contains(t, known)); });
    }

    [Fact]
    public async Task At_least_three_practitioners_have_two_active_offerings_priced_in_grosz()
    {
        var perPractitioner = await fx.Db(db => db.Offerings.AsNoTracking().Where(o => o.IsActive).GroupBy(o => o.PractitionerId).Select(g => new { g.Key, N = g.Count(), Min = g.Min(o => o.PriceMinor), Max = g.Max(o => o.PriceMinor) }).ToListAsync());
        Assert.True(perPractitioner.Count(p => p.N == 2) >= 3);
        Assert.All(perPractitioner, p => Assert.InRange(p.Min, 5000, 150000));
        Assert.All(perPractitioner, p => Assert.InRange(p.Max, 5000, 150000));
    }

    [Fact]
    public async Task Every_slug_and_every_user_name_is_in_the_demo_namespace()
    {
        Assert.All(await Rows(), r => Assert.StartsWith("demo-", r.Slug));
        Assert.Equal(10, fx.FirstRun.Practitioners.Count);
        Assert.All(fx.FirstRun.Practitioners.Keys, k => Assert.StartsWith("demo-", k));
        Assert.All(fx.FirstRun.Clients.Keys, k => Assert.StartsWith("demo-", k));
        // the Directory rows belong to exactly the identity users the seeder signed in as
        var userIds = await fx.Db(db => db.Practitioners.AsNoTracking().Select(p => p.UserId).ToListAsync());
        Assert.Equal(userIds.Order(), fx.FirstRun.Practitioners.Values.Select(p => p.UserId).Order());
    }

    [Fact]
    public void A_handful_of_demo_clients_exist()
    {
        Assert.True(fx.FirstRun.Clients.Count >= 4);
        Assert.Contains("demo-client", fx.FirstRun.Clients.Keys);
    }

    [Fact]
    public async Task Public_search_lists_the_approved_only_ranked_by_the_pushed_stats_not_alphabetically()
    {
        using var http = fx.Directory.CreateClient();
        var page = await http.GetFromJsonAsync<JsonElement>("/api/v1/practitioners?limit=50", J);
        var slugs = page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("slug").GetString()!).ToList();

        Assert.Equal(ApprovedInRankOrder, slugs);
        Assert.NotEqual(slugs.Order(StringComparer.Ordinal), slugs);
    }

    [Fact]
    public async Task No_profile_shows_more_reviews_than_the_seeder_created_so_none_shows_a_rating_yet()
    {
        using var http = fx.Directory.CreateClient();
        var page = await http.GetFromJsonAsync<JsonElement>("/api/v1/practitioners?limit=50", J);
        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        // the three seeded reviews are one each on three practitioners: below the three it takes to show a rating (CONTRACTS 3.2)
        Assert.All(items, i => Assert.True(i.GetProperty("isNew").GetBoolean(), i.GetProperty("slug").GetString()));
        Assert.All(items, i => Assert.False(i.TryGetProperty("rating", out var r) && r.ValueKind != JsonValueKind.Null, i.GetProperty("slug").GetString()));
        Assert.True(DemoCatalog.Practitioners.Sum(p => p.Stats?.Reviews ?? 0) <= ReputationPlan.All.Length);
    }

    [Fact]
    public async Task A_second_run_changes_nothing_and_writes_nothing_but_the_monotonic_pushes()
    {
        var before = await Snapshot();
        var http = new TestSeedHttp(fx.Identity, fx.Directory);
        var (code, _) = await fx.RunAsync(new CapturedLog(), http);

        Assert.Equal(0, code);
        Assert.Equal(before, await Snapshot());
        var writes = http.DirectoryCalls.Requests.Where(r => r.Method != HttpMethod.Get && !r.Path.StartsWith("/internal/")).ToList();
        Assert.True(writes.Count == 0, "a second run wrote: " + string.Join(", ", writes.Select(w => $"{w.Method} {w.Path}")));
    }

    async Task<string> Snapshot() => await fx.Db(async db => JsonSerializer.Serialize(new
    {
        P = await db.Practitioners.AsNoTracking().OrderBy(p => p.Slug).ToListAsync(),
        O = await db.Offerings.AsNoTracking().OrderBy(o => o.PractitionerId).ThenBy(o => o.Title).ToListAsync(),
        E = await db.VerificationEvents.AsNoTracking().CountAsync(),
    }));

    [Fact]
    public void No_secret_or_token_reaches_the_log()
    {
        var text = fx.FirstRunLog.Text;
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain(DirectoryHost.Key, text);
        Assert.DoesNotContain(SeedOptions.DevIdentityPassword, text);
        Assert.DoesNotContain("eyJ", text);       // the start of every JWT
        Assert.DoesNotContain("Bearer", text);
        Assert.DoesNotContain("gabinety-verify-", text); // the GitHub challenge code
    }

    [Fact]
    public void The_steps_are_registered_once_each_in_ascending_order()
    {
        var steps = SeedSteps.All;
        Assert.Equal(steps.Select(s => s.Name).Distinct().Count(), steps.Count);
        Assert.Equal(steps.Select(s => s.Order).Order(), steps.Select(s => s.Order));
        Assert.Equal(("directory", 100), (steps[0].Name, steps[0].Order));
    }
}
