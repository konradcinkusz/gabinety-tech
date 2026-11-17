using System.Text.Json.Serialization;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Directory.Features.Verification;

// These records belong in Gabinety.Contracts; they move there with the other admin shapes in a later change.
/// <summary>What a reviewer needs to decide on one profile. Deliberately without the owner's user id and without the GitHub challenge (a live secret).</summary>
public sealed record AdminPractitionerDetail(Guid Id, string Slug, string DisplayName, string Headline, string Bio, PractitionerStatus Status, string? StatusReason,
    bool GithubLinked, string? GithubLogin, string? Nip, bool NipValid, string? PortfolioUrl,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? SubmittedAt, DateTimeOffset? ApprovedAt);
public sealed record AdminVerificationEvent(Guid Id, [property: JsonConverter(typeof(JsonStringEnumConverter))] VerificationEventKind Kind, string ActorUserId, string? Note, DateTimeOffset At);
public sealed record AdminVerificationEventList(IReadOnlyList<AdminVerificationEvent> Items);

/// <summary>Read side of the admin verification page (CONTRACTS 3.4, same `Admin` policy as the list): one profile and its append-only event history.
/// Bio, notes and reasons are returned to the admin, never logged.</summary>
public sealed class AdminDetailFeature : IServiceFeature
{
    const int MaxEvents = 200; // a profile has a handful of events; the cap only bounds a pathological history

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin/practitioners").RequireAuthorization("Admin");
        admin.MapGet("{id:guid}", Detail);
        admin.MapGet("{id:guid}/verification-events", Events);
    }

    static IResult NotFound() =>
        Results.Json(ApiProblem.Of(ErrorCodes.NotFound, "Not found", 404, null, null), statusCode: 404, contentType: "application/problem+json");

    static async Task<IResult> Detail(Guid id, DirectoryDbContext db, CancellationToken ct)
    {
        var row = await db.Practitioners.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new
            {
                P = p,
                Submitted = db.VerificationEvents.Where(e => e.PractitionerId == p.Id && e.Kind == VerificationEventKind.Submitted).Max(e => (DateTimeOffset?)e.At),
            }).SingleOrDefaultAsync(ct);
        if (row is null) return NotFound();
        var p = row.P;
        return Results.Ok(new AdminPractitionerDetail(p.Id, p.Slug, p.DisplayName, p.Headline, p.Bio, p.Status, p.StatusReason, p.GithubLinked, p.GithubLogin,
            p.Nip, p.NipValid, p.PortfolioUrl, p.CreatedAt, p.UpdatedAt, row.Submitted, p.ApprovedAt));
    }

    static async Task<IResult> Events(Guid id, DirectoryDbContext db, CancellationToken ct)
    {
        if (!await db.Practitioners.AsNoTracking().AnyAsync(p => p.Id == id, ct)) return NotFound();
        var items = await db.VerificationEvents.AsNoTracking().Where(e => e.PractitionerId == id)
            .OrderBy(e => e.At).ThenBy(e => e.Id).Take(MaxEvents)
            .Select(e => new AdminVerificationEvent(e.Id, e.Kind, e.ActorUserId, e.Note, e.At)).ToListAsync(ct);
        return Results.Ok(new AdminVerificationEventList(items));
    }
}
