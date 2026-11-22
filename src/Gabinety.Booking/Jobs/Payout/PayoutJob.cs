using System.Linq.Expressions;
using Gabinety.Billing;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Features.Payouts;
using Gabinety.Booking.Jobs.Base;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Jobs.Payout;

/// <summary>
/// Pays the practitioner for every money-complete booking whose <c>payout_due_at</c> has passed (CONTRACTS 8.1, 9.2; ADR-0005: the provider moves the money).
/// Money-complete: <c>Completed</c>, <c>ClientNoShow</c>, <c>LateCancelledByClient</c>, <c>Resolved</c> with <c>release</c> or <c>refund_partial</c>.
/// A <c>Completed</c> booking falls due at <c>max(end + 48 h, start + 72 h)</c> (ADR proposal 0011: the dispute window always closes before money moves);
/// a <c>Resolved</c> booking at <c>resolved_at</c>.
/// <para>Per booking, three steps, never one transaction around the network call:</para>
/// <list type="number">
/// <item><b>Short transaction.</b> The booking row is locked with <c>FOR UPDATE SKIP LOCKED</c> (a row another instance holds is skipped, not waited for), its status is re-read (a disputed or otherwise
/// ineligible booking is skipped) and the <c>payouts</c> row is created or taken up: <c>Pending</c>, <c>attempt_count</c> + 1, and <c>next_attempt_at</c> = now + the lease
/// (<c>Jobs:Payout:StalePendingSeconds</c>, 120). The lease is what keeps the other instances off while this one is at the provider; a crashed owner's lease just runs out.
/// The unique index on <c>booking_id</c> decides a race on the insert: the loser skips.</item>
/// <item><b>No transaction, no database context:</b> <see cref="IPaymentProvider.PayoutAsync"/> with the stable key <c>payout:&lt;bookingId:N&gt;</c> and a 10 s budget (SPEC 5.1).
/// The key makes a repeated call (a retry, a crash recovery, two instances) one payout at the provider.</item>
/// <item><b>Second short transaction.</b> Under the payout row's lock: <c>Sent</c> with <c>provider_ref</c> and <c>sent_at</c>, and the <c>PractitionerPayout</c> ledger entry written
/// exactly once (a row already <c>Sent</c> writes nothing).</item>
/// </list>
/// A provider failure sets the row <c>Failed</c> with <c>next_attempt_at</c> = now + backoff (<c>Jobs:Payout:RetryBackoffSeconds</c> 60, doubling); a later run takes it up with the same key,
/// up to <c>Jobs:Payout:MaxAttempts</c> (5) attempts. The bound is the row's own <c>attempt_count</c>, so a restart or a second instance neither resets nor doubles it. A payout that used up its
/// attempts is logged as an error once (the run that used the last one) and is not taken up again until an operator resets <c>attempt_count</c> (docs/operations/RUNBOOK.md).
/// Logs carry the booking id only: never the key, the practitioner or any user data. Idempotent, and safe in two processes at once.
/// </para>
/// </summary>
public class PayoutJob(IServiceScopeFactory scopes, IPaymentProvider provider, TimeProvider time, IConfiguration config, JobMetrics metrics, ILogger<PayoutJob> log)
    : PeriodicJob(time, config, metrics, log)
{
    public const string JobName = "payout";
    const int DefaultBatch = 50, DefaultMaxPerRun = 100, DefaultMaxAttempts = 5, DefaultBackoffSeconds = 60, DefaultStaleSeconds = 120;
    static readonly TimeSpan ProviderBudget = TimeSpan.FromSeconds(10);

    /// <summary>InMemory has neither row locks nor the unique index: this gate stands in for them (one process; the database is shared by name there).</summary>
    static readonly SemaphoreSlim InMemoryGate = new(1, 1);

    /// <summary>The status rule of "money-complete" for the query. <see cref="IsMoneyComplete"/> is the same rule for a loaded row.</summary>
    static readonly Expression<Func<BookingEntity, bool>> MoneyCompleteExpr = b =>
        b.Status == BookingStatus.Completed || b.Status == BookingStatus.ClientNoShow || b.Status == BookingStatus.LateCancelledByClient
        || (b.Status == BookingStatus.Resolved && (b.DisputeResolution == "release" || b.DisputeResolution == "refund_partial"));
    static readonly Func<BookingEntity, bool> IsMoneyComplete = MoneyCompleteExpr.Compile();

    static DateTimeOffset? DueAt(BookingEntity b) => b.PayoutDueAt ?? (b.Status == BookingStatus.Resolved ? b.ResolvedAt : null);

    sealed record Prepared(Guid PayoutId, Guid BookingId, Guid PractitionerId, long AmountMinor, int Attempt, string? DestinationAccountId, string? ProviderPaymentId);
    enum Outcome { Paid, Skipped, Failed }

    public override string Name => JobName;

    public override async Task RunOnceAsync(DateTimeOffset now, CancellationToken ct) => await PayDueAsync(now, ct);

    int MaxAttempts => ReadPositiveInt(Configuration, "Jobs:Payout:MaxAttempts", DefaultMaxAttempts);
    TimeSpan Lease => TimeSpan.FromSeconds(ReadPositiveInt(Configuration, "Jobs:Payout:StalePendingSeconds", DefaultStaleSeconds));

    /// <summary>Pays every due booking up to the per-run cap and returns how many payouts this call completed. Failures are counted, every other booking is still handled, then <see cref="JobFailedException"/> is thrown.</summary>
    public async Task<int> PayDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        var batch = ReadPositiveInt(Configuration, "Jobs:Payout:BatchSize", DefaultBatch);
        var cap = ReadPositiveInt(Configuration, "Jobs:Payout:MaxPerRun", DefaultMaxPerRun);
        List<Guid> seen = [];
        int paid = 0, failed = 0;
        while (seen.Count < cap)
        {
            var ids = await SelectDueAsync(now, seen, Math.Min(batch, cap - seen.Count), ct);
            if (ids.Count == 0) break;
            seen.AddRange(ids);
            var (p, f) = await ProcessAsync(ids, now, ct);
            paid += p;
            failed += f;
        }
        if (paid > 0) log.LogInformation("Payout completed {Count} payout(s)", paid);
        if (failed > 0) throw new JobFailedException(failed);
        return paid;
    }

    /// <summary>Pays the given bookings (a list selected earlier may be stale: each is re-checked in its own transaction) and returns how many payouts this call completed.</summary>
    public async Task<int> PayAsync(IReadOnlyCollection<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        var (paid, failed) = await ProcessAsync(ids, now, ct);
        if (failed > 0) throw new JobFailedException(failed);
        return paid;
    }

    /// <summary>Narrows what the job looks at. The default is everything; a test sharing a database with other tests restricts it to its own rows.</summary>
    protected virtual IQueryable<BookingEntity> Scope(IQueryable<BookingEntity> bookings) => bookings;

    async Task<List<Guid>> SelectDueAsync(DateTimeOffset now, List<Guid> seen, int take, CancellationToken ct)
    {
        // what is backing off, leased to a run or out of attempts must not take a place in the batch (it would starve the others every run)
        var maxAttempts = MaxAttempts;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var due = from b in Scope(db.Bookings.AsNoTracking()).Where(MoneyCompleteExpr)
                  where b.PayoutMinor > 0 && !seen.Contains(b.Id) && (b.PayoutDueAt ?? (b.Status == BookingStatus.Resolved ? b.ResolvedAt : null)) <= now
                  join p in db.Payouts.AsNoTracking() on b.Id equals p.BookingId into rows
                  from p in rows.DefaultIfEmpty()
                  where p == null || (p.Status != PayoutStatus.Sent && p.AttemptCount < maxAttempts && (p.NextAttemptAt == null || p.NextAttemptAt <= now))
                  orderby b.EndUtc, b.Id
                  select b.Id;
        return await due.Take(take).ToListAsync(ct);
    }

    async Task<(int Paid, int Failed)> ProcessAsync(IReadOnlyCollection<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        int paid = 0, failed = 0;
        foreach (var id in ids)
        {
            try
            {
                switch (await PayOneAsync(id, now, ct))
                {
                    case Outcome.Paid: paid++; break;
                    case Outcome.Failed: failed++; break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failed++;
                log.LogError("Payout could not be processed for booking {BookingId}: {ExceptionType}", id, ex.GetType().Name);
            }
        }
        return (paid, failed);
    }

    async Task<Outcome> PayOneAsync(Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var prepared = await PrepareAsync(id, now, ct);
        if (prepared is null) return Outcome.Skipped;

        // Step 2: the provider call (the 10 s budget is a timeout, not a clock reading). No scope, no DbContext, no transaction and no lock is open here (ADR-0005; SPEC 5.1: 10 s).
        PayoutResult result;
        try
        {
            using var budget = new CancellationTokenSource(ProviderBudget);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
            result = await provider.PayoutAsync(new PayoutRequest(prepared.PractitionerId, prepared.AmountMinor, "PLN", $"payout:{id:N}", prepared.DestinationAccountId, prepared.ProviderPaymentId, id), linked.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            await MarkFailedAsync(id, prepared, now, ct);
            log.LogError("Payout request failed for booking {BookingId}: {ExceptionType}", id, ex.GetType().Name);
            return Outcome.Failed;
        }

        // Step 3. If this fails the row stays as it was (Pending or Failed) and a later run repeats the call: same key, one payout at the provider.
        var completed = await CompleteAsync(prepared, result, now, ct);
        if (completed) log.LogInformation("Payout sent for booking {BookingId}", id);
        return completed ? Outcome.Paid : Outcome.Skipped;
    }

    /// <summary>Step 1. Null when there is nothing to pay or somebody else is on it.</summary>
    async Task<Prepared?> PrepareAsync(Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var maxAttempts = MaxAttempts;
        try
        {
            return await InTransactionAsync(async db =>
            {
                var booking = await LoadForUpdateAsync(db, id, ct); // SKIP LOCKED: null also when another instance has the row
                // re-read under the lock: disputed (or anything else that is not money-complete) since the selection means no payout
                if (booking is null || !IsMoneyComplete(booking) || booking.PayoutMinor is not > 0 || DueAt(booking) is not { } due || due > now) return null;

                var row = await db.Payouts.SingleOrDefaultAsync(p => p.BookingId == id, ct);
                if (row is null)
                    db.Payouts.Add(row = new Features.Payouts.Payout
                    {
                        Id = Guid.CreateVersion7(), BookingId = id, PractitionerId = booking.PractitionerId, AmountMinor = booking.PayoutMinor.Value,
                        Status = PayoutStatus.Pending, CreatedAt = now,
                    });
                else if (row.Status == PayoutStatus.Sent || row.AttemptCount >= maxAttempts || row.NextAttemptAt > now) return null; // done, out of attempts, backing off, or its owner is at work

                row.Status = PayoutStatus.Pending;
                row.AttemptCount++;
                row.NextAttemptAt = now + Lease;
                await db.SaveChangesAsync(ct);

                var account = await db.PaymentAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.PractitionerId == row.PractitionerId, ct);
                var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.BookingId == id, ct);
                return new Prepared(row.Id, id, row.PractitionerId, row.AmountMinor, row.AttemptCount, account?.ProviderAccountId, payment?.ProviderPaymentId);
            }, ct);
        }
        catch (DbUpdateException)
        {
            if (await RowExistsAsync(id, ct)) return null; // the unique index on booking_id: another instance won the insert
            throw;
        }
    }

    async Task<bool> CompleteAsync(Prepared p, PayoutResult result, DateTimeOffset now, CancellationToken ct) =>
        await InTransactionAsync(async db =>
        {
            var row = await LoadPayoutForUpdateAsync(db, p.PayoutId, ct);
            if (row is null || row.Status == PayoutStatus.Sent) return false; // another instance finished it: its ledger entry is the one
            row.Status = PayoutStatus.Sent;
            row.ProviderRef = result.ProviderPayoutId;
            row.SentAt = now;
            row.NextAttemptAt = null;
            if (!await db.LedgerEntries.AnyAsync(l => l.BookingId == row.BookingId && l.Kind == LedgerKind.PractitionerPayout, ct))
                db.LedgerEntries.Add(new LedgerEntry { Id = Guid.CreateVersion7(), BookingId = row.BookingId, Kind = LedgerKind.PractitionerPayout, AmountMinor = row.AmountMinor, CreatedAt = now });
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

    async Task MarkFailedAsync(Guid bookingId, Prepared prepared, DateTimeOffset now, CancellationToken ct)
    {
        var maxAttempts = MaxAttempts;
        var backoff = TimeSpan.FromSeconds(ReadPositiveInt(Configuration, "Jobs:Payout:RetryBackoffSeconds", DefaultBackoffSeconds) * Math.Pow(2, Math.Min(prepared.Attempt - 1, 10)));
        await InTransactionAsync<bool>(async db =>
        {
            var row = await LoadPayoutForUpdateAsync(db, prepared.PayoutId, ct);
            if (row is null || row.Status == PayoutStatus.Sent) return false; // somebody else got it through
            row.Status = PayoutStatus.Failed;
            row.NextAttemptAt = now + backoff;
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
        if (prepared.Attempt == maxAttempts) log.LogError("Payout for booking {BookingId} failed {Attempts} times and is not retried until an operator resets its attempt count", bookingId, prepared.Attempt);
    }

    async Task<bool> RowExistsAsync(Guid bookingId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BookingDbContext>().Payouts.AsNoTracking().AnyAsync(p => p.BookingId == bookingId, ct);
    }

    static async Task<BookingEntity?> LoadForUpdateAsync(BookingDbContext db, Guid id, CancellationToken ct) =>
        db.Database.IsNpgsql()
            ? await db.Bookings.FromSql($"SELECT * FROM bookings WHERE id = {id} FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(ct)
            : await db.Bookings.SingleOrDefaultAsync(b => b.Id == id, ct);

    static async Task<Features.Payouts.Payout?> LoadPayoutForUpdateAsync(BookingDbContext db, Guid id, CancellationToken ct) =>
        db.Database.IsNpgsql()
            ? await db.Payouts.FromSql($"SELECT * FROM payouts WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(ct)
            : await db.Payouts.SingleOrDefaultAsync(p => p.Id == id, ct);

    /// <summary>One short transaction in its own scope and context; the scope is gone when this returns, so nothing of it is open during the provider call.</summary>
    async Task<T> InTransactionAsync<T>(Func<BookingDbContext, Task<T>> body, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        async Task<T> Run() { db.ChangeTracker.Clear(); return await body(db); } // a retry starts from the database
        if (!db.Database.IsRelational())
        {
            await InMemoryGate.WaitAsync(ct);
            try { return await Run(); } finally { InMemoryGate.Release(); }
        }
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var r = await Run();
            await tx.CommitAsync(ct);
            return r;
        });
    }
}

/// <summary>Registers the payout job as a singleton (a test resolves it) and as the hosted service that runs it. <c>Jobs:Enabled=false</c> stops the loop from starting.</summary>
public sealed class PayoutFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<PayoutJob>();
        builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<PayoutJob>());
    }

    public void Map(IEndpointRouteBuilder endpoints) { }
}
