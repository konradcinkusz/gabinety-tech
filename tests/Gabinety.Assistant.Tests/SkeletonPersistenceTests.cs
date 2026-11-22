using Gabinety.Assistant.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Gabinety.Assistant.Tests;

public class SkeletonMigrationStateTests
{
    [Fact]
    public async Task On_in_memory_the_migration_service_is_a_no_op_and_the_state_becomes_Done()
    {
        using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("Llm:Provider", "Fake"));
        f.CreateClient();
        var state = f.Services.GetRequiredService<MigrationState>();
        await state.WaitForCompletionAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(MigrationStatus.Done, state.Status);
    }
}

[Trait("tier", "integration")]
public class SkeletonMigrationIntegrationTests
{
    static readonly string[] ContractColumns =
        ["ai_credits", "at", "feature", "id", "input_tokens", "latency_ms", "model_id", "outcome", "output_tokens", "provider_kind", "usd_estimate", "user_hash"];

    // The migrations already released, in order. Never edit or reorder these; a new migration is NOT added here: the applied history must equal the assembly's own list
    // (a missing, extra or reordered migration fails) and must start with this pinned prefix (an edited, renamed or reordered released one fails).
    static readonly string[] Shipped = ["20261116130000_T-06.01_Usage"];
    static readonly string[] Expected = [.. new AssistantDbContextFactory().CreateDbContext([]).Database.GetMigrations()];

    static void AssertHistory(IReadOnlyList<string> applied)
    {
        Assert.Equal(Expected, applied);                                  // everything in the assembly, applied once, in order
        Assert.Equal(Shipped, applied.Take(Shipped.Length).ToArray());    // the released part is untouched
    }

    static WebApplicationFactory<Program> Host(string cs) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseSetting("Llm:Provider", "Fake");
        b.UseSetting("Database:Provider", "PostgreSQL");
        b.UseSetting("ConnectionStrings:assistantdb", cs);
    });

    static async Task<List<string>> Query(string cs, string sql)
    {
        await using var c = new NpgsqlConnection(cs);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        await using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await r.ReadAsync()) rows.Add(r.GetString(0));
        return rows;
    }

    static async Task MigrateOnce(string cs)
    {
        using var host = Host(cs);
        host.CreateClient();
        var state = host.Services.GetRequiredService<MigrationState>();
        await state.WaitForCompletionAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(MigrationStatus.Done, state.Status);
    }

    [Fact]
    public async Task Migration_runs_once_a_second_start_is_a_no_op_history_equals_every_declared_migration_and_the_columns_are_the_contracts()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        var cs = pg.GetConnectionString();
        const string history = """select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId" collate "C" """;

        await MigrateOnce(cs);
        AssertHistory(await Query(cs, history));
        await MigrateOnce(cs);
        AssertHistory(await Query(cs, history));

        Assert.Equal(ContractColumns, await Query(cs,
            "select column_name from information_schema.columns where table_schema='public' and table_name='assistant_usage' order by 1"));
        var indexes = await Query(cs, "select indexdef from pg_indexes where tablename = 'assistant_usage'");
        Assert.Contains(indexes, d => d.Contains("(at)"));
        Assert.Contains(indexes, d => d.Contains("(feature, at)"));
    }
}
