using Gabinety.Billing;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Booking.Jobs.Base;
using Gabinety.Booking.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Jobs.AutoComplete;

/// <summary>
/// Completes the sessions nobody disputed (CONTRACTS 8.1: <c>Confirmed</c> to <c>Completed</c> once <c>end_utc + Booking:CompleteAfterHours</c> (48) has passed).
/// The effects are the ones of the contract row: <c>ended_at = end_utc</c> and <c>payout_due_at = end_utc + 48 h</c>; the fee hook (T-04.08c) computes
/// <c>fee_minor</c>/<c>payout_minor</c> in the same transaction. Each booking is its own transaction through <see cref="BookingTransitions"/> (trigger
/// <see cref="BookingTrigger.CompletionWindowElapsed"/>), which re-checks status and time under the row lock: a booking disputed, completed or cancelled meanwhile
/// (by a client, another run or another instance) is no longer <c>Confirmed</c>, so it is a no-op, not an error. A disputed booking therefore can never be completed.
/// Each cycle takes the due bookings oldest first in batches (<c>Jobs:AutoComplete:BatchSize</c>, 50) until none are left or <c>Jobs:AutoComplete:MaxPerRun</c> (500) were handled.
/// Idempotent, and safe in two processes at once.
/// </summary>
public class AutoCompleteJob(IServiceScopeFactory scopes, TimeProvider time, IConfiguration config, JobMetrics metrics, ILogger<AutoCompleteJob> log)
    : PeriodicJob(time, config, metrics, log)
{
    public const string JobName = "auto_complete";
    public const string WindowKey = "Booking:CompleteAfterHours";
    public const int DefaultWindowHours = 48, DefaultBatch = 50, DefaultMaxPerRun = 500;

    /// <summary>CONTRACTS 8.1 fixes the payout date of a completed booking at <c>end_utc + 48 h</c>, whatever the completion window is configured to.</summary>
    public static readonly TimeSpan PayoutDelay = TimeSpan.FromHours(48);
    /// <summary>The practitioner no-show claim runs until start + 72 h (CONTRACTS 4.2): money never moves before it has closed (ADR proposal 0011).</summary>
    public static readonly TimeSpan ClaimWindowAfterStart = TimeSpan.FromHours(72);

    /// <summary><c>payout_due_at</c> of a completed booking: <c>max(end + 48 h, start + 72 h)</c>.</summary>
    public static DateTimeOffset PayoutDueAt(DateTimeOffset startUtc, DateTimeOffset endUtc) =>
        DateTimeOffset.Compare(endUtc + PayoutDelay, startUtc + ClaimWindowAfterStart) >= 0 ? endUtc + PayoutDelay : startUtc + ClaimWindowAfterStart;

    public override string Name => JobName;

    public override async Task RunOnceAsync(DateTimeOffset now, CancellationToken ct) => await CompleteDueAsync(now, ct);

    TimeSpan Window => TimeSpan.FromHours(ReadPositiveInt(Configuration, WindowKey, DefaultWindowHours));

    /// <summary>Completes every due booking up to the per-run cap and returns how many this call completed. Failures are counted, every other booking is still handled, then <see cref="JobFailedException"/> is thrown. A booking that fails is not tried again within the run.</summary>
    public async Task<int> CompleteDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        var batch = ReadPositiveInt(Configuration, "Jobs:AutoComplete:BatchSize", DefaultBatch);
        var cap = ReadPositiveInt(Configuration, "Jobs:AutoComplete:MaxPerRun", DefaultMaxPerRun);
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
        if (applied > 0) log.LogInformation("AutoComplete completed {Count} booking(s)", applied);
        if (failed > 0) throw new JobFailedException(failed);
        return applied;
    }

    /// <summary>Completes the given bookings (a list selected earlier may be stale) and returns how many were completed; the others are left as they are.</summary>
    public async Task<int> CompleteAsync(IReadOnlyCollection<Guid> ids, DateTimeOffset now, CancellationToken ct)
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
        var cutoff = now - Window; // end_utc + window <= now
        var due = Scope(db.Bookings.AsNoTracking()).Where(b => b.Status == BookingStatus.Confirmed && b.EndUtc <= cutoff);
        if (exclude.Count > 0) due = due.Where(b => !exclude.Contains(b.Id));
        return await due.OrderBy(b => b.EndUtc).ThenBy(b => b.Id).Select(b => b.Id).Take(take).ToListAsync(ct);
    }

    async Task<(int Applied, int Failed)> ProcessAsync(IReadOnlyCollection<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        var window = Window;
        int applied = 0, failed = 0;
        foreach (var id in ids)
        {
            try
            {
                using var scope = scopes.CreateScope(); // one context, one transaction per booking
                var transitions = scope.ServiceProvider.GetRequiredService<BookingTransitions>();
                var result = await transitions.ApplyAsync(new TransitionRequest(id, BookingTrigger.CompletionWindowElapsed, now)
                {
                    Precondition = b => b.EndUtc + window <= now,
                    Effects = c =>
                    {
                        c.Booking.EndedAt = c.Booking.EndUtc;
                        c.Booking.PayoutDueAt = PayoutDueAt(c.Booking.StartUtc, c.Booking.EndUtc);
                    },
                }, ct);
                if (result.Applied) applied++; // Invalid (disputed, cancelled or completed meanwhile; not due) and NotFound are no-ops
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failed++;
                log.LogError("AutoComplete could not complete booking {BookingId}: {ExceptionType}", id, ex.GetType().Name);
            }
        }
        return (applied, failed);
    }
}

/// <summary>Registers the auto-complete job as a singleton (a test resolves it) and as the hosted service that runs it. <c>Jobs:Enabled=false</c> stops the loop from starting.</summary>
public sealed class AutoCompleteFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<AutoCompleteJob>();
        builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<AutoCompleteJob>());
    }

    public void Map(IEndpointRouteBuilder endpoints) { }
}
