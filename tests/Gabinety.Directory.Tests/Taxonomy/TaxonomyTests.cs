using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Taxonomy;
using Gabinety.Directory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gabinety.Directory.Tests.Taxonomy;

[Collection("taxonomy")]
public class TaxonomyTests(TaxonomyFactory f)
{
    static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    async Task<TaxonomyDto> Get()
    {
        var r = await f.CreateClient().GetAsync("/api/v1/taxonomy");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<TaxonomyDto>(J))!;
    }

    [Fact]
    public async Task Endpoint_returns_the_8_problems_and_12_techs_with_polish_names_in_order()
    {
        await f.SeedAsync();
        var t = await Get();
        Assert.Equal(Expected.Version, t.Version);
        Assert.Equal(Expected.Problems, t.Problems.Select(c => (c.Slug, c.NamePl)));
        Assert.Equal(Expected.Techs, t.Techs.Select(c => (c.Slug, c.NamePl)));
        Assert.Contains(t.Problems, c => c.NamePl == "Rozwiązanie awarii"); // the diacritics survive the round trip
    }

    [Fact]
    public async Task Endpoint_is_anonymous_and_camel_case()
    {
        await f.SeedAsync();
        var json = await f.CreateClient().GetStringAsync("/api/v1/taxonomy");
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("problems", out var problems));
        Assert.True(problems[0].TryGetProperty("namePl", out _));
        Assert.True(doc.RootElement.TryGetProperty("version", out _));
    }

    [Fact]
    public async Task Seeding_twice_changes_nothing()
    {
        await f.SeedAsync();
        var before = await Snapshot();
        var changes = await f.SeedAsync();
        Assert.Equal(0, changes);
        Assert.Equal(20, before.Count);
        Assert.Equal(before, await Snapshot());
        Assert.Equal(1, await f.WithDb(db => db.TaxonomyMeta.CountAsync()));
    }

    [Fact]
    public async Task Removing_a_slug_deactivates_it_and_keeps_the_row()
    {
        await f.SeedAsync();
        await f.SeedAsync(Expected.Set("2026-11-v2", "code-review"));

        var row = await f.WithDb(db => db.Categories.SingleAsync(c => c.Slug == "code-review"));
        Assert.False(row.IsActive);
        Assert.Equal(20, await f.WithDb(db => db.Categories.CountAsync()));
        var t = await Get();
        Assert.DoesNotContain(t.Problems, c => c.Slug == "code-review");
        Assert.Equal(7, t.Problems.Count);
        Assert.Equal("2026-11-v2", t.Version);
        Assert.Equal("2026-11-v2", await f.WithDb(db => db.TaxonomyMeta.Select(m => m.Version).SingleAsync()));
    }

    [Fact]
    public async Task A_slug_that_returns_to_the_seed_is_active_again()
    {
        await f.SeedAsync(Expected.Set("2026-11-v2", "code-review"));
        await f.SeedAsync();
        Assert.True((await f.WithDb(db => db.Categories.SingleAsync(c => c.Slug == "code-review"))).IsActive);
        Assert.Contains((await Get()).Problems, c => c.Slug == "code-review");
    }

    [Fact]
    public async Task Adding_a_slug_inserts_it_and_a_changed_name_is_updated()
    {
        await f.SeedAsync();
        var set = Expected.Set("2026-11-v3");
        set = set with { Categories = [.. set.Categories.Select(c => c.Slug == "azure" ? c with { NamePl = "Microsoft Azure" } : c),
                                       new SeedCategory("demo-nowa-technologia", "tech", "Nowa technologia", 99)] };
        await f.SeedAsync(set);

        var t = await Get();
        Assert.Equal(13, t.Techs.Count);
        Assert.Equal("demo-nowa-technologia", t.Techs[^1].Slug);
        Assert.Equal("Microsoft Azure", t.Techs.Single(c => c.Slug == "azure").NamePl);
        Assert.Equal(21, await f.WithDb(db => db.Categories.CountAsync()));
        await f.SeedAsync(clean: false); // back to the real set: the extra row is deactivated, not deleted
        Assert.False((await f.WithDb(db => db.Categories.SingleAsync(c => c.Slug == "demo-nowa-technologia"))).IsActive);
    }

    [Fact]
    public async Task A_failed_migration_makes_the_seeder_log_one_warning_and_do_nothing()
    {
        var state = new MigrationState();
        state.Set(MigrationStatus.Failed);
        var log = new ListLogger<TaxonomySeeder>();
        var seeder = new TaxonomySeeder(new ThrowingServices(), new SwappableSource(), state, log);
        await seeder.StartAsync(CancellationToken.None);
        await seeder.Completed.WaitAsync(TimeSpan.FromSeconds(10));
        await seeder.StopAsync(CancellationToken.None);
        Assert.Equal([LogLevel.Warning], log.Levels);
    }

    [Fact]
    public async Task Seed_file_and_migration_contain_no_seed_rows()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "GabinetyTech.sln"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var migrations = Path.Combine(dir!, "src", "Gabinety.Directory", "Persistence", "Migrations");
        var files = System.IO.Directory.GetFiles(migrations, "T-03.03*.cs").Append(Path.Combine(migrations, "DirectoryDbContextModelSnapshot.cs")).ToList();
        Assert.True(files.Count >= 3, "migration, designer and snapshot");
        foreach (var file in files)
        {
            var text = await File.ReadAllTextAsync(file);
            Assert.DoesNotContain("InsertData", text);
            Assert.DoesNotContain(".HasData(", text);
            foreach (var (slug, name) in Expected.Problems.Concat(Expected.Techs))
            {
                Assert.DoesNotContain($"\"{slug}\"", text);
                Assert.DoesNotContain($"\"{name}\"", text);
            }
        }
    }

    Task<List<string>> Snapshot() => f.WithDb(async db =>
        (await db.Categories.AsNoTracking().OrderBy(c => c.Slug).ToListAsync()).Select(c => $"{c.Slug}|{c.Kind}|{c.NamePl}|{c.Sort}|{c.IsActive}").ToList());

    sealed class ThrowingServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => throw new InvalidOperationException("the seeder must not touch the database after a failed migration");
    }
}
