using System.Net;
using System.Net.Http.Json;
using Gabinety.Directory.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using static Gabinety.Directory.Tests.Offerings.Kit;

namespace Gabinety.Directory.Tests.Offerings;

/// <summary>Real PostgreSQL 17 (Docker needed; CI job `integration` runs these): the migration, the column types, the GIN index, the array filter and the lock.</summary>
[Trait("tier", "integration")]
public class OfferingIntegrationTests
{
    sealed class PgFactory(string cs) : OfferingFactory
    {
        protected override string Provider => "PostgreSQL";
        protected override string? ConnectionString => cs;
    }

    static async Task<(PostgreSqlContainer Pg, PgFactory F)> Start()
    {
        var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        var f = new PgFactory(pg.GetConnectionString());
        f.CreateClient();
        await f.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));
        await WaitForSeed(f);
        return (pg, f);
    }

    static async Task<List<object?>> Scalars(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var rd = await cmd.ExecuteReaderAsync();
        var rows = new List<object?>();
        while (await rd.ReadAsync()) rows.Add(rd.GetValue(0));
        return rows;
    }

    [Fact]
    public async Task Migration_creates_offerings_with_a_text_array_a_gin_index_and_restricting_foreign_keys()
    {
        var (pg, f) = await Start();
        await using var _ = pg; using var __ = f;
        var cs = pg.GetConnectionString();

        Assert.Equal(["_text"], await Scalars(cs, "SELECT udt_name FROM information_schema.columns WHERE table_name='offerings' AND column_name='tech_slugs'"));
        Assert.Equal(["smallint"], await Scalars(cs, "SELECT data_type FROM information_schema.columns WHERE table_name='offerings' AND column_name='duration_minutes'"));
        var indexes = (await Scalars(cs, "SELECT indexdef FROM pg_indexes WHERE tablename='offerings'")).Cast<string>().ToList();
        Assert.Contains(indexes, d => d.Contains("USING gin (tech_slugs)"));
        Assert.Contains(indexes, d => d.Contains("(problem_slug, is_active)"));
        Assert.Contains(indexes, d => d.Contains("(practitioner_id)"));
        Assert.Equal(["r", "r"], (await Scalars(cs, "SELECT confdeltype::text FROM pg_constraint WHERE conrelid='offerings'::regclass AND contype='f'")).Select(x => x?.ToString()).ToList());
        Assert.Contains("T-03.04_Offerings", (await Scalars(cs, "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\"")).Select(x => x?.ToString()).Single(x => x!.Contains("T-03.04"))!);
    }

    [Fact]
    public async Task Filter_by_problem_and_tech_array_containment_returns_the_right_rows_in_creation_order()
    {
        var (pg, f) = await Start();
        await using var _ = pg; using var __ = f;
        var c = await Practitioner(f);
        await Created(c, Valid(title: "Dotnet review", problem: "code-review", techs: ["dotnet", "csharp"]));
        await Created(c, Valid(title: "Python review", problem: "code-review", techs: ["python"]));
        await Created(c, Valid(title: "Dotnet audyt", problem: "audyt-kodu", techs: ["dotnet"]));
        var third = await Created(c, Valid(title: "Dotnet drugi review", problem: "code-review", techs: ["csharp", "dotnet"]));
        await c.PostAsync($"{Url}/{third.Id}/deactivate", null);
        await Created(c, Valid(title: "Dotnet trzeci review", problem: "code-review", techs: ["dotnet"]));

        var titles = await Scalars(pg.GetConnectionString(),
            "SELECT title FROM offerings WHERE problem_slug = 'code-review' AND is_active AND tech_slugs @> ARRAY['dotnet'] ORDER BY created_at, id");
        Assert.Equal(["Dotnet review", "Dotnet trzeci review"], titles.Cast<string>());
        Assert.Equal(5, (await List(c)).Count);
    }

    [Fact]
    public async Task Six_concurrent_creates_give_exactly_five_created_and_one_too_many_offerings()
    {
        var (pg, f) = await Start();
        await using var _ = pg; using var __ = f;
        var sub = Id();
        var owner = await Practitioner(f, sub);
        var clients = Enumerable.Range(0, 6).Select(_ => f.ClientFor(sub)).ToList();

        using var gate = new ManualResetEventSlim(false);
        var calls = clients.Select((cl, i) => Task.Run(async () =>
        {
            gate.Wait();
            return await cl.PostAsJsonAsync(Url, Valid(title: $"Równoległa oferta {i}"));
        })).ToList();
        gate.Set();
        var results = await Task.WhenAll(calls);

        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        var rejected = Assert.Single(results, r => r.StatusCode != HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Equal("too_many_offerings", (await ProblemOf(rejected)).Code);
        Assert.Equal(5, (await List(owner)).Count(x => x.IsActive));
    }
}
