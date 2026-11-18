using Gabinety.Contracts.Directory;
using Gabinety.Directory.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Directory.Features.Taxonomy;

/// <summary>GET /api/v1/taxonomy (CONTRACTS 3.2, anonymous) and the seeder that fills `categories` (CONTRACTS 2).</summary>
public sealed class TaxonomyFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton<ITaxonomySeedSource, EmbeddedTaxonomySeedSource>();
        builder.Services.AddSingleton<TaxonomySeeder>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<TaxonomySeeder>());
    }

    public void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGet("/api/v1/taxonomy", Get).AllowAnonymous();

    static async Task<IResult> Get(DirectoryDbContext db, HttpContext ctx, CancellationToken ct)
    {
        var active = await db.Categories.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Sort).ThenBy(c => c.Slug).ToListAsync(ct);
        var version = await db.TaxonomyMeta.AsNoTracking().Select(m => m.Version).SingleOrDefaultAsync(ct);
        if (version is not null) ctx.Response.Headers.CacheControl = "public, max-age=300"; // not cached while the seed has not run yet
        List<CategoryDto> Of(CategoryKind k) => active.Where(c => c.Kind == k).Select(c => new CategoryDto(c.Slug, c.NamePl)).ToList();
        return Results.Ok(new TaxonomyDto(version ?? "", Of(CategoryKind.Problem), Of(CategoryKind.Tech)));
    }
}
