using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Directory.Features.Offerings;
using Gabinety.Directory.Features.Profiles;
using Gabinety.Directory.Persistence;
using Gabinety.Directory.Tests.Verification;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Directory.Tests.Internal;

/// <summary>Directory host with the internal key configured. Tests filter by their own random ids: the in-memory database is shared by name.</summary>
public class InternalFactory : VerificationFactory
{
    public const string Key = "demo-internal-key-0001";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Internal:Key", Key);
    }

    public HttpClient Anon() => CreateClient();

    public HttpClient Internal(string? key = Key)
    {
        var c = CreateClient();
        if (key is not null) c.DefaultRequestHeaders.Add("X-Internal-Key", key);
        return c;
    }

    public Task<Offering> SeedOffering(Practitioner p, string? meetingUrl = null, bool active = true, long price = 15000, short minutes = 60) => Db(async db =>
    {
        var o = new Offering
        {
            Id = Guid.NewGuid(), PractitionerId = p.Id, Title = "Demo review", Description = "demo description", DurationMinutes = minutes, PriceMinor = price,
            Currency = "PLN", ProblemSlug = "code-review", TechSlugs = ["dotnet"], MeetingUrl = meetingUrl, IsActive = active, CreatedAt = Clock.GetUtcNow(),
        };
        db.Offerings.Add(o);
        await db.SaveChangesAsync();
        return o;
    });

    public Task<List<Offering>> OfferingsOf(Guid practitionerId) => Db(db => db.Offerings.AsNoTracking().Where(o => o.PractitionerId == practitionerId).ToListAsync());
}

public static class Ik
{
    public static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    public static Task<HttpResponseMessage> PutReputation(HttpClient c, Guid id, int count, double avg, long version) =>
        c.PutAsJsonAsync($"/internal/v1/practitioners/{id}/reputation", new { ratingCount = count, ratingAvg = avg, version }, J);
    public static Task<HttpResponseMessage> PutStats(HttpClient c, Guid id, int completed, int cancelled, int noShow, int open, long version) =>
        c.PutAsJsonAsync($"/internal/v1/practitioners/{id}/booking-stats",
            new { completedCount = completed, practitionerCancelCount = cancelled, noShowUpheldCount = noShow, openSlots14d = open, version }, J);
}
