using System.Net;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>The erasure on real PostgreSQL 17: the unique index (client_user_id, idempotency_key) survives the erasure of two users who used the same key.</summary>
[Trait("tier", "integration")]
public class ErasureIntegrationTests : IAsyncLifetime
{
    PostgreSqlContainer pg = null!;
    string cs = "";
    CreateHost host = null!;

    public async Task InitializeAsync()
    {
        pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        cs = pg.GetConnectionString();
        host = ErasureApiTests.NewHost();
        host.Settings["Database:Provider"] = "PostgreSQL";
        host.Settings["ConnectionStrings:bookingdb"] = cs;
        host.CreateClient();
        await host.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));
    }

    public async Task DisposeAsync() { await host.DisposeAsync(); await pg.DisposeAsync(); }

    BookingDbContext Db() => new(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(cs).Options);

    [Fact]
    public async Task Two_users_with_the_same_idempotency_key_both_survive_their_erasure_with_their_own_row()
    {
        var shared = Guid.NewGuid(); // the key is only unique per client: two clients may well send the same one
        string a = ErasureApiTests.NewUser("a"), b = ErasureApiTests.NewUser("b"), practitionerUser = ErasureApiTests.NewUser("p");
        var ba = await ErasureApiTests.SeedBooking(host, a, practitionerUser, key: shared, withMoney: false);
        var bb = await ErasureApiTests.SeedBooking(host, b, practitionerUser, key: shared, withMoney: false, day: 2);
        await host.Seed(db => db.IdempotencyRecords.AddRange(
            new IdempotencyRecord { ClientUserId = a, IdempotencyKey = shared, RequestHash = "ha", ResponseStatus = 201, ResponseBody = "{}", CreatedAt = host.Clock.GetUtcNow() },
            new IdempotencyRecord { ClientUserId = b, IdempotencyKey = shared, RequestHash = "hb", ResponseStatus = 201, ResponseBody = "{}", CreatedAt = host.Clock.GetUtcNow() }));

        Assert.Equal(HttpStatusCode.NoContent, (await ErasureApiTests.Erase(host, a)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ErasureApiTests.Erase(host, b)).StatusCode);

        await using var db = Db();
        var rows = await db.Bookings.AsNoTracking().Where(x => x.Id == ba || x.Id == bb).OrderBy(x => x.ClientUserId).ToListAsync();
        Assert.Equal(new[] { ErasureApiTests.Token(a), ErasureApiTests.Token(b) }.Order().ToArray(), rows.Select(x => x.ClientUserId).ToArray());
        Assert.All(rows, x => { Assert.Equal(shared, x.IdempotencyKey); Assert.Equal("[usunięto]", x.BriefText); Assert.Equal(ErasureApiTests.Token(x.ClientUserId == ErasureApiTests.Token(a) ? a : b), x.ClientUserId); });
        var records = await db.IdempotencyRecords.AsNoTracking().Where(r => r.IdempotencyKey == shared).ToListAsync();
        Assert.Equal(new[] { ErasureApiTests.Token(a), ErasureApiTests.Token(b) }.Order().ToArray(), records.Select(r => r.ClientUserId).Order().ToArray());
        Assert.Equal(0, await db.Bookings.CountAsync(x => x.ClientUserId == a || x.ClientUserId == b || x.PractitionerUserId == a || x.PractitionerUserId == b));

        // the index still guards: a second row of the same (token, key) is refused
        db.Bookings.Add(new Gabinety.Booking.Features.Bookings.Entities.Booking
        {
            Id = Guid.NewGuid(), ClientUserId = ErasureApiTests.Token(a), PractitionerId = Guid.NewGuid(), PractitionerUserId = practitionerUser, OfferingId = host.Offering, OfferingTitle = "demo-offering",
            DurationMinutes = 60, PriceMinor = 1, StartUtc = host.Clock.GetUtcNow().AddDays(5), EndUtc = host.Clock.GetUtcNow().AddDays(5).AddHours(1), Status = BookingStatus.Confirmed,
            BriefText = "x", IdempotencyKey = shared, CreatedAt = host.Clock.GetUtcNow(),
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_user_with_several_batches_of_bookings_is_erased_on_PostgreSQL()
    {
        string u = ErasureApiTests.NewUser("many"), p = ErasureApiTests.NewUser("p");
        await host.Seed(db =>
        {
            for (var i = 0; i < 1203; i++)
                db.Bookings.Add(new Gabinety.Booking.Features.Bookings.Entities.Booking
                {
                    Id = Guid.NewGuid(), ClientUserId = u, PractitionerId = Guid.NewGuid(), PractitionerUserId = p, OfferingId = host.Offering, OfferingTitle = "demo-offering",
                    DurationMinutes = 60, PriceMinor = 1, StartUtc = host.Clock.GetUtcNow().AddDays(1), EndUtc = host.Clock.GetUtcNow().AddDays(1).AddHours(1),
                    Status = BookingStatus.Completed, BriefText = "demo-brief", IdempotencyKey = Guid.NewGuid(), CreatedAt = host.Clock.GetUtcNow(),
                });
        });
        Assert.Equal(HttpStatusCode.NoContent, (await ErasureApiTests.Erase(host, u)).StatusCode);
        await using var db = Db();
        Assert.Equal(0, await db.Bookings.CountAsync(x => x.ClientUserId == u));
        Assert.Equal(1203, await db.Bookings.CountAsync(x => x.ClientUserId == ErasureApiTests.Token(u) && x.BriefText == "[usunięto]"));
    }
}
