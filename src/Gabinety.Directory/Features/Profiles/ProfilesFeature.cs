using System.Security.Claims;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Directory.Features.Profiles;

/// <summary>/api/v1/me/practitioner: create, read, replace the editable fields, submit for review (CONTRACTS 3.3). Each user sees only the row whose user_id is their token sub.</summary>
public sealed class ProfilesFeature : IServiceFeature
{
    const string Route = "/api/v1/me/practitioner";
    const int SlugAttempts = 5;

    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<INipValidator, NipChecksumValidator>();
        builder.Services.AddSingleton<IContactDetailDetector, ContactDetailDetector>();
        builder.Services.TryAddSingleton<IChecklistFacts, NoOfferingsYet>(); // the offerings feature registers the real one; whichever feature is added first, it wins
#pragma warning disable EXTEXP0001 // the kernel adds the standard resilience handler (with retries) to every client; this one must not retry
        builder.Services.AddHttpClient<IEmailVerifiedLookup, HttpEmailVerifiedLookup>(c => c.Timeout = HttpEmailVerifiedLookup.Timeout)
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
    }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var g = endpoints.MapGroup(Route).RequireAuthorization("Authenticated");
        g.MapPost("", Create);
        g.MapGet("", Get);
        g.MapPut("", Update);
        g.MapPost("submit", Submit);
    }

    static IResult Problem(int status, string code, string title, Dictionary<string, string[]>? errors = null) =>
        Results.Json(ApiProblem.Of(code, title, status, null, errors), statusCode: status, contentType: "application/problem+json");

    static IResult Invalid(string field, string message) =>
        Problem(422, ErrorCodes.ValidationFailed, "Validation failed", new() { [field] = [message] });

    static string? UserId(ClaimsPrincipal u) => u.FindFirst("sub")?.Value ?? u.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary>Keeps the e-mail snapshot of the token (<see cref="Practitioner.ContactEmail"/>) current. A token without a usable address leaves the stored one alone. Does not save.</summary>
    static void Snapshot(Practitioner p, ClaimsPrincipal user)
    {
        if (UserEmail.From(user) is { } e && e != p.ContactEmail) p.ContactEmail = e;
    }

    static string? Bearer(HttpContext ctx) =>
        ctx.Request.Headers.Authorization.ToString() is { } h && h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? h[7..].Trim() : null;

    static async Task<PractitionerPrivate> View(Practitioner p, HttpContext ctx, IEmailVerifiedLookup email, CancellationToken ct) =>
        new(p.Id, p.Slug, p.Status, p.StatusReason, p.DisplayName, p.Headline, p.Bio, p.PortfolioUrl, p.MeetingUrlDefault, p.Nip,
            Bearer(ctx) is { } t && await email.IsVerifiedAsync(t, ct), p.GithubLinked, p.NipValid, p.GithubLogin);

    static bool ValidName(string? name, out string trimmed)
    {
        trimmed = name?.Trim() ?? "";
        return trimmed.Length is >= 2 and <= 80 && !trimmed.Any(char.IsControl);
    }

    static async Task<IResult> Create(CreatePractitionerRequest req, ClaimsPrincipal user, HttpContext ctx, DirectoryDbContext db,
        IEmailVerifiedLookup email, TimeProvider time, CancellationToken ct)
    {
        if (UserId(user) is not { } uid) return Results.Unauthorized();
        if (!ValidName(req.DisplayName, out var name)) return Invalid("displayName", "2 to 80 characters, no control characters");
        if (req.Slug is not null && !SlugGenerator.IsValid(req.Slug)) return Invalid("slug", "3 to 60 characters of a-z, 0-9 and single hyphens");
        if (await db.Practitioners.AnyAsync(x => x.UserId == uid, ct)) return Problem(409, ErrorCodes.PractitionerExists, "Practitioner exists");

        var baseSlug = req.Slug ?? SlugGenerator.FromName(name);
        var now = time.GetUtcNow();
        for (var n = 1; n <= SlugAttempts + 1; n++)
        {
            var slug = SlugGenerator.WithSuffix(baseSlug, n);
            if (await db.Practitioners.AnyAsync(x => x.Slug == slug, ct)) { continue; }
            var p = new Practitioner { Id = Guid.NewGuid(), UserId = uid, Slug = slug, DisplayName = name, ContactEmail = UserEmail.From(user), Status = PractitionerStatus.Draft, CreatedAt = now, UpdatedAt = now };
            db.Practitioners.Add(p);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                // The unique indexes decide a race: another request took the user or the slug between our check and the insert.
                db.Entry(p).State = EntityState.Detached;
                if (await db.Practitioners.AnyAsync(x => x.UserId == uid, ct)) return Problem(409, ErrorCodes.PractitionerExists, "Practitioner exists");
                continue;
            }
            return Results.Created(Route, await View(p, ctx, email, ct));
        }
        return Problem(409, ErrorCodes.PractitionerExists, "Slug could not be allocated, try again"); // practically unreachable
    }

    static async Task<IResult> Get(ClaimsPrincipal user, HttpContext ctx, DirectoryDbContext db, IEmailVerifiedLookup email, CancellationToken ct)
    {
        if (UserId(user) is not { } uid) return Results.Unauthorized();
        var p = await db.Practitioners.SingleOrDefaultAsync(x => x.UserId == uid, ct);
        if (p is null) return Problem(404, ErrorCodes.NotFound, "Not found");
        Snapshot(p, user); // the dashboard loads this on sign-in: the address follows a change made in authservice
        if (db.Entry(p).State == EntityState.Modified) await db.SaveChangesAsync(ct);
        return Results.Ok(await View(p, ctx, email, ct));
    }

    static async Task<IResult> Update(UpdatePractitionerRequest req, ClaimsPrincipal user, HttpContext ctx, DirectoryDbContext db,
        IEmailVerifiedLookup email, IChecklistFacts facts, INipValidator nips, IContactDetailDetector contact, IConfiguration config,
        TimeProvider time, CancellationToken ct)
    {
        if (UserId(user) is not { } uid) return Results.Unauthorized();
        var p = await db.Practitioners.SingleOrDefaultAsync(x => x.UserId == uid, ct);
        if (p is null) return Problem(404, ErrorCodes.NotFound, "Not found");

        var errors = new Dictionary<string, string[]>();
        if (!ValidName(req.DisplayName, out var name)) errors["displayName"] = ["2 to 80 characters, no control characters"];
        var headline = req.Headline?.Trim() ?? "";
        var bio = req.Bio?.Trim() ?? "";
        if (headline.Length is < 10 or > 120) errors["headline"] = ["10 to 120 characters"];
        if (bio.Length > 2000) errors["bio"] = ["at most 2000 characters"];
        var portfolio = Blank(req.PortfolioUrl);
        var meeting = Blank(req.MeetingUrlDefault);
        if (portfolio is not null && !UrlRules.IsAcceptable(portfolio, out _)) errors["portfolioUrl"] = ["an https URL on a named host, at most 300 characters"];
        if (meeting is not null)
        {
            var hosts = config.GetSection("Directory:MeetingHosts").Get<string[]>() is { Length: > 0 } c ? c : UrlRules.DefaultMeetingHosts;
            if (!UrlRules.IsAcceptable(meeting, out var host) || !UrlRules.HostAllowed(host, hosts))
                errors["meetingUrlDefault"] = ["an https URL on an allowed meeting host, at most 300 characters"];
        }
        var nip = Blank(req.Nip);
        if (nip is not null && !nips.IsValid(nip)) errors["nip"] = ["ten digits with a valid checksum"];
        if (errors.Count > 0) return Problem(422, ErrorCodes.ValidationFailed, "Validation failed", errors);
        if (contact.ContainsContactDetails(headline) || contact.ContainsContactDetails(bio))
            return Problem(422, ErrorCodes.ContactDetailsNotAllowed, "Contact details are not allowed in the profile text");

        var nipChanged = nip != p.Nip;
        if (nipChanged && p.Status == PractitionerStatus.Approved &&
            PractitionerStateMachine.TryTransition(p.Status, PractitionerTrigger.NipChanged, out var next))
        {
            p.Status = next;
            Verification.VerificationEvents.Record(db, p.Id, Verification.VerificationEventKind.NipChanged, uid, null, time);
        }
        p.DisplayName = name; p.Headline = headline; p.Bio = bio; p.PortfolioUrl = portfolio; p.MeetingUrlDefault = meeting;
        p.Nip = nip; p.NipValid = nip is not null; // a stored NIP has passed the checksum
        Snapshot(p, user);
        p.ProfileCompleteness = ProfileCompleteness.Compute(headline.Length > 0, bio.Length >= 200,
            await facts.ActiveOfferingCountAsync(p.Id, ct) > 0, p.GithubLinked, portfolio is not null, p.NipValid, p.OpenSlots14d > 0);
        Ranking.RankingRecalculator.Recompute(p);
        p.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Results.Ok(await View(p, ctx, email, ct));
    }

    static async Task<IResult> Submit(ClaimsPrincipal user, HttpContext ctx, DirectoryDbContext db, IEmailVerifiedLookup email,
        IChecklistFacts facts, TimeProvider time, CancellationToken ct)
    {
        if (UserId(user) is not { } uid) return Results.Unauthorized();
        var p = await db.Practitioners.SingleOrDefaultAsync(x => x.UserId == uid, ct);
        if (p is null) return Problem(404, ErrorCodes.NotFound, "Not found");
        if (!PractitionerStateMachine.TryTransition(p.Status, PractitionerTrigger.Submit, out var next))
            return Problem(409, ErrorCodes.InvalidTransition, "Invalid transition");

        var unmet = new List<string>();
        if (p.Headline.Length == 0) unmet.Add("headline");
        if (p.Bio.Length < 200) unmet.Add("bio");
        if (await facts.ActiveOfferingCountAsync(p.Id, ct) < 1) unmet.Add("offering");
        if (!p.GithubLinked) unmet.Add("github_linked");
        if (!p.NipValid) unmet.Add("nip_valid");
        if (p.PortfolioUrl is null) unmet.Add("portfolioUrl");
        if (!(Bearer(ctx) is { } t && await email.IsVerifiedAsync(t, ct))) unmet.Add("email_verified");
        if (unmet.Count > 0)
            return Results.Json(new { type = "urn:gabinety:error:submit_incomplete", title = "Profile is incomplete", status = 422,
                code = ErrorCodes.SubmitIncomplete, unmetItems = new SubmitIncomplete(unmet).UnmetItems }, statusCode: 422, contentType: "application/problem+json");

        Snapshot(p, user);
        p.Status = next; p.StatusReason = null; p.UpdatedAt = time.GetUtcNow();
        Verification.VerificationEvents.Record(db, p.Id, Verification.VerificationEventKind.Submitted, uid, null, time);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
