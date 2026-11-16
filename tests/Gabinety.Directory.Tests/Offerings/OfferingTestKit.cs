using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Profiles;
using Gabinety.Directory.Features.Taxonomy;
using Gabinety.Directory.Persistence;
using Gabinety.Directory.Tests.Profiles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Directory.Tests.Offerings;

/// <summary>Shared helpers: synthetic users, a valid request, problem reading, and a host that waits for its own taxonomy seed.</summary>
public static class Kit
{
    public static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    public const string Url = "/api/v1/me/practitioner/offerings";

    public static string Id() => "demo-" + Guid.NewGuid().ToString("n");

    public static OfferingRequest Valid(string title = "Przegląd kodu .NET", string description = "Review jednego modułu aplikacji",
        int duration = 30, long price = 10000, string problem = "code-review", string[]? techs = null, string? meeting = null) =>
        new(title, description, duration, price, problem, techs ?? ["dotnet"], meeting);

    public static async Task<HttpClient> Practitioner(ProfileFactory f, string? sub = null)
    {
        var c = f.ClientFor(sub ?? Id());
        var r = await c.PostAsJsonAsync("/api/v1/me/practitioner", new CreatePractitionerRequest("Demo " + Guid.NewGuid().ToString("n")[..12]));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return c;
    }

    public static async Task<ApiProblem> ProblemOf(HttpResponseMessage r)
    {
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        return (await r.Content.ReadFromJsonAsync<ApiProblem>(J))!;
    }

    public static async Task<OfferingPrivate> Created(HttpClient c, OfferingRequest req)
    {
        var r = await c.PostAsJsonAsync(Url, req);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<OfferingPrivate>(J))!;
    }

    public static async Task<List<OfferingPrivate>> List(HttpClient c) =>
        (await c.GetFromJsonAsync<ItemsDto>(Url, J))!.Items;

    public sealed record ItemsDto(List<OfferingPrivate> Items);

    public static async Task<decimal> Completeness(ProfileFactory f, string userId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();
        return (await db.Practitioners.AsNoTracking().SingleAsync(p => p.UserId == userId)).ProfileCompleteness;
    }

    public static async Task WaitForSeed(ProfileFactory f)
    {
        f.CreateClient();
        await f.Services.GetRequiredService<TaxonomySeeder>().Completed.WaitAsync(TimeSpan.FromSeconds(60));
    }
}

/// <summary>Directory host without the per-user rate limit (these tests make far more than 60 calls a minute from one address).</summary>
public class OfferingFactory : ProfileFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.PostConfigure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(o => o.GlobalLimiter = null));
    }

    /// <summary>Adds inactive categories of their own, so no test changes the seeded ones.</summary>
    public async Task AddInactiveCategoriesAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();
        if (await db.Categories.AnyAsync(c => c.Slug == "demo-inactive-problem")) return;
        db.Categories.Add(new Category { Slug = "demo-inactive-problem", Kind = CategoryKind.Problem, NamePl = "Demo nieaktywny", Sort = 90, IsActive = false });
        db.Categories.Add(new Category { Slug = "demo-inactive-tech", Kind = CategoryKind.Tech, NamePl = "Demo nieaktywny", Sort = 90, IsActive = false });
        await db.SaveChangesAsync();
    }
}

/// <summary>Host with the price limits changed through configuration.</summary>
public sealed class PriceConfigFactory : OfferingFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Directory:Price:MinMinor", "6000");
        builder.UseSetting("Directory:Price:MaxMinor", "9000");
    }
}
