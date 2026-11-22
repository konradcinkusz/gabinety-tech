using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Clients;
using Gabinety.Directory.Features.Offerings;
using Gabinety.Directory.Features.Profiles;
using Gabinety.Directory.Features.Ranking;
using Gabinety.Directory.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Directory.Features.Internal;

public sealed record InternalOfferingDto(Guid OfferingId, Guid PractitionerId, string PractitionerUserId, PractitionerStatus PractitionerStatus,
    string Title, int DurationMinutes, MoneyDto Price, string? MeetingUrl, bool IsActive);
public sealed record InternalPractitionerDto(Guid PractitionerId, PractitionerStatus Status);

/// <summary><c>GET /internal/v1/practitioners/{id}</c>: what Booking shows next to a booking (ADR-0012 section 6). Display name and slug only; an erased practitioner reads <see cref="InternalFeature.ErasedName"/>.</summary>
public sealed record InternalPractitionerNameDto(Guid PractitionerId, string DisplayName, string Slug);

/// <summary>GET /internal/v1/practitioners/{userId}/contact: the e-mail snapshot only. Personal data; the caller (Booking) must not log it.</summary>
public sealed record InternalContactDto(string Email);
public sealed record ReputationPush(int RatingCount, decimal RatingAvg, long Version);
public sealed record BookingStatsPush(int CompletedCount, int PractitionerCancelCount, int NoShowUpheldCount, int OpenSlots14d, long Version);

/// <summary>
/// /internal/v1/... (CONTRACTS 3.6, ADR-0007) behind the "Internal" policy (X-Internal-Key). The two PUTs are monotonic: a version not greater than the stored
/// one is ignored with 204, so a retried or reordered push is harmless. Every response carries only the fields of the contract.
/// </summary>
public sealed class InternalFeature : IServiceFeature
{
    public const string ErasedName = "Usunięty użytkownik";

