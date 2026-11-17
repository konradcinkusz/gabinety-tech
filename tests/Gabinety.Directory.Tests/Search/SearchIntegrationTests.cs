using System.Net;
using System.Net.Http.Json;
using Gabinety.Directory.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Gabinety.Directory.Tests.Search;

/// <summary>
/// Real PostgreSQL 17 (Docker needed; CI job `integration` runs these). One container per class. For a machine without Docker, setting
/// DIRECTORY_TEST_PG to a server connection string (a role that may create databases) runs the same tests against that server in a fresh database.
/// </summary>
public sealed class PgSearchFixture : IAsyncLifetime
{
    PostgreSqlContainer? _pg;
    public string ConnectionString { get; private set; } = "";
    public PgSearchFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("DIRECTORY_TEST_PG") is { Length: > 0 } server)
        {
            var db = "directory_search_" + Guid.NewGuid().ToString("n")[..12];
            await using (var admin = new NpgsqlConnection(server))
            {
                await admin.OpenAsync();
                await using var cmd = new NpgsqlCommand($"create database {db}", admin);
                await cmd.ExecuteNonQueryAsync();
            }
            ConnectionString = new NpgsqlConnectionStringBuilder(server) { Database = db }.ConnectionString;
        }
        else
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await _pg.StartAsync();
            ConnectionString = _pg.GetConnectionString();
        }
        Factory = new PgSearchFactory(ConnectionString);
        Factory.CreateClient();
        await Factory.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));
    }

    public async Task DisposeAsync()
    {
        Factory?.Dispose();
        if (_pg is not null) await _pg.DisposeAsync();
    }
}

public sealed class PgSearchFactory(string cs) : SearchFactory
{
    protected override string Provider => "PostgreSQL";
    protected override string? ConnectionString => cs;
}

[Trait("tier", "integration")]
public class SearchIntegrationTests(PgSearchFixture pg) : IClassFixture<PgSearchFixture>
{
    SearchFactory F => pg.Factory;

    static async Task<List<string>> Column(string cs, string sql)
    {
        await using var c = new NpgsqlConnection(cs);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        await using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await r.ReadAsync()) rows.Add(r.GetString(0));
        return rows;
    }

    [Fact]
    public async Task The_migrations_apply_from_empty_the_history_equals_every_declared_migration_and_the_listing_and_tech_indexes_exist()
    {
        var declared = typeof(DirectoryDbContext).Assembly.GetTypes()
            .Select(t => (Attribute.GetCustomAttribute(t, typeof(Microsoft.EntityFrameworkCore.Migrations.MigrationAttribute)) as Microsoft.EntityFrameworkCore.Migrations.MigrationAttribute)?.Id)
            .OfType<string>().Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(declared, await Column(pg.ConnectionString, """select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId" collate "C" """));

        var listing = Assert.Single(await Column(pg.ConnectionString, "select indexdef from pg_indexes where tablename='practitioners' and indexdef ilike '%status%ranking_score%approved_at%id%'"));
        Assert.Contains("ranking_score DESC", listing);
        var gin = Assert.Single(await Column(pg.ConnectionString, "select indexdef from pg_indexes where tablename='offerings' and indexdef ilike '%gin%tech_slugs%'"));
        Assert.Contains("USING gin", gin);
    }

    [Fact] public Task Paging_30_rows_with_ties_has_no_gaps_or_duplicates() => SearchScenarios.Paging_30_rows_with_ties_visits_every_row_once_in_ranking_order(F, SearchScenarios.PostgresOrder);
    [Fact] public Task Tech_array_filter_returns_exactly_the_right_rows() => SearchScenarios.Tech_filter_returns_exactly_the_rows_whose_offering_has_that_tech(F);
    [Fact] public Task Combined_filters_hold_on_the_same_offering() => SearchScenarios.Filters_must_all_hold_on_the_same_offering(F);
    [Fact] public Task Ties_are_ordered_by_score_then_approved_at_then_id() => SearchScenarios.Ties_are_ordered_by_score_then_approved_at_then_id(F, SearchScenarios.PostgresOrder);
    [Fact] public Task Q_is_case_insensitive_and_treats_percent_literally() => SearchScenarios.Q_matches_name_or_headline_case_insensitively_and_treats_wildcards_literally(F);

    [Fact]
    public async Task Profile_and_sitemap_work_on_PostgreSQL()
    {
        await F.Reset();
        for (var i = 0; i < 3; i++) await F.Approved(null, new Spec("code-review", ["dotnet"]));
        var p = await F.Approved(null, new Spec("audyt-kodu", ["csharp"], 60, 7000));
        var c = F.Anon();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/practitioners/" + p.Slug)).StatusCode);
        var m = (await c.GetFromJsonAsync<Gabinety.Contracts.Directory.SitemapDto>("/api/v1/sitemap", Sk.J))!;
        Assert.Equal(4, m.Practitioners.Count);
        Assert.Contains(new Gabinety.Contracts.Directory.SitemapCategory("/programisci/code-review/dotnet", 3, true), m.Categories);
        Assert.Contains(new Gabinety.Contracts.Directory.SitemapCategory("/programisci/audyt-kodu/csharp", 1, false), m.Categories);
    }
}
