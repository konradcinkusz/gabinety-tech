using Gabinety.Directory.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Directory.Features.Taxonomy;

/// <summary>
/// Applies the versioned seed set (P4: reference data is seeded, never HasData) once the migration service has finished.
/// Upserts by slug, deactivates slugs that left the set (never deletes), stores the set's version. Idempotent; never throws out of the host.
/// </summary>
public sealed class TaxonomySeeder(IServiceProvider services, ITaxonomySeedSource source, MigrationState state, ILogger<TaxonomySeeder> log) : BackgroundService
{
    static readonly TimeSpan MigrationWait = TimeSpan.FromMinutes(5);
    readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the host's own seeding run has finished, whatever its outcome.</summary>
    public Task Completed => _completed.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await state.WaitForCompletionAsync(MigrationWait).WaitAsync(stoppingToken);
            if (state.Status != MigrationStatus.Done) { log.LogWarning("Taxonomy seeding skipped: migrations did not finish"); return; }
            var changes = await SeedOnceAsync(stoppingToken);
            log.LogInformation("Taxonomy seeded, {Changes} row changes", changes);
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (TimeoutException) { log.LogWarning("Taxonomy seeding skipped: migrations did not finish in time"); }
        catch (Exception ex) { log.LogError(ex, "Taxonomy seeding failed"); }
        finally { _completed.TrySetResult(); }
    }

    /// <summary>One transactional pass (a single SaveChanges). Returns the number of rows written: 0 when the database already matches the set.</summary>
    public async Task<int> SeedOnceAsync(CancellationToken ct)
    {
        var seed = source.Load();
        for (var attempt = 1; ; attempt++)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();
            try { return await Apply(db, seed, ct); }
            catch (DbUpdateException) when (attempt == 1) { /* another replica seeded at the same moment: look again once */ }
        }
    }

    static async Task<int> Apply(DirectoryDbContext db, TaxonomySeed seed, CancellationToken ct)
    {
        var rows = await db.Categories.ToDictionaryAsync(c => c.Slug, ct);
        foreach (var s in seed.Categories)
        {
            var kind = s.Kind == "problem" ? CategoryKind.Problem : s.Kind == "tech" ? CategoryKind.Tech : throw new InvalidOperationException($"Unknown kind '{s.Kind}'");
            if (!rows.TryGetValue(s.Slug, out var row)) { db.Categories.Add(new Category { Slug = s.Slug, Kind = kind, NamePl = s.NamePl, Sort = s.Sort, IsActive = true }); continue; }
            row.Kind = kind; row.NamePl = s.NamePl; row.Sort = s.Sort; row.IsActive = true; // assigning an equal value marks nothing modified
        }
        var inSeed = seed.Categories.Select(c => c.Slug).ToHashSet();
        foreach (var row in rows.Values.Where(r => !inSeed.Contains(r.Slug))) row.IsActive = false;

        var meta = await db.TaxonomyMeta.SingleOrDefaultAsync(ct);
        if (meta is null) db.TaxonomyMeta.Add(new TaxonomyMeta { Version = seed.Version });
        else meta.Version = seed.Version;

        return await db.SaveChangesAsync(ct);
    }
}
