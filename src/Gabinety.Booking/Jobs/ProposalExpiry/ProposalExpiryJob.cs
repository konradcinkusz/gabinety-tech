using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Proposals;
using Gabinety.Booking.Jobs.Base;
using Gabinety.Booking.Notifications;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Jobs.ProposalExpiry;

/// <summary>
/// Expires the collaboration proposals nobody answered in time (ADR proposal 0013 section 3): an open proposal whose <c>expires_at &lt;= now</c> becomes <c>Expired</c>, gets a system round, and both parties are told.
/// The expiry is <c>Proposals:ExpiryDays</c> after the last action (default 14), set by the feature that acted. Each cycle takes the due proposals oldest first in batches
/// (<c>Jobs:ProposalExpiry:BatchSize</c>, 50) until none are left or <c>Jobs:ProposalExpiry:MaxPerRun</c> (500) were handled. Every proposal is its own transaction, so one failing row never blocks another,
/// and the row's concurrency token makes a proposal answered meanwhile (by a party, another run or another instance) a no-op, not an error. Idempotent, and safe in two processes at once.
/// </summary>
public class ProposalExpiryJob(IServiceScopeFactory scopes, TimeProvider time, IConfiguration config, JobMetrics metrics, ILogger<ProposalExpiryJob> log)
    : PeriodicJob(time, config, metrics, log)
{
    public const string JobName = "proposal_expiry";
    const int DefaultBatch = 50, DefaultMaxPerRun = 500;

    public override string Name => JobName;

    public override async Task RunOnceAsync(DateTimeOffset now, CancellationToken ct) => await ExpireDueAsync(now, ct);

    /// <summary>Expires every due proposal up to the per-run cap and returns how many this call expired. When some failed it still handles all the others, then throws <see cref="JobFailedException"/>.</summary>
    public async Task<int> ExpireDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        var batch = ReadPositiveInt(Configuration, "Jobs:ProposalExpiry:BatchSize", DefaultBatch);
        var cap = ReadPositiveInt(Configuration, "Jobs:ProposalExpiry:MaxPerRun", DefaultMaxPerRun);
        List<Guid> seen = [];
        int applied = 0, failed = 0;
        while (seen.Count < cap)
        {
            var ids = await SelectDueAsync(now, seen, Math.Min(batch, cap - seen.Count), ct);
            if (ids.Count == 0) break;
            seen.AddRange(ids);
            foreach (var id in ids)
            {
                try { if (await ExpireOneAsync(id, now, ct)) applied++; }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    failed++;
                    log.LogError("ProposalExpiry could not expire proposal {ProposalId}: {ExceptionType}", id, ex.GetType().Name);
                }
            }
        }
        if (applied > 0) log.LogInformation("ProposalExpiry expired {Count} proposal(s)", applied);
        if (failed > 0) throw new JobFailedException(failed);
        return applied;
    }

    /// <summary>Narrows what the job looks at. The default is everything; a test sharing a database with other tests restricts it to its own rows.</summary>
    protected virtual IQueryable<Proposal> Scope(IQueryable<Proposal> proposals) => proposals;

    async Task<List<Guid>> SelectDueAsync(DateTimeOffset now, List<Guid> exclude, int take, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var due = Scope(db.Proposals.AsNoTracking()).Where(p => (p.Status == ProposalStatus.AwaitingPractitioner || p.Status == ProposalStatus.AwaitingClient) && p.ExpiresAt <= now);
        if (exclude.Count > 0) due = due.Where(p => !exclude.Contains(p.Id));
        return await due.OrderBy(p => p.ExpiresAt).ThenBy(p => p.Id).Select(p => p.Id).Take(take).ToListAsync(ct);
    }

    /// <summary>True when this call expired it; false when it moved meanwhile (answered, withdrawn or expired by another run).</summary>
    async Task<bool> ExpireOneAsync(Guid id, DateTimeOffset now, CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); // one context, one transaction per proposal
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationWriter>();
        using var held = db.Database.IsRelational() ? null : await PractitionerLocks.AcquireAsync(id, ct); // see ProposalActionsFeature: the in-memory provider has no transaction
        var p = await db.Proposals.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null || p.ExpiresAt is not { } due || due > now) return false;
        if (ProposalStateMachine.Next(p.Status, ProposalTrigger.Expire, ProposalActor.System) is not { } next) return false;

        p.Status = next;
        p.RoundCount++;
        p.LastActionAt = now;
        p.ExpiresAt = null;
        p.ClosedAt = now;
        db.ProposalRounds.Add(new ProposalRound
        {
            Id = Guid.CreateVersion7(), ProposalId = p.Id, Number = p.RoundCount, By = ProposalActor.System, Action = ProposalAction.Expired, CreatedAt = now,
        });
        notifications.ScheduleProposal(p, NotificationKind.ProposalExpired, p.ClientUserId, p.RoundCount, now);
        notifications.ScheduleProposal(p, NotificationKind.ProposalExpired, p.PractitionerUserId, p.RoundCount, now);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return false; } // a party answered at the same moment
        return true;
    }
}

/// <summary>Registers the proposal-expiry job as a singleton (a test resolves it and drives it) and as the hosted service that runs it. <c>Jobs:Enabled=false</c> stops the loop from starting.</summary>
public sealed class ProposalExpiryFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ProposalExpiryJob>();
        builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<ProposalExpiryJob>());
    }

    public void Map(IEndpointRouteBuilder endpoints) { }
}
