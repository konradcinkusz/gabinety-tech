using Gabinety.Reputation.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Gabinety.Reputation.Tests;

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

    [Fact]
    public async Task Entities_round_trip_on_in_memory_including_hold_reasons()
    {
        using var f = new WebApplicationFactory<Program>();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReputationDbContext>();
        var id = Guid.NewGuid();
        db.Reviews.Add(new Review
        {
            Id = id, BookingId = Guid.NewGuid(), PractitionerId = Guid.NewGuid(), ClientUserId = "demo-user-1", Rating = 4,
            Text = "Synthetyczna opinia o długości powyżej dwudziestu znaków.", Status = Gabinety.Contracts.Reputation.ReviewStatus.Held,
            HoldReasons = ["contains_link", "repetition"], CreatedAt = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(["contains_link", "repetition"], (await db.Reviews.FindAsync(id))!.HoldReasons);
    }
}

[Trait("tier", "integration")]
public class SkeletonMigrationIntegrationTests
{
    // The migrations already released, in order. Never edit or reorder these; a new migration is NOT added here: the applied history must equal the assembly's own list
    // (a missing, extra or reordered migration fails) and must start with this pinned prefix (an edited, renamed or reordered released one fails).
    static readonly string[] Shipped = ["20261116100000_T-05.01_Entities"];
    static readonly string[] Expected = [.. new ReputationDbContextFactory().CreateDbContext([]).Database.GetMigrations()];

    static void AssertHistory(IReadOnlyList<string> applied)
    {
        Assert.Equal(Expected, applied);                                  // everything in the assembly, applied once, in order
        Assert.Equal(Shipped, applied.Take(Shipped.Length).ToArray());    // the released part is untouched
    }

    static WebApplicationFactory<Program> Host(string cs) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseSetting("Database:Provider", "PostgreSQL");
        b.UseSetting("ConnectionStrings:reputationdb", cs);
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
    public async Task Migration_runs_once_a_second_start_is_a_no_op_and_the_tables_and_unique_index_exist()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        var cs = pg.GetConnectionString();
        const string history = """select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId" collate "C" """;

        await MigrateOnce(cs);
        AssertHistory(await Query(cs, history));
        await MigrateOnce(cs);
        AssertHistory(await Query(cs, history));

        Assert.Equal(["moderation_events", "rating_summaries", "reviews"], await Query(cs,
            "select table_name from information_schema.tables where table_schema = 'public' and table_name in ('reviews','rating_summaries','moderation_events') order by 1"));
        Assert.Equal(["text[]"], (await Query(cs, "select udt_name::text from information_schema.columns where table_name='reviews' and column_name='hold_reasons'")).Select(x => x == "_text" ? "text[]" : x));
        var unique = await Query(cs, "select indexdef from pg_indexes where tablename = 'reviews' and indexdef like 'CREATE UNIQUE INDEX%'");
        Assert.Contains(unique, d => d.Contains("(booking_id)"));
        Assert.Contains(await Query(cs, "select indexdef from pg_indexes where tablename = 'reviews'"),
            d => d.Contains("practitioner_id, status, published_at DESC, id"));
    }
}
