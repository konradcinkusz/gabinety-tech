using Gabinety.Billing;
using Gabinety.Booking.Features.Slots;
using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gabinety.Booking.Tests;

public class BookingSlotGuardTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    static PostgresException Pg(string sqlState, string? constraint) =>
        new("message", "ERROR", "ERROR", sqlState, constraintName: constraint);

    static BookingDbContext InMemory() =>
        new(new DbContextOptionsBuilder<BookingDbContext>().UseInMemoryDatabase("guard-" + Guid.NewGuid()).Options);

    [Fact]
    public void The_exclusion_violation_of_bookings_no_overlap_is_a_taken_slot() =>
        Assert.True(BookingSlotGuard.IsSlotTaken(new DbUpdateException("x", Pg("23P01", "bookings_no_overlap"))));

    [Theory]
    [InlineData("23P01", "another_constraint")] // an exclusion violation of some other constraint
    [InlineData("23505", "bookings_no_overlap")] // a unique violation
    [InlineData("23505", "IX_bookings_client_user_id_idempotency_key")]
    public void Other_violations_are_not(string sqlState, string constraint) =>
        Assert.False(BookingSlotGuard.IsSlotTaken(new DbUpdateException("x", Pg(sqlState, constraint))));

    [Fact]
    public void An_update_exception_without_a_postgres_cause_is_not_a_taken_slot()
    {
        Assert.False(BookingSlotGuard.IsSlotTaken(new DbUpdateException("x")));
        Assert.False(BookingSlotGuard.IsSlotTaken(new DbUpdateException("x", new InvalidOperationException())));
    }

    [Fact]
    public async Task SaveAsync_on_InMemory_saves_and_does_not_throw()
    {
        await using var db = InMemory();
        db.Bookings.Add(Slots.NewBooking(Guid.NewGuid(), Now.AddDays(2), 60, BookingStatus.Confirmed));
        await BookingSlotGuard.SaveAsync(db, default);
        Assert.Equal(1, await db.Bookings.CountAsync());
    }

    [Fact]
    public async Task LockPractitionerAsync_is_a_no_op_on_InMemory()
    {
        await using var db = InMemory();
        await BookingSlotGuard.LockPractitionerAsync(db, Guid.NewGuid(), default);
    }

    [Fact]
    public async Task ExpireStaleHoldsAsync_expires_only_this_practitioners_stale_pending_holds_and_leaves_saving_to_the_caller()
    {
        var mine = Guid.NewGuid();
        var name = "expire-" + Guid.NewGuid();
        var options = new DbContextOptionsBuilder<BookingDbContext>().UseInMemoryDatabase(name).Options;
        var stale = Slots.NewBooking(mine, Now.AddDays(2), 60, BookingStatus.PendingPayment, Now.AddMinutes(-5));
        var boundary = Slots.NewBooking(mine, Now.AddDays(3), 60, BookingStatus.PendingPayment, Now); // hold_expires_at <= now
        var live = Slots.NewBooking(mine, Now.AddDays(4), 60, BookingStatus.PendingPayment, Now.AddMinutes(5));
        var confirmed = Slots.NewBooking(mine, Now.AddDays(5), 60, BookingStatus.Confirmed, Now.AddMinutes(-5));
        var other = Slots.NewBooking(Guid.NewGuid(), Now.AddDays(2), 60, BookingStatus.PendingPayment, Now.AddMinutes(-5));
        await using (var seed = new BookingDbContext(options))
        {
            seed.Bookings.AddRange(stale, boundary, live, confirmed, other);
            await seed.SaveChangesAsync();
        }

        await using var db = new BookingDbContext(options);
        Assert.Equal(2, await BookingSlotGuard.ExpireStaleHoldsAsync(db, mine, Now, default));

        await using (var unsaved = new BookingDbContext(options)) // nothing was saved by the helper
            Assert.Equal(BookingStatus.PendingPayment, (await unsaved.Bookings.FindAsync(stale.Id))!.Status);

        await db.SaveChangesAsync();
        await using var check = new BookingDbContext(options);
        var status = await check.Bookings.ToDictionaryAsync(b => b.Id, b => b.Status);
        Assert.Equal(BookingStatus.Expired, status[stale.Id]);
        Assert.Equal(BookingStatus.Expired, status[boundary.Id]);
        Assert.Equal(BookingStatus.PendingPayment, status[live.Id]);
        Assert.Equal(BookingStatus.Confirmed, status[confirmed.Id]);
        Assert.Equal(BookingStatus.PendingPayment, status[other.Id]);
    }
}
