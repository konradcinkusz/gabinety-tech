using System.Buffers.Text;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Clients;
using Gabinety.Directory.Features.Profiles;
using Gabinety.Directory.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Directory.Features.Verification;

// These records belong in Gabinety.Contracts; they move there in a later change.
public sealed record GithubLinkRequest(string? GithubLogin);
public sealed record GithubLinkChallenge(string Code, string Instruction);
public sealed record RejectRequest(string? Reason);
public sealed record SuspendRequest(string? Reason);
public sealed record AdminPractitionerRow(Guid Id, string Slug, string DisplayName, string Headline, PractitionerStatus Status,
    string? StatusReason, bool GithubLinked, string? GithubLogin, bool NipValid, DateTimeOffset CreatedAt, DateTimeOffset? SubmittedAt);
public sealed record AdminPractitionerPage(IReadOnlyList<AdminPractitionerRow> Items, string? NextCursor);

/// <summary>GitHub linking by bio challenge (CONTRACTS 3.3) and the admin verification workflow with moderators (CONTRACTS 3.4). Reasons and bios are never logged.</summary>
public sealed class VerificationFeature : IServiceFeature
{
    const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
    static readonly TimeSpan ChallengeLife = TimeSpan.FromMinutes(30);
    const int DefaultLimit = 12, MaxLimit = 50;

    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        if (Internal.DemoReset.NonDeployedProfile.IsActive(builder.Configuration)) { FakeGithubProfileLookup.Register(builder.Services); return; } // fake profile: no network
#pragma warning disable EXTEXP0001 // the kernel adds the retrying resilience handler to every client; GitHub is called once, 5 s, no retry
        builder.Services.AddHttpClient<IGithubProfileLookup, HttpGithubProfileLookup>(c => c.Timeout = HttpGithubProfileLookup.Timeout)
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
    }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var me = endpoints.MapGroup("/api/v1/me/practitioner/github-link").RequireAuthorization("Authenticated");
        me.MapPost("", RequestLink);
        me.MapPost("verify", VerifyLink);

        var admin = endpoints.MapGroup("/api/v1/admin").RequireAuthorization("Admin");
        admin.MapGet("practitioners", List);
        admin.MapPost("practitioners/{id:guid}/approve", (Guid id, ClaimsPrincipal u, DirectoryDbContext db, TimeProvider t, CancellationToken ct) =>
            Transition(id, PractitionerTrigger.Approve, null, u, db, t, ct));
        admin.MapPost("practitioners/{id:guid}/reject", (Guid id, RejectRequest? req, ClaimsPrincipal u, DirectoryDbContext db, TimeProvider t, CancellationToken ct) =>
            Transition(id, PractitionerTrigger.Reject, req?.Reason ?? "", u, db, t, ct));
        admin.MapPost("practitioners/{id:guid}/suspend", (Guid id, SuspendRequest? req, ClaimsPrincipal u, DirectoryDbContext db, TimeProvider t, IBookingClient booking, CancellationToken ct) =>
            Transition(id, PractitionerTrigger.Suspend, req?.Reason ?? "", u, db, t, ct, booking));
        admin.MapPost("practitioners/{id:guid}/reinstate", (Guid id, ClaimsPrincipal u, DirectoryDbContext db, TimeProvider t, CancellationToken ct) =>
            Transition(id, PractitionerTrigger.Reinstate, null, u, db, t, ct));
        admin.MapPut("moderators/{userId}", AddModerator).RequireAuthorization(p => p.RequireRole("SuperAdmin"));
        admin.MapDelete("moderators/{userId}", RemoveModerator).RequireAuthorization(p => p.RequireRole("SuperAdmin"));
    }

    static IResult Problem(int status, string code, string title, Dictionary<string, string[]>? errors = null, string? detail = null) =>
        Results.Json(ApiProblem.Of(code, title, status, detail, errors), statusCode: status, contentType: "application/problem+json");

    static IResult Invalid(string field, string message) => Problem(422, ErrorCodes.ValidationFailed, "Validation failed", new() { [field] = [message] });

    static string? UserId(ClaimsPrincipal u) => u.FindFirst("sub")?.Value ?? u.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    // ---- github-link

    static async Task<IResult> RequestLink(GithubLinkRequest? req, ClaimsPrincipal user, DirectoryDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (UserId(user) is not { } uid) return Results.Unauthorized();
        if (!GithubHandle.IsValid(req?.GithubLogin)) return Invalid("githubLogin", "1 to 39 characters of A-Z, a-z, 0-9 and hyphen, not starting or ending with a hyphen");
        var p = await db.Practitioners.SingleOrDefaultAsync(x => x.UserId == uid, ct);
        if (p is null) return Problem(404, ErrorCodes.NotFound, "Not found");

        var code = "gabinety-verify-" + RandomNumberGenerator.GetString(Alphabet, 10); // cryptographic RNG, unbiased
        p.GithubChallengeCode = code;
        p.GithubChallengeLogin = req!.GithubLogin;
        p.GithubChallengeExpiresAt = time.GetUtcNow() + ChallengeLife;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new GithubLinkChallenge(code, $"Put the code {code} in the bio of the public GitHub profile {p.GithubChallengeLogin} within 30 minutes, then call verify."));
    }

    static IResult NotLinked(Dictionary<string, string[]>? errors = null) =>
        Problem(422, ErrorCodes.GithubNotLinked, "GitHub profile is not linked", errors);

    static async Task<IResult> VerifyLink(ClaimsPrincipal user, DirectoryDbContext db, IGithubProfileLookup github, IChecklistFacts facts, TimeProvider time, CancellationToken ct)
    {
        if (UserId(user) is not { } uid) return Results.Unauthorized();
        var p = await db.Practitioners.SingleOrDefaultAsync(x => x.UserId == uid, ct);
        if (p is null) return Problem(404, ErrorCodes.NotFound, "Not found");

        var now = time.GetUtcNow();
        if (p.GithubChallengeCode is not { } code || p.GithubChallengeLogin is not { } login || p.GithubChallengeExpiresAt is not { } expires || now > expires)
            return NotLinked(); // none pending, or expired: exactly 30 minutes is still valid

        var lower = login.ToLowerInvariant();
        if (await db.Practitioners.AnyAsync(x => x.Id != p.Id && x.GithubLinked && x.GithubLogin != null && x.GithubLogin.ToLower() == lower, ct))
            return NotLinked(new() { ["githubLogin"] = ["this GitHub account is already linked to another profile"] });

        GithubProfile profile;
        try { profile = await github.GetBioAsync(login, ct); }
        catch (GithubUnavailableException) { return Problem(503, ErrorCodes.DependencyUnavailable, "GitHub is not available"); }
        if (!profile.Exists || profile.Bio is null || !profile.Bio.Contains(code, StringComparison.Ordinal)) return NotLinked();

        p.GithubLinked = true; p.GithubLogin = login;
        p.GithubChallengeCode = null; p.GithubChallengeLogin = null; p.GithubChallengeExpiresAt = null;
        p.ProfileCompleteness = ProfileCompleteness.Compute(p.Headline.Length > 0, p.Bio.Length >= 200, await facts.ActiveOfferingCountAsync(p.Id, ct) > 0,
            true, p.PortfolioUrl is not null, p.NipValid, p.OpenSlots14d > 0);
        Ranking.RankingRecalculator.Recompute(p);
        p.UpdatedAt = now;
        VerificationEvents.Record(db, p.Id, VerificationEventKind.GithubLinked, uid, login, time);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) // the unique partial index decided a race for the handle
        {
            return NotLinked(new() { ["githubLogin"] = ["this GitHub account is already linked to another profile"] });
        }
        return Results.NoContent();
    }

    // ---- admin transitions

    /// <summary>
    /// One admin transition. A suspension then asks Booking to cancel the practitioner's future bookings with a full refund (ADR-0012): the status is saved FIRST, so Booking
    /// already refuses new bookings when that runs, and the call is idempotent. When Booking cannot finish, the profile stays suspended and the answer is 503 saying so;
    /// the cancellation is repeated through Booking's internal route (RUNBOOK 9.2).
    /// </summary>
    static async Task<IResult> Transition(Guid id, PractitionerTrigger trigger, string? reason, ClaimsPrincipal user, DirectoryDbContext db, TimeProvider time, CancellationToken ct, IBookingClient? booking = null)
    {
        if (UserId(user) is not { } admin) return Results.Unauthorized();
        string? note = null;
        if (reason is not null)
        {
            note = reason.Trim();
            if (note.Length is < 10 or > 500) return Invalid("reason", "10 to 500 characters");
        }
        var p = await db.Practitioners.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Problem(404, ErrorCodes.NotFound, "Not found");
        if (!PractitionerStateMachine.TryTransition(p.Status, trigger, out var next)) return Problem(409, ErrorCodes.InvalidTransition, "Invalid transition");

        var now = time.GetUtcNow();
        p.Status = next; p.StatusReason = note; p.UpdatedAt = now;
        if (trigger == PractitionerTrigger.Approve) p.ApprovedAt = now;
        Ranking.RankingRecalculator.Recompute(p); // scores an Approved row (approve and reinstate); a no-op for any other status
        var kind = trigger switch
        {
            PractitionerTrigger.Approve => VerificationEventKind.Approved,
            PractitionerTrigger.Reject => VerificationEventKind.Rejected,
            PractitionerTrigger.Suspend => VerificationEventKind.Suspended,
            _ => VerificationEventKind.Reinstated,
        };
        VerificationEvents.Record(db, p.Id, kind, admin, note, time); // one SaveChanges: the status and its event commit together
        await db.SaveChangesAsync(ct);
        if (trigger == PractitionerTrigger.Suspend && booking is not null)
        {
            try { await booking.CancelFutureBookingsAsync(p.Id, ct); }
            catch (BookingUnavailableException)
            {
                return Problem(503, ErrorCodes.DependencyUnavailable, "Dependency unavailable",
                    detail: "The practitioner is suspended, but their future bookings could not all be cancelled yet; repeat the cancellation (RUNBOOK 9.2).");
            }
        }
        return Results.NoContent();
    }

    // ---- admin list

    static async Task<IResult> List(string? status, string? cursor, int? limit, DirectoryDbContext db, CancellationToken ct)
    {
        var wanted = PractitionerStatus.Submitted;
        if (status is not null && !(Enum.TryParse(status, true, out wanted) && Enum.IsDefined(wanted) && !char.IsDigit(status[0])))
            return Invalid("status", "one of Draft, Submitted, Approved, Rejected, Suspended");
        (DateTimeOffset At, Guid Id)? after = null;
        if (cursor is not null)
        {
            if (!TryDecode(cursor, out var c)) return Invalid("cursor", "not a cursor of this list");
            after = c;
        }
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        // Sort key: the time of the latest Submitted event, else created_at; then id. Order BEFORE projecting to the record (Npgsql cannot translate it after).
        var q = db.Practitioners.AsNoTracking().Where(p => p.Status == wanted)
            .Select(p => new
            {
                P = p,
                Submitted = db.VerificationEvents.Where(e => e.PractitionerId == p.Id && e.Kind == VerificationEventKind.Submitted).Max(e => (DateTimeOffset?)e.At),
            })
            .Select(x => new { x.P, x.Submitted, Key = x.Submitted ?? x.P.CreatedAt });
        if (after is { } a) q = q.Where(x => x.Key > a.At || (x.Key == a.At && x.P.Id.CompareTo(a.Id) > 0));
        var rows = await q.OrderBy(x => x.Key).ThenBy(x => x.P.Id).Take(take + 1).ToListAsync(ct);

        var items = rows.Take(take).Select(x => new AdminPractitionerRow(x.P.Id, x.P.Slug, x.P.DisplayName, x.P.Headline, x.P.Status, x.P.StatusReason,
            x.P.GithubLinked, x.P.GithubLogin, x.P.NipValid, x.P.CreatedAt, x.Submitted)).ToList();
        var next = rows.Count > take ? Encode(rows[take - 1].Key, rows[take - 1].P.Id) : null;
        return Results.Ok(new AdminPractitionerPage(items, next));
    }

    /// <summary>Opaque cursor: base64url of "&lt;UTC ticks&gt;:&lt;id&gt;" (the id is last: a total order).</summary>
    static string Encode(DateTimeOffset at, Guid id) => Base64Url.EncodeToString(Encoding.ASCII.GetBytes($"{at.UtcTicks.ToString(CultureInfo.InvariantCulture)}:{id:N}"));

    static bool TryDecode(string cursor, out (DateTimeOffset At, Guid Id) value)
    {
        value = default;
        try
        {
            var parts = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(cursor)).Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks > DateTime.MaxValue.Ticks || !Guid.TryParseExact(parts[1], "N", out var id)) return false;
            value = (new DateTimeOffset(ticks, TimeSpan.Zero), id);
            return true;
        }
        catch (FormatException) { return false; }
    }

    // ---- moderators (SuperAdmin)

    static async Task<IResult> AddModerator(string userId, DirectoryDbContext db, CancellationToken ct)
    {
        if (userId.Length is < 1 or > 64) return Invalid("userId", "1 to 64 characters");
        if (await db.Moderators.AnyAsync(m => m.UserId == userId, ct)) return Results.NoContent();
        db.Moderators.Add(new Moderator { UserId = userId });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (!await db.Moderators.AsNoTracking().AnyAsync(m => m.UserId == userId, ct)) throw; // a concurrent PUT won: still idempotent
        }
        return Results.NoContent();
    }

    static async Task<IResult> RemoveModerator(string userId, DirectoryDbContext db, CancellationToken ct)
    {
        if (await db.Moderators.SingleOrDefaultAsync(m => m.UserId == userId, ct) is { } m)
        {
            db.Moderators.Remove(m);
            try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { /* already gone */ }
        }
        return Results.NoContent();
    }
}
