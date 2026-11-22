using System.Security.Claims;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Profiles;
using Gabinety.Directory.Features.Taxonomy;
using Gabinety.Directory.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Directory.Features.Offerings;

/// <summary>/api/v1/me/practitioner/offerings: list, create, replace, deactivate (CONTRACTS 3.3). Every call runs on the caller's own practitioner row.</summary>
public sealed class OfferingsFeature : IServiceFeature
{
    const string Route = "/api/v1/me/practitioner/offerings";
    const int MaxActive = 5, MaxTechs = 4;
    const long DefaultMinPrice = 5000, DefaultMaxPrice = 150000;

    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        // Added after (or before) ProfilesFeature: its registration is a TryAdd, so this one is the last or the only one either way.
        builder.Services.AddSingleton<IChecklistFacts, OfferingFacts>();
    }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var g = endpoints.MapGroup(Route).RequireAuthorization("Authenticated");
        g.MapGet("", List);
        g.MapPost("", Create);
        g.MapPut("{id:guid}", Update);
        g.MapPost("{id:guid}/deactivate", Deactivate);
        g.MapPost("{id:guid}/activate", Activate);
    }

    static IResult Problem(int status, string code, string title, Dictionary<string, string[]>? errors = null) =>
        Results.Json(ApiProblem.Of(code, title, status, null, errors), statusCode: status, contentType: "application/problem+json");

    static string? UserId(ClaimsPrincipal u) => u.FindFirst("sub")?.Value ?? u.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    static OfferingPrivate View(Offering o) => new(o.Id, o.Title, o.Description, (int)o.DurationMinutes, new MoneyDto(o.PriceMinor, o.Currency),
        o.ProblemSlug, o.TechSlugs, o.IsActive, o.MeetingUrl);

    /// <summary>
    /// Runs <paramref name="body"/> on the caller's practitioner row, or answers 404 when there is none. On PostgreSQL the row is read
    /// `FOR UPDATE` inside a transaction, so two requests of one practitioner run one after the other (the 5-active-offerings check and the
    /// insert cannot interleave); the retrying execution strategy needs the whole unit inside one delegate. InMemory has no transactions or locks.
    /// </summary>
    static async Task<IResult> OnOwnRow(DirectoryDbContext db, string uid, CancellationToken ct, Func<Practitioner, Task<IResult>> body)
    {
        if (!db.Database.IsRelational())
            return await db.Practitioners.SingleOrDefaultAsync(x => x.UserId == uid, ct) is { } p ? await body(p) : Problem(404, ErrorCodes.NotFound, "Not found");

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); // a retry starts from the database, not from a half-applied attempt
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var rows = await db.Practitioners.FromSql($"SELECT * FROM practitioners WHERE user_id = {uid} FOR UPDATE").ToListAsync(ct);
            if (rows.Count == 0) return Problem(404, ErrorCodes.NotFound, "Not found");
            var result = await body(rows[0]);
            await tx.CommitAsync(ct);
            return result;
        });
    }

    static async Task<IResult> List(ClaimsPrincipal user, DirectoryDbContext db, CancellationToken ct)
    {
        if (UserId(user) is not { } uid) return Results.Unauthorized();
        var p = await db.Practitioners.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == uid, ct);
        if (p is null) return Problem(404, ErrorCodes.NotFound, "Not found");
        var rows = await db.Offerings.AsNoTracking().Where(o => o.PractitionerId == p.Id).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).ToListAsync(ct); // order, then project
        return Results.Ok(new { items = rows.Select(View) });
    }

    static Task<IResult> Create(OfferingRequest req, ClaimsPrincipal user, DirectoryDbContext db, IContactDetailDetector contact,
        IConfiguration config, TimeProvider time, CancellationToken ct) =>
        UserId(user) is not { } uid ? Task.FromResult(Results.Unauthorized()) : OnOwnRow(db, uid, ct, async p =>
        {
            if (await Validate(req, db, contact, config, ct) is { } rejected) return rejected;
            if (await db.Offerings.CountAsync(o => o.PractitionerId == p.Id && o.IsActive, ct) >= MaxActive)
                return Problem(422, ErrorCodes.TooManyOfferings, "At most 5 active offerings");
            var o = new Offering { Id = Guid.NewGuid(), PractitionerId = p.Id, Currency = "PLN", IsActive = true, CreatedAt = time.GetUtcNow() };
            Apply(o, req);
            db.Offerings.Add(o);
            await Save(db, p, time, ct);
            return Results.Created($"{Route}/{o.Id}", View(o));
        });

    static Task<IResult> Update(Guid id, OfferingRequest req, ClaimsPrincipal user, DirectoryDbContext db, IContactDetailDetector contact,
        IConfiguration config, TimeProvider time, CancellationToken ct) =>
        UserId(user) is not { } uid ? Task.FromResult(Results.Unauthorized()) : OnOwnRow(db, uid, ct, async p =>
        {
            var o = await db.Offerings.SingleOrDefaultAsync(x => x.Id == id && x.PractitionerId == p.Id, ct);
            if (o is null) return Problem(404, ErrorCodes.NotFound, "Not found");
            if (await Validate(req, db, contact, config, ct) is { } rejected) return rejected;
            Apply(o, req); // IsActive is not touched: reactivation is its own route (activate)
            await Save(db, p, time, ct);
            return Results.Ok(View(o));
        });

    static Task<IResult> Deactivate(Guid id, ClaimsPrincipal user, DirectoryDbContext db, TimeProvider time, CancellationToken ct) =>
        UserId(user) is not { } uid ? Task.FromResult(Results.Unauthorized()) : OnOwnRow(db, uid, ct, async p =>
        {
            var o = await db.Offerings.SingleOrDefaultAsync(x => x.Id == id && x.PractitionerId == p.Id, ct);
            if (o is null) return Problem(404, ErrorCodes.NotFound, "Not found");
            o.IsActive = false; // already inactive: nothing changes, still 204
            await Save(db, p, time, ct);
            return Results.NoContent();
        });

    /// <summary>Turns a deactivated offering back on (the practitioner's undo of "Wyłącz"). The limit of 5 active offerings applies; already active: nothing changes, still 204.</summary>
    static Task<IResult> Activate(Guid id, ClaimsPrincipal user, DirectoryDbContext db, TimeProvider time, CancellationToken ct) =>
        UserId(user) is not { } uid ? Task.FromResult(Results.Unauthorized()) : OnOwnRow(db, uid, ct, async p =>
        {
            var o = await db.Offerings.SingleOrDefaultAsync(x => x.Id == id && x.PractitionerId == p.Id, ct);
            if (o is null) return Problem(404, ErrorCodes.NotFound, "Not found");
            if (o.IsActive) return Results.NoContent();
            if (await db.Offerings.CountAsync(x => x.PractitionerId == p.Id && x.IsActive, ct) >= MaxActive)
                return Problem(422, ErrorCodes.TooManyOfferings, "At most 5 active offerings");
            o.IsActive = true;
            await Save(db, p, time, ct);
            return Results.NoContent();
        });

    static void Apply(Offering o, OfferingRequest r)
    {
        o.Title = r.Title.Trim();
        o.Description = r.Description?.Trim() ?? "";
        o.DurationMinutes = (short)r.DurationMinutes; // validated to 30 or 60
        o.PriceMinor = r.PriceMinor;
        o.ProblemSlug = r.ProblemSlug;
        o.TechSlugs = (r.TechSlugs ?? []).ToArray();
        o.MeetingUrl = string.IsNullOrWhiteSpace(r.MeetingUrl) ? null : r.MeetingUrl.Trim();
    }

    /// <summary>Stores the change, then recomputes the practitioner's completeness from the offerings as they now are (the "one active offering" component).</summary>
    static async Task Save(DirectoryDbContext db, Practitioner p, TimeProvider time, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        var hasActive = await db.Offerings.AnyAsync(o => o.PractitionerId == p.Id && o.IsActive, ct);
        p.ProfileCompleteness = ProfileCompleteness.Compute(p.Headline.Length > 0, p.Bio.Length >= 200, hasActive, p.GithubLinked,
            p.PortfolioUrl is not null, p.NipValid, p.OpenSlots14d > 0);
        Ranking.RankingRecalculator.Recompute(p);
        p.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>CONTRACTS 3.5. Returns the 422 to send, or null when the request is acceptable. Field errors come first, then the contact-detail check.</summary>
    static async Task<IResult?> Validate(OfferingRequest r, DirectoryDbContext db, IContactDetailDetector contact, IConfiguration config, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var title = r.Title?.Trim() ?? "";
        var description = r.Description?.Trim() ?? "";
        if (title.Length is < 5 or > 80 || title.Any(char.IsControl)) errors["title"] = ["5 to 80 characters, no control characters"];
        if (description.Length > 600) errors["description"] = ["at most 600 characters"];
        if (r.DurationMinutes is not (30 or 60)) errors["durationMinutes"] = ["30 or 60"];
        var min = config.GetValue<long?>("Directory:Price:MinMinor") ?? DefaultMinPrice;
        var max = config.GetValue<long?>("Directory:Price:MaxMinor") ?? DefaultMaxPrice;
        if (r.PriceMinor < min || r.PriceMinor > max) errors["priceMinor"] = [$"{min} to {max} grosz"];

        var meeting = string.IsNullOrWhiteSpace(r.MeetingUrl) ? null : r.MeetingUrl.Trim();
        if (meeting is not null)
        {
            var hosts = config.GetSection("Directory:MeetingHosts").Get<string[]>() is { Length: > 0 } c ? c : UrlRules.DefaultMeetingHosts;
            if (!UrlRules.IsAcceptable(meeting, out var host) || !UrlRules.HostAllowed(host, hosts))
                errors["meetingUrl"] = ["an https URL on an allowed meeting host, at most 300 characters"];
        }

        var techs = r.TechSlugs ?? [];
        if (techs.Count > MaxTechs) errors["techSlugs"] = ["at most 4"];
        else if (techs.Distinct().Count() != techs.Count) errors["techSlugs"] = ["no duplicates"];

        var wanted = techs.Append(r.ProblemSlug ?? "").Distinct().ToList();
        var active = await db.Categories.AsNoTracking().Where(x => x.IsActive && wanted.Contains(x.Slug)).Select(x => new { x.Slug, x.Kind }).ToListAsync(ct);
        if (!active.Any(x => x.Slug == r.ProblemSlug && x.Kind == CategoryKind.Problem)) errors["problemSlug"] = ["an active problem category"];
        if (!errors.ContainsKey("techSlugs") && techs.Any(t => !active.Any(x => x.Slug == t && x.Kind == CategoryKind.Tech)))
            errors["techSlugs"] = ["each an active tech category"];

        if (errors.Count > 0) return Problem(422, ErrorCodes.ValidationFailed, "Validation failed", errors);
        return contact.ContainsContactDetails(title) || contact.ContainsContactDetails(description)
            ? Problem(422, ErrorCodes.ContactDetailsNotAllowed, "Contact details are not allowed in the offering text")
            : null;
    }
}
