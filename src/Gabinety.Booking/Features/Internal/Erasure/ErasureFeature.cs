using System.Security.Cryptography;
using System.Text;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Payouts;
using Gabinety.Booking.Features.Proposals;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;

namespace Gabinety.Booking.Features.Internal.Erasure;

/// <summary>
/// DELETE /internal/v1/subjects/{userId} (CONTRACTS 4.3, SPEC 5 rule 4), behind the "Internal" policy, called when a user is deleted. For every booking where the
/// user is the client or the practitioner's user it replaces <c>brief_text</c> with <see cref="ErasedBrief"/>, clears <c>dispute_text</c> and the free-text
/// <c>status_reason</c>, and, when the user is the client, nulls <c>client_email</c> (the e-mail snapshot of T-04.09c); and, when the user is the practitioner, clears <c>meeting_url_snapshot</c> and replaces <c>offering_title</c> with the placeholder; and replaces the user id with <see cref="Token"/>: deterministic, one-way, distinct per user, so the unique index
/// <c>(client_user_id, idempotency_key)</c> stays valid. The same token replaces <c>idempotency_records.client_user_id</c> and the client id inside the
/// <c>pair_key</c> of the client's ledger rows (the only ledger column that carries a user id). It KEEPS ledger amounts and kinds, payments, payouts (none
/// carries a user id), and the booking's amounts, statuses and times: accounting. The counterparty still sees the booking, with the placeholder brief.
/// The user's <c>notification_preferences</c> rows are deleted in the same transaction (T-11.12).
/// Idempotent: a second call finds no row of the old id. An unknown user is 204, as Directory's and Reputation's erasure answer. One transaction, rows
/// updated in batches of <see cref="BatchSize"/>.
/// <para>Collaboration proposals (ADR proposal 0013, T-13.02) follow the same rules: the erased user's id becomes the token; as the client their e-mail, name snapshot and company name are cleared; the text of every round
/// the user wrote becomes <see cref="ErasedBrief"/> (the counterpart's own words stay: they are the counterpart's record); an open proposal is closed by the system (Withdrawn when the client was erased,
/// Rejected when the practitioner was) so nobody waits for an answer that cannot come; the user's unsent notifications are deleted and the sent ones lose the address of the user id.</para> Nothing here logs a text or the raw user id; the id appears only as a short hash prefix.
/// </summary>
public sealed class ErasureFeature : IServiceFeature
{
    public const string ErasedBrief = "[usunięto]";
    public const int BatchSize = 500;
    internal const string TokenPrefix = "erased-";

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapDelete("/internal/v1/subjects/{userId}", EraseAsync).RequireAuthorization("Internal");

