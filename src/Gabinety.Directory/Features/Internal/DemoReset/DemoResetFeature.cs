using Gabinety.Directory.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Directory.Features.Internal.DemoReset;

/// <summary>
/// True for the profiles that are not a deployed environment (CONTRACTS 10.4): <c>GABINETY_FAKE=1</c> or <c>Environment:Kind=Development</c>.
/// Read from configuration, so the environment variable and a test setting behave the same. Same rule as Booking's.
/// </summary>
public static class NonDeployedProfile
{
    public static bool IsActive(IConfiguration config) =>
        config["GABINETY_FAKE"] == "1" || string.Equals(config["Environment:Kind"], "Development", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// POST /internal/v1/demo/reset (CONTRACTS 10.4): removes the Directory rows of the <c>demo-</c> namespace and returns the counts. Mapped only outside a
/// deployed environment. A practitioner is in the namespace when its SLUG starts with <c>demo-</c> (case-sensitive): the seeder's user ids are opaque
/// DevIdentity subjects. Its offerings and verification events go with it (the two tables with a practitioner foreign key; ratings, stats, badges and the
/// GitHub challenge are columns of the practitioner row). Moderators are keyed by user id, not by practitioner, and are not touched.
/// </summary>
public sealed class DemoResetFeature : IServiceFeature
{
    const string Ns = "demo-";

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        if (!NonDeployedProfile.IsActive(endpoints.ServiceProvider.GetRequiredService<IConfiguration>())) return;
        endpoints.MapPost("/internal/v1/demo/reset", ResetAsync).RequireAuthorization("Internal");
    }

    static async Task<IResult> ResetAsync(DirectoryDbContext db, CancellationToken ct)
    {
        // StartsWith is a case-sensitive prefix match on PostgreSQL (LIKE) and on the in-memory provider (ordinal).
        var practitioners = await db.Practitioners.Where(p => p.Slug.StartsWith(Ns)).ToListAsync(ct);
        var ids = practitioners.Select(p => p.Id).ToList();
        var events = await db.VerificationEvents.Where(e => ids.Contains(e.PractitionerId)).ToListAsync(ct);
        var offerings = await db.Offerings.Where(o => ids.Contains(o.PractitionerId)).ToListAsync(ct);

        db.RemoveRange(events);
        db.RemoveRange(offerings);
        db.RemoveRange(practitioners);
        await db.SaveChangesAsync(ct); // one unit of work: all or nothing; EF orders the deletes children first

        return Results.Ok(new { practitioners = practitioners.Count, offerings = offerings.Count, verificationEvents = events.Count });
    }
}
