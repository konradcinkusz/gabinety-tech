using System.Text.Json;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Payments;
using Gabinety.Contracts.Booking;
using Gabinety.Booking.Jobs.Base;
using Gabinety.Booking.Jobs.HoldExpiry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>The job restricted to one practitioner: the test InMemory database is shared by every host in the process, and an unscoped job would expire other tests' holds.</summary>
sealed class ScopedHoldExpiryJob(IServiceScopeFactory scopes, TimeProvider time, IConfiguration config, JobMetrics metrics, ILogger<HoldExpiryJob> log, Guid practitioner)
    : HoldExpiryJob(scopes, time, config, metrics, log)
{
    protected override IQueryable<BookingEntity> Scope(IQueryable<BookingEntity> q) => q.Where(b => b.PractitionerId == practitioner);
}

/// <summary>Throws from inside the transition's transaction for the listed bookings, with a message that carries the brief.</summary>
sealed class FailingHook(HashSet<Guid> bad) : IBookingTransitionHook
{
    public Task OnTransitionAsync(BookingTransitionContext context, CancellationToken ct) =>
        bad.Contains(context.Booking.Id) ? throw new InvalidOperationException("boom " + context.Booking.BriefText) : Task.CompletedTask;
}

static class Hold
{
    /// <summary>A host that does not start the periodic loop by itself; the tests call the job directly.</summary>
    public static CreateHost Host(HashSet<Guid>? failing = null, params (string Key, string Value)[] settings)
    {
        var h = Life.Host(new RecordingProvider());
        h.Settings["Jobs:Enabled"] = "false";
        foreach (var (k, v) in settings) h.Settings[k] = v;
        if (failing != null)
        {
            var provider = h.Configure;
            h.Configure = s => { provider?.Invoke(s); s.AddScoped<IBookingTransitionHook>(_ => new FailingHook(failing)); };
        }
        return h;
    }

    /// <summary>A booking in PendingPayment whose hold ends <paramref name="holdEndsIn"/> after the host clock; <paramref name="slot"/> keeps the slots of one practitioner apart.</summary>
    public static async Task<Guid> Seed(CreateHost h, TimeSpan holdEndsIn, int slot = 0, BookingStatus status = BookingStatus.PendingPayment, TimeSpan? created = null)
    {
        var id = Guid.NewGuid();
        var now = h.Clock.GetUtcNow();
        var start = now.AddDays(2).AddHours(3 * slot);
        await h.Seed(db =>
        {
            db.Bookings.Add(new BookingEntity
            {
                Id = id, ClientUserId = Life.Client, PractitionerId = h.Practitioner, PractitionerUserId = h.OwnerUserId, OfferingId = h.Offering,
                OfferingTitle = h.Title, DurationMinutes = 60, PriceMinor = h.PriceMinor, StartUtc = start, EndUtc = start.AddHours(1), Status = status,
                BriefText = "demo-brief: a first review of my plan", IdempotencyKey = Guid.NewGuid(), CreatedAt = now + (created ?? TimeSpan.Zero),
                HoldExpiresAt = now + holdEndsIn,
            });
            db.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(), BookingId = id, Provider = "Mock", ProviderPaymentId = "pay_" + id.ToString("N"), AmountMinor = h.PriceMinor,
                Status = PaymentStatus.Created, CreatedAt = now, UpdatedAt = now,
            });
        });
        return id;
    }

    public static ScopedHoldExpiryJob Job(CreateHost h) =>
        new(h.Services.GetRequiredService<IServiceScopeFactory>(), h.Clock, h.Services.GetRequiredService<IConfiguration>(),
            h.Services.GetRequiredService<JobMetrics>(), h.Services.GetRequiredService<ILogger<HoldExpiryJob>>(), h.Practitioner);

    public static Task<BookingStatus> StatusOf(CreateHost h, Guid id) => h.Query(db => db.Bookings.AsNoTracking().Where(b => b.Id == id).Select(b => b.Status).SingleAsync());

    /// <summary>Every row the job could touch, as text, so "changes nothing" is an equality.</summary>
    public static Task<string> Snapshot(CreateHost h) => h.Query(async db =>
    {
        var bookings = await db.Bookings.AsNoTracking().Where(b => b.PractitionerId == h.Practitioner).OrderBy(b => b.Id).ToListAsync();
        var ids = bookings.Select(b => b.Id).ToList();
        var payments = await db.Payments.AsNoTracking().Where(p => ids.Contains(p.BookingId)).OrderBy(p => p.Id).ToListAsync();
        var ledger = await db.LedgerEntries.AsNoTracking().Where(l => ids.Contains(l.BookingId)).OrderBy(l => l.Id).ToListAsync();
        return JsonSerializer.Serialize(new { bookings, payments, ledger });
    });
}
