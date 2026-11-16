using Npgsql;
using Testcontainers.PostgreSql;

namespace Gabinety.Integration.Tests;

/// <summary>The first version of this test used `tstzrange(start_utc, end_utc + interval '15 minutes')` exactly as CONTRACTS 4.1 had it; PostgreSQL refused it (42P17, not IMMUTABLE). The UTC wall-clock form below is the amended CONTRACTS 4.1 (spec PR pending the owner). Proves the integration job has a real PostgreSQL, and that the extension the no-double-booking constraint needs exists (CONTRACTS 4.1).</summary>
[Trait("tier", "integration")]
public class PostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    [Fact]
    public async Task Exclusion_constraint_rejects_overlapping_ranges_for_one_practitioner()
    {
        await using var c = new NpgsqlConnection(_pg.GetConnectionString());
        await c.OpenAsync();
        async Task Run(string sql) { await using var cmd = new NpgsqlCommand(sql, c); await cmd.ExecuteNonQueryAsync(); }
        await Run("CREATE EXTENSION IF NOT EXISTS btree_gist");
        await Run("CREATE TABLE b (practitioner_id uuid, start_utc timestamptz, end_utc timestamptz, " +
                  "EXCLUDE USING gist (practitioner_id WITH =, tsrange(start_utc AT TIME ZONE 'UTC', (end_utc AT TIME ZONE 'UTC') + interval '15 minutes') WITH &&))");
        var p = Guid.NewGuid();
        await Run($"INSERT INTO b VALUES ('{p}', '2027-01-01 10:00+00', '2027-01-01 10:30+00')");
        await Run($"INSERT INTO b VALUES ('{Guid.NewGuid()}', '2027-01-01 10:00+00', '2027-01-01 10:30+00')"); // another practitioner: fine
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            Run($"INSERT INTO b VALUES ('{p}', '2027-01-01 10:35+00', '2027-01-01 11:05+00')")); // inside the 15 minute buffer
        Assert.Equal("23P01", ex.SqlState);
        await Run($"INSERT INTO b VALUES ('{p}', '2027-01-01 10:45+00', '2027-01-01 11:15+00')"); // after the buffer: fine
    }
}
