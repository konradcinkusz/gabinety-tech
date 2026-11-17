using Gabinety.Booking.Features.Availability;
using Gabinety.Booking.Features.Internal.TestClock;
using Gabinety.Booking.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Features.Internal.DemoReset;

/// <summary>
/// POST /internal/v1/demo/reset (CONTRACTS 10.4): removes the Booking rows of the <c>demo-</c> namespace and returns the counts. Mapped only outside a
/// deployed environment. A booking is in the namespace when its client or its practitioner user id starts with <c>demo-</c> (case-sensitive); its
/// payments and ledger entries go with it. Idempotency records are keyed by client user, so they go when that user is a demo user. Webhook events carry
/// no booking or payment reference, so they go by their own id: <c>EventId</c> starting with <c>demo-</c>.
/// Availability rules and exceptions are NOT touched (see the PR: keyed by practitioner GUID, a demo booking does not prove a demo practitioner).
/// There is no payouts table on main yet.
/// </summary>
public sealed class DemoResetFeature : IServiceFeature
{
    const string Ns = "demo-";

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        if (!NonDeployedProfile.IsActive(endpoints.ServiceProvider.GetRequiredService<IConfiguration>())) return;
        endpoints.MapPost("/internal/v1/demo/reset", ResetAsync).RequireAuthorization("Internal");
    }

    static async Task<IResult> ResetAsync(BookingDbContext db, PractitionerResolver practitioners, CancellationToken ct)
    {
        // StartsWith translates to a case-sensitive prefix match on PostgreSQL (LIKE) and on the in-memory provider (ordinal).
        var bookings = await db.Bookings.Where(b => b.ClientUserId.StartsWith(Ns) || b.PractitionerUserId.StartsWith(Ns)).ToListAsync(ct);
        var ids = bookings.Select(b => b.Id).ToList();
        var payments = await db.Payments.Where(p => ids.Contains(p.BookingId)).ToListAsync(ct);
        var ledger = await db.LedgerEntries.Where(l => ids.Contains(l.BookingId)).ToListAsync(ct);
        var idempotency = await db.IdempotencyRecords.Where(r => r.ClientUserId.StartsWith(Ns)).ToListAsync(ct);
        var events = await db.WebhookEvents.Where(e => e.EventId.StartsWith(Ns)).ToListAsync(ct);

        db.RemoveRange(payments);
        db.RemoveRange(ledger);
        db.RemoveRange(idempotency);
        db.RemoveRange(events);
        db.RemoveRange(bookings);
        await db.SaveChangesAsync(ct); // one unit of work: all or nothing
        practitioners.Clear(); // the practitioner ids it cached belong to a Directory that is being reset too

        return Results.Ok(new
        {
            bookings = bookings.Count, payments = payments.Count, ledgerEntries = ledger.Count,
            idempotencyRecords = idempotency.Count, webhookEvents = events.Count,
        });
    }
}
