using Gabinety.Directory.Persistence;
using Gabinety.Directory.Tests.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Gabinety.Directory.Features.Taxonomy;

namespace Gabinety.Directory.Tests.Taxonomy;

/// <summary>Real PostgreSQL: the migration creates the tables, the seeder fills them once, a second start changes nothing.</summary>
[Trait("tier", "integration")]
public class TaxonomyIntegrationTests
{
    [Fact]
    public async Task Host_start_seeds_20_categories_and_one_meta_row_and_a_second_start_changes_nothing()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();

        var first = await StartAndRead(pg.GetConnectionString());
        Assert.Equal(20, first.Count);
        Assert.Equal(1, first.MetaRows);
        Assert.Equal(Expected.Version, first.Version);

        var second = await StartAndRead(pg.GetConnectionString());
        Assert.Equal(first, second);
    }

    static async Task<Result> StartAndRead(string cs)
    {
        using var f = new PgFactory(cs);
        f.CreateClient(); // starts the host
        await f.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));
        await f.Services.GetRequiredService<TaxonomySeeder>().Completed.WaitAsync(TimeSpan.FromSeconds(60));
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();
        var rows = await db.Categories.AsNoTracking().OrderBy(c => c.Slug).Select(c => $"{c.Slug}|{c.Kind}|{c.NamePl}|{c.Sort}|{c.IsActive}").ToListAsync();
        return new(rows.Count, await db.TaxonomyMeta.CountAsync(), await db.TaxonomyMeta.Select(m => m.Version).SingleAsync(), string.Join("\n", rows));
    }

    sealed record Result(int Count, int MetaRows, string Version, string Rows);

    sealed class PgFactory(string cs) : ProfileFactory
    {
        protected override string Provider => "PostgreSQL";
        protected override string? ConnectionString => cs;
    }
}
