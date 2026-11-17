using Gabinety.Booking.Notifications;
using Gabinety.Booking.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace Gabinety.Booking.Tests;

/// <summary>Real PostgreSQL (CI `integration` job; Testcontainers needs Docker).</summary>
[Trait("tier", "integration")]
public class NotificationIntegrationTests
{
    static BookingDbContext Db(string cs) => new(new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(cs).Options);

    static async Task<PostgreSqlContainer> Pg()
    {
        var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        return pg;
    }

    static CreateHost Host(string cs, RecordingEmailSender sender, FakeContacts contacts)
    {
        var h = new CreateHost();
        h.Settings["Database:Provider"] = "PostgreSQL";
        h.Settings["ConnectionStrings:bookingdb"] = cs;
        h.Configure = s =>
        {
            s.RemoveAll<IEmailSender>();
            s.AddSingleton<IEmailSender>(sender);
            s.RemoveAll<IContactResolver>();
            s.AddSingleton<IContactResolver>(contacts);
        };
        return h;
    }

    [Fact]
    public async Task The_unique_index_rejects_a_duplicate_booking_kind_and_user()
    {
        await using var pg = await Pg();
        using var host = Host(pg.GetConnectionString(), new(), new());
        host.CreateClient();
        await host.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));
        var booking = Guid.NewGuid();
        var due = DateTimeOffset.UtcNow;

        await using var db = Db(pg.GetConnectionString());
        db.Notifications.Add(new() { Id = Guid.NewGuid(), BookingId = booking, Kind = NotificationKind.Reminder1h, ToUserId = "demo-client", DueAt = due });
        await db.SaveChangesAsync();

        await using var db2 = Db(pg.GetConnectionString());
        db2.Notifications.Add(new() { Id = Guid.NewGuid(), BookingId = booking, Kind = NotificationKind.Reminder1h, ToUserId = "demo-client", DueAt = due });
        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());

        db2.ChangeTracker.Clear();
        db2.Notifications.Add(new() { Id = Guid.NewGuid(), BookingId = booking, Kind = NotificationKind.Reminder1h, ToUserId = "demo-practitioner", DueAt = due }); // another user: fine
        await db2.SaveChangesAsync();
    }

    [Fact]
    public async Task Two_dispatchers_at_once_on_PostgreSQL_send_each_notification_exactly_once()
    {
        await using var pg = await Pg();
        var cs = pg.GetConnectionString();
        var sender = new RecordingEmailSender { Yield = true };
        var contacts = new FakeContacts { Default = "demo-person@demo.invalid" };
        using var a = Host(cs, sender, contacts);
        using var b = Host(cs, sender, contacts);
        a.CreateClient();
        b.CreateClient();
        await a.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));

        var booking = Guid.NewGuid();
        var now = a.Clock.GetUtcNow();
        var ids = new List<Guid>();
        await using (var seed = Db(cs))
        {
            for (var i = 0; i < 40; i++)
            {
                var n = new Notification { Id = Guid.NewGuid(), BookingId = booking, Kind = NotificationKind.Reminder24h, ToUserId = $"demo-user-{i:D3}", DueAt = now };
                ids.Add(n.Id);
                seed.Notifications.Add(n);
            }
            seed.Bookings.Add(new() { Id = booking, ClientUserId = "demo-client", PractitionerUserId = "demo-practitioner", OfferingTitle = "demo-offering", StartUtc = now.AddDays(2), EndUtc = now.AddDays(2).AddHours(1), IdempotencyKey = Guid.NewGuid(), CreatedAt = now, Status = Gabinety.Billing.BookingStatus.Confirmed });
            await seed.SaveChangesAsync();
        }

        // Whatever the interleaving, a notification is claimed by one dispatcher only and the loser never throws.
        var da = a.Services.GetRequiredService<NotificationDispatcher>();
        var db = b.Services.GetRequiredService<NotificationDispatcher>();
        var counts = await Task.WhenAll(da.RunOnceAsync(default), db.RunOnceAsync(default), da.RunOnceAsync(default), db.RunOnceAsync(default));

        Assert.Equal(40, counts.Sum());
        Assert.Equal(40, sender.Sent.Select(m => m.ToUserId).Distinct().Count());
        Assert.Equal(40, sender.Sent.Count);
        await using var check = Db(cs);
        var rows = await check.Notifications.AsNoTracking().Where(n => ids.Contains(n.Id)).ToListAsync();
        Assert.All(rows, r => { Assert.NotNull(r.SentAt); Assert.Equal(1, r.Attempts); });
    }
}