    /// <summary><c>"erased-"</c> + the first 40 lowercase hex characters of SHA-256 of the UTF-8 user id (47 characters, fits varchar(64)). It cannot be reversed.</summary>
    public static string Token(string userId) => TokenPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(userId)))[..40];

    static async Task<IResult> EraseAsync(string userId, BookingDbContext db, TimeProvider time, ILogger<ErasureFeature> log, CancellationToken ct)
    {
        // A token is the output of an erasure, never an input: erasing it again would rewrite the rows to a second token.
        if (userId.StartsWith(TokenPrefix, StringComparison.Ordinal)) return Results.NoContent();
        var token = Token(userId);

        var (bookings, records, proposals) = db.Database.IsRelational()
            ? await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var counts = await EraseAllAsync(db, userId, token, time.GetUtcNow(), ct);
                await tx.CommitAsync(ct);
                return counts;
            })
            : await EraseAllAsync(db, userId, token, time.GetUtcNow(), ct);

        if (bookings + records + proposals > 0)
            log.LogInformation("Erased {Bookings} bookings, {Records} idempotency records and {Proposals} proposals of subject {SubjectHash}", bookings, records, proposals, token.Substring(TokenPrefix.Length, 8));
        return Results.NoContent();
    }

    static async Task<(int Bookings, int Records, int Proposals)> EraseAllAsync(BookingDbContext db, string userId, string token, DateTimeOffset now, CancellationToken ct)
    {
        db.ChangeTracker.Clear(); // a retried execution strategy starts from the database, not from a half-applied tracker
        int bookings = 0, records = 0;

        // Rows leave the filter as they are updated, so each pass takes the next batch and the loop ends when none is left.
        while (true)
        {
            var batch = await db.Bookings.Where(b => b.ClientUserId == userId || b.PractitionerUserId == userId).OrderBy(b => b.Id).Take(BatchSize).ToListAsync(ct);
            if (batch.Count == 0) break;

            var clientRows = batch.Where(b => b.ClientUserId == userId).ToDictionary(b => b.Id, b => b.PractitionerId);
            var fees = clientRows.Count == 0 ? [] : await db.LedgerEntries.Where(l => l.PairKey != null && clientRows.Keys.Contains(l.BookingId)).ToListAsync(ct);
            foreach (var l in fees) l.PairKey = PairKey.For(token, clientRows[l.BookingId]);

            foreach (var b in batch) Anonymise(b, userId, token);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            bookings += batch.Count;
        }

        // The key of an idempotency record is (client_user_id, idempotency_key): a key cannot be updated in place, so each record is replaced by a copy.
        while (true)
        {
            var batch = await db.IdempotencyRecords.Where(r => r.ClientUserId == userId).OrderBy(r => r.IdempotencyKey).Take(BatchSize).ToListAsync(ct);
            if (batch.Count == 0) break;
            foreach (var r in batch)
            {
                db.IdempotencyRecords.Remove(r);
                db.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    ClientUserId = token, IdempotencyKey = r.IdempotencyKey, RequestHash = r.RequestHash, ResponseStatus = r.ResponseStatus,
                    ResponseBody = r.ResponseBody, CreatedAt = r.CreatedAt,
                });
            }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            records += batch.Count;
        }

        // Collaboration proposals: the rows leave the filter as their user ids are replaced, so the loop ends when none is left.
        var proposals = 0;
        while (true)
        {
            var batch = await db.Proposals.Include(p => p.Rounds).Where(p => p.ClientUserId == userId || p.PractitionerUserId == userId).OrderBy(p => p.Id).Take(BatchSize).ToListAsync(ct);
            if (batch.Count == 0) break;
            var ids = batch.Select(p => p.Id).ToList();
            var mails = await db.Notifications.Where(n => n.ProposalId != null && ids.Contains(n.ProposalId.Value) && n.ToUserId == userId).ToListAsync(ct);
            db.Notifications.RemoveRange(mails.Where(n => n.SentAt == null)); // never sent, and now never will be
            foreach (var n in mails.Where(n => n.SentAt != null)) n.ToUserId = token;
            foreach (var p in batch) AnonymiseProposal(db, p, userId, token, now);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            proposals += batch.Count;
        }

        // Notification preferences are keyed by the raw user id and are no accounting: they are deleted, not anonymised (T-11.12).
        db.NotificationPreferences.RemoveRange(await db.NotificationPreferences.Where(p => p.UserId == userId).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return (bookings, records, proposals);
    }

    static void AnonymiseProposal(BookingDbContext db, Proposal p, string userId, string token, DateTimeOffset now)
    {
        var asClient = p.ClientUserId == userId;
        var asPractitioner = p.PractitionerUserId == userId;
        // an open proposal cannot be answered by someone who is gone: the system closes it, with a round, before the id is replaced
        if (ProposalStateMachine.IsOpen(p.Status))
        {
            p.Status = asClient ? ProposalStatus.Withdrawn : ProposalStatus.Rejected;
            p.RoundCount++;
            p.LastActionAt = now; p.ExpiresAt = null; p.ClosedAt = now;
            var round = new ProposalRound
            {
                Id = Guid.CreateVersion7(), ProposalId = p.Id, Number = p.RoundCount, By = ProposalActor.System,
                Action = asClient ? ProposalAction.Withdrawn : ProposalAction.Rejected, CreatedAt = now,
            };
            db.ProposalRounds.Add(round);
        }
        foreach (var r in p.Rounds.Where(r => r.Text != null && r.By == (asClient ? ProposalActor.Client : ProposalActor.Practitioner))) r.Text = ErasedBrief;
        if (asClient) { p.ClientUserId = token; p.ClientEmail = null; p.ClientDisplayName = null; p.CompanyName = null; }
        if (asPractitioner) p.PractitionerUserId = token;
    }

    static void Anonymise(BookingEntity b, string userId, string token)
    {
        b.BriefText = ErasedBrief;
        b.DisputeText = null;
        b.StatusReason = null;
        if (b.ClientUserId == userId) { b.ClientUserId = token; b.ClientEmail = null; b.ClientDisplayName = null; } // the e-mail and the name snapshots are the client's own
        if (b.PractitionerUserId == userId)
        {   // the practitioner's own data: a personal meeting link and the offering title; a client's erasure must not touch them
            b.PractitionerUserId = token;
            b.MeetingUrlSnapshot = null;
            b.OfferingTitle = ErasedBrief;
        }
    }
}
