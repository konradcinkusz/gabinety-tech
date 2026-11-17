using Gabinety.Billing;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Booking.Jobs.Base;
using Gabinety.Booking.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Jobs.HoldExpiry;

/// <summary>
/// Expires the holds that ran out (CONTRACTS 8.1: <c>PendingPayment</c> to <c>Expired</c> when <c>hold_expires_at &lt;= now</c>). The slot is free from then on because
/// <c>Expired</c> is outside the no-overlap constraint's status set. Each cycle takes the due bookings oldest first in batches (<c>Jobs:HoldExpiry:BatchSize</c>, 50) until none
/// are left or <c>Jobs:HoldExpiry:MaxPerRun</c> (500) were handled; the rest waits for the next cycle.
/// Every booking is its own transaction through <see cref="BookingTransitions"/>, so one failing row never blocks another, and the transition re-checks status and hold under the row
/// lock: a booking paid or expired meanwhile (by another run, another instance or the webhook) is a no-op, not an error. The payment stays <c>Created</c>, so a payment that succeeds later is
/// refunded in full by the webhook handler. Idempotent, and safe in two processes at once.
/// </summary>
public class HoldExpiryJob(IServiceScopeFactory scopes, TimeProvider time, IConfiguration config, JobMetrics metrics, ILogger<HoldExpiryJob> log)
    : PeriodicJob(time, config, metrics, log)
{
    public const string JobName = "hold_expiry";
    const int DefaultBatch = 50, DefaultMaxPerRun = 500;

    public override string Name => JobName;

    public override async Task RunOnceAsync(DateTimeOffset now, CancellationToken ct) => await ExpireDueAsync(now, ct);

    /// <summary>
    /// Expires every due hold up to the per-run cap and returns how many this call expired. When some bookings failed it still handles all the others, then throws
    /// <see cref="JobFailedException"/>. A booking that fails is not tried again within the run.
    /// </summary>
    public async Task<int> ExpireDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        var batch = ReadPositiveInt(Configuration, "Jobs:HoldExpiry:BatchSize", DefaultBatch);
        var cap = ReadPositiveInt(Configuration, "Jobs:HoldExpiry:MaxPerRun", DefaultMaxPerRun);
        List<Guid> seen = [];
        int applied = 0, failed = 0;
        while (seen.Count < cap)
        {
            var ids = await SelectDueAsync(now, seen, Math.Min(batch, cap - seen.Count), ct);
            if (ids.Count == 0) break;
            seen.AddRange(ids);
            var (a, f) = await ProcessAsync(ids, now, ct);
            applied += a;
            failed += f;
        }
        if (applied > 0) log.LogInformation("HoldExpiry expired {Count} hold(s)", applied);
        if (failed > 0) throw new JobFailedException(failed);
        return applied;
    }

    /// <summary>Expires the given bookings (a list selected earlier may be stale) and returns how many were expired; the others are left as they are. Throws <see cref="JobFailedException"/> after handling all of them when some failed.</summary>
    public async Task<int> ExpireAsync(IReadOnlyCollection<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        var (applied, failed) = await ProcessAsync(ids, now, ct);
        if (failed > 0) throw new JobFailedException(failed);
        return applied;
    }

    /// <summary>Narrows what the job looks at. The default is everything; a test sharing a database with other tests restricts it to its own rows.</summary>
    protected virtual IQueryable<BookingEntity> Scope(IQueryable<BookingEntity> bookings) => bookings;

    async Task<List<Guid>> SelectDueAsync(DateTimeOffset now, List<Guid> exclude, int take, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var due = Scope(db.Bookings.AsNoTracking()).Where(b => b.Status == BookingStatus.PendingPayment && b.HoldExpiresAt <= now);
        if (exclude.Count > 0) due = due.Where(b => !exclude.Contains(b.Id));
        return await due.OrderBy(b => b.HoldExpiresAt).ThenBy(b => b.Id).Select(b => b.Id).Take(take).ToListAsync(ct);
    }

    async Task<(int Applied, int Failed)> ProcessAsync(IReadOnlyCollection<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        int applied = 0, failed = 0;
        foreach (var id in ids)
        {
            try
            {
                using var scope = scopes.CreateScope(); // one context, one transaction per booking
                var transitions = scope.ServiceProvider.GetRequiredService<BookingTransitions>();
                var result = await transitions.ApplyAsync(new TransitionRequest(id, BookingTrigger.HoldExpired, now) { Precondition = b => b.HoldExpiresAt <= now }, ct);
                if (result.Applied) applied++; // Invalid (moved meanwhile, or the hold is not due) and NotFound are no-ops
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failed++;
                log.LogError("HoldExpiry could not expire booking {BookingId}: {ExceptionType}", id, ex.GetType().Name);
            }
        }
        return (applied, failed);
    }
}

/// <summary>Registers the hold-expiry job as a singleton (a test resolves it and calls <see cref="HoldExpiryJob.RunOnceAsync"/>) and as the hosted service that runs it. <c>Jobs:Enabled=false</c> stops the loop from starting.</summary>
public sealed class HoldExpiryFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<HoldExpiryJob>();
        builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<HoldExpiryJob>());
    }

    public void Map(IEndpointRouteBuilder endpoints) { }
}
