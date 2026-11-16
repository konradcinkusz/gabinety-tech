using Gabinety.Booking.Features.Availability;
using Gabinety.Booking.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Gabinety.Booking.Tests;

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
    public void The_migrations_match_the_model_so_MigrateAsync_will_not_refuse_to_run()
    {
        using var db = new BookingDbContextFactory().CreateDbContext([]);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_model_maps_the_two_tables_with_their_indexes()
    {
        using var db = new BookingDbContextFactory().CreateDbContext([]);
        var rules = db.Model.FindEntityType(typeof(AvailabilityRule))!;
        var days = db.Model.FindEntityType(typeof(AvailabilityException))!;
        Assert.Equal("availability_rules", rules.GetTableName());
        Assert.Equal("availability_exceptions", days.GetTableName());
        Assert.Contains(rules.GetIndexes(), i => !i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["PractitionerId", "IsoWeekday"]));
        Assert.Contains(days.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["PractitionerId", "DateLocal"]));
    }
}

[Trait("tier", "integration")]
public class SkeletonMigrationIntegrationTests
{
    static readonly string[] Expected = ["20261116000000_T-04.03_Baseline", "20261116110000_T-04.03_Availability", "20261116130000_T-04.04_Bookings"];

    static WebApplicationFactory<Program> Host(string cs) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseSetting("Database:Provider", "PostgreSQL");
        b.UseSetting("ConnectionStrings:bookingdb", cs);
    });

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

    static async Task Start(string cs)
    {
        using var host = Host(cs);
        host.CreateClient();
        var state = host.Services.GetRequiredService<MigrationState>();
        await state.WaitForCompletionAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(MigrationStatus.Done, state.Status);
    }

    static BookingDbContext Db(string cs) =>
        new(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(cs).Options);

    static async Task<PostgreSqlContainer> Pg()
    {
        var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        return pg;
    }

    [Fact]
    public async Task Migration_runs_once_and_a_second_start_is_a_no_op()
    {
        await using var pg = await Pg();
        var cs = pg.GetConnectionString();
        const string history = """select "MigrationId" from "__EFMigrationsHistory" order by 1""";

        await Start(cs);
        Assert.Equal(Expected, await Column(cs, history));
        await Start(cs);
        Assert.Equal(Expected, await Column(cs, history));
        Assert.Equal(["availability_exceptions", "availability_rules"],
            await Column(cs, "select table_name from information_schema.tables where table_schema='public' and table_name like 'availability%' order by 1"));
    }

    [Fact]
    public async Task The_unique_index_rejects_a_duplicate_exception_date()
    {
        await using var pg = await Pg();
        var cs = pg.GetConnectionString();
        await Start(cs);
        var practitioner = Guid.NewGuid();
        await using var db = Db(cs);
        db.Exceptions.Add(new() { Id = Guid.NewGuid(), PractitionerId = practitioner, DateLocal = new(2027, 1, 1) });
        await db.SaveChangesAsync();

        await using var db2 = Db(cs);
        db2.Exceptions.Add(new() { Id = Guid.NewGuid(), PractitionerId = practitioner, DateLocal = new(2027, 1, 1) });
        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());

        db2.ChangeTracker.Clear();
        db2.Exceptions.Add(new() { Id = Guid.NewGuid(), PractitionerId = Guid.NewGuid(), DateLocal = new(2027, 1, 1) }); // another practitioner: fine
        await db2.SaveChangesAsync();
    }

    [Fact]
    public async Task Replace_as_a_whole_is_transactional_against_real_PostgreSQL()
    {
        await using var pg = await Pg();
        var cs = pg.GetConnectionString();
        await Start(cs);
        var practitioner = Guid.NewGuid();
        var oldRule = new AvailabilityRule { Id = Guid.NewGuid(), PractitionerId = practitioner, IsoWeekday = 1, StartLocal = new(9, 0), EndLocal = new(10, 0) };
        var oldDay = new AvailabilityException { Id = Guid.NewGuid(), PractitionerId = practitioner, DateLocal = new(2027, 1, 1) };
        await using (var seed = Db(cs))
            await new AvailabilityStore(seed).ReplaceAsync(practitioner, [oldRule], [oldDay], default);

        // Second exception row repeats a date: the unique index fires after the rule inserts and the old rows' deletes were queued.
        var d = new DateOnly(2027, 2, 2);
        await using (var bad = Db(cs))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => new AvailabilityStore(bad).ReplaceAsync(practitioner,
                [new() { Id = Guid.NewGuid(), PractitionerId = practitioner, IsoWeekday = 5, StartLocal = new(14, 0), EndLocal = new(15, 0) }],
                [new() { Id = Guid.NewGuid(), PractitionerId = practitioner, DateLocal = d }, new() { Id = Guid.NewGuid(), PractitionerId = practitioner, DateLocal = d }],
                default));
        }

        await using var check = Db(cs);
        Assert.Equal([oldRule.Id], await check.Rules.Where(r => r.PractitionerId == practitioner).Select(r => r.Id).ToListAsync());
        Assert.Equal([oldDay.Id], await check.Exceptions.Where(r => r.PractitionerId == practitioner).Select(r => r.Id).ToListAsync());
    }
}