    public void Add(IHostApplicationBuilder builder) => builder.Services.TryAddSingleton(TimeProvider.System);

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var g = endpoints.MapGroup("/internal/v1").RequireAuthorization("Internal");
        g.MapGet("offerings/{id:guid}", GetOffering);
        g.MapGet("practitioners/by-user/{userId}", GetByUser);
        g.MapGet("practitioners/{id:guid}", GetNames);
        g.MapGet("practitioners/{userId}/contact", GetContact);
        g.MapPut("practitioners/{id:guid}/reputation", PutReputation);
        g.MapPut("practitioners/{id:guid}/booking-stats", PutStats);
        g.MapDelete("subjects/{userId}", Erase);
    }

    static IResult Problem(int status, string code, string title, Dictionary<string, string[]>? errors = null) =>
        Results.Json(ApiProblem.Of(code, title, status, null, errors), statusCode: status, contentType: "application/problem+json");

    static async Task<IResult> GetOffering(Guid id, DirectoryDbContext db, CancellationToken ct)
    {
        var row = await (from o in db.Offerings.AsNoTracking()
                         join p in db.Practitioners.AsNoTracking() on o.PractitionerId equals p.Id
                         where o.Id == id
                         select new { o, p.UserId, p.Status, p.MeetingUrlDefault }).SingleOrDefaultAsync(ct);
        if (row is null) return Problem(404, ErrorCodes.NotFound, "Not found");
        return Results.Ok(new InternalOfferingDto(row.o.Id, row.o.PractitionerId, row.UserId, row.Status, row.o.Title, row.o.DurationMinutes,
            new MoneyDto(row.o.PriceMinor, row.o.Currency), row.o.MeetingUrl ?? row.MeetingUrlDefault, row.o.IsActive));
    }

    static async Task<IResult> GetByUser(string userId, DirectoryDbContext db, CancellationToken ct)
    {
        var row = await db.Practitioners.AsNoTracking().Where(p => p.UserId == userId).Select(p => new { p.Id, p.Status }).SingleOrDefaultAsync(ct);
        return row is null ? Problem(404, ErrorCodes.NotFound, "Not found") : Results.Ok(new InternalPractitionerDto(row.Id, row.Status));
    }

    static async Task<IResult> GetNames(Guid id, DirectoryDbContext db, CancellationToken ct)
    {
        var row = await db.Practitioners.AsNoTracking().Where(p => p.Id == id).Select(p => new { p.Id, p.DisplayName, p.Slug }).SingleOrDefaultAsync(ct);
        return row is null ? Problem(404, ErrorCodes.NotFound, "Not found") : Results.Ok(new InternalPractitionerNameDto(row.Id, row.DisplayName, row.Slug));
    }

    /// <summary>404 for an unknown user and for a practitioner whose address is not held yet (they have not used their profile since the snapshot exists): the caller retries later.</summary>
    static async Task<IResult> GetContact(string userId, DirectoryDbContext db, CancellationToken ct)
    {
        var email = await db.Practitioners.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.ContactEmail).SingleOrDefaultAsync(ct);
        return string.IsNullOrEmpty(email) ? Problem(404, ErrorCodes.NotFound, "Not found") : Results.Ok(new InternalContactDto(email));
    }

    static async Task<IResult> PutReputation(Guid id, ReputationPush body, DirectoryDbContext db, TimeProvider time, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (body.RatingCount < 0) errors["ratingCount"] = ["must not be negative"];
        if (body.RatingAvg is < 0 or > 5) errors["ratingAvg"] = ["must be between 0 and 5"];
        if (body.Version < 0) errors["version"] = ["must not be negative"];
        if (errors.Count > 0) return Problem(400, ErrorCodes.ValidationFailed, "Validation failed", errors);

        var p = await db.Practitioners.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Problem(404, ErrorCodes.NotFound, "Not found");
        if (body.Version <= p.ReputationVersion) return Results.NoContent();

        p.RatingCount = body.RatingCount; p.RatingAvg = Math.Round(body.RatingAvg, 2); p.ReputationVersion = body.Version;
        RankingRecalculator.Recompute(p);
        p.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    static async Task<IResult> PutStats(Guid id, BookingStatsPush body, DirectoryDbContext db, IChecklistFacts facts, TimeProvider time, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (body.CompletedCount < 0) errors["completedCount"] = ["must not be negative"];
        if (body.PractitionerCancelCount < 0) errors["practitionerCancelCount"] = ["must not be negative"];
        if (body.NoShowUpheldCount < 0) errors["noShowUpheldCount"] = ["must not be negative"];
        if (body.OpenSlots14d < 0) errors["openSlots14d"] = ["must not be negative"];
        if (body.Version < 0) errors["version"] = ["must not be negative"];
        if (errors.Count > 0) return Problem(400, ErrorCodes.ValidationFailed, "Validation failed", errors);

        var p = await db.Practitioners.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Problem(404, ErrorCodes.NotFound, "Not found");
        if (body.Version <= p.StatsVersion) return Results.NoContent();

        p.CompletedCount = body.CompletedCount; p.PractitionerCancelCount = body.PractitionerCancelCount; p.NoShowUpheldCount = body.NoShowUpheldCount;
        p.OpenSlots14d = body.OpenSlots14d; p.StatsVersion = body.Version;
        // open slots are one of the completeness inputs (CONTRACTS 3.5), so the checklist is recomputed before the ranking uses it
        p.ProfileCompleteness = ProfileCompleteness.Compute(p.Headline.Length > 0, p.Bio.Length >= 200, await facts.ActiveOfferingCountAsync(p.Id, ct) > 0,
            p.GithubLinked, p.PortfolioUrl is not null, p.NipValid, p.OpenSlots14d > 0);
        RankingRecalculator.Recompute(p);
        p.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Anonymises the practitioner row and its offerings, after Booking has cancelled their future bookings with a full refund (ADR-0012): the practitioner id the
    /// call needs survives the erasure, but the clients must not be left with a booking nobody will hold. When Booking cannot finish, nothing is erased and the answer is
    /// 503, so the erasure job repeats the whole call. Idempotent: a second call finds no row for the old user id and answers 204.
    /// </summary>
    static async Task<IResult> Erase(string userId, DirectoryDbContext db, TimeProvider time, IBookingClient booking, CancellationToken ct)
    {
        var p = await db.Practitioners.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (p is null) return Results.NoContent();
        try { await booking.CancelFutureBookingsAsync(p.Id, ct); }
        catch (BookingUnavailableException)
        {
            return Results.Json(ApiProblem.Of(ErrorCodes.DependencyUnavailable, "Dependency unavailable", 503,
                "Booking could not cancel the practitioner's future bookings yet; nothing was erased. Repeat the call.", null), statusCode: 503, contentType: "application/problem+json");
        }

        p.DisplayName = ErasedName; p.Headline = ""; p.Bio = "";
        p.GithubLogin = null; p.GithubLinked = false; p.Nip = null; p.NipValid = false; p.PortfolioUrl = null; p.MeetingUrlDefault = null; p.StatusReason = null;
        p.GithubChallengeCode = null; p.GithubChallengeLogin = null; p.GithubChallengeExpiresAt = null; p.ContactEmail = null;
        p.Status = PractitionerStatus.Suspended;
        p.UserId = "erased-" + Guid.NewGuid().ToString("n");
        p.Slug = "removed-" + Guid.NewGuid().ToString("n")[..16];
        p.UpdatedAt = time.GetUtcNow();
        foreach (var o in await db.Offerings.Where(x => x.PractitionerId == p.Id).ToListAsync(ct))
        {
            o.Title = ""; o.Description = ""; o.MeetingUrl = null; o.IsActive = false;
        }
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
