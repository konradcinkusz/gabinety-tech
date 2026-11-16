using Gabinety.Directory.Features.Taxonomy;
using Gabinety.Directory.Persistence;
using Gabinety.Directory.Tests.Profiles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gabinety.Directory.Tests.Taxonomy;

/// <summary>The seed set of CONTRACTS 2, written out independently of the embedded JSON so a typo in either is caught.</summary>
public static class Expected
{
    public static readonly (string Slug, string NamePl)[] Problems =
    [
        ("audyt-kodu", "Audyt kodu"), ("migracja-legacy-dotnet", "Migracja legacy .NET"), ("integracja-ai", "Integracja AI"),
        ("automatyzacja-procesow", "Automatyzacja procesów"), ("architektura-systemu", "Architektura systemu"), ("code-review", "Code review"),
        ("wycena-projektu", "Wycena i plan projektu"), ("rozwiazanie-awarii", "Rozwiązanie awarii"),
    ];

    public static readonly (string Slug, string NamePl)[] Techs =
    [
        ("dotnet", ".NET"), ("csharp", "C#"), ("aspnet-core", "ASP.NET Core"), ("entity-framework", "Entity Framework"), ("azure", "Azure"),
        ("sql-server", "SQL Server"), ("postgresql", "PostgreSQL"), ("python", "Python"), ("llm", "LLM i agenci"), ("kubernetes", "Kubernetes"),
        ("react", "React"), ("typescript", "TypeScript"),
    ];

    public const string Version = "2026-11-v1";

    public static TaxonomySeed Set(string version, params string[] without)
    {
        IEnumerable<SeedCategory> all = Problems.Select((p, i) => new SeedCategory(p.Slug, "problem", p.NamePl, i + 1))
            .Concat(Techs.Select((t, i) => new SeedCategory(t.Slug, "tech", t.NamePl, i + 1)));
        return new(version, all.Where(c => !without.Contains(c.Slug)).ToList());
    }
}

public sealed class SwappableSource : ITaxonomySeedSource
{
    public TaxonomySeed? Override;
    readonly EmbeddedTaxonomySeedSource _embedded = new();
    public TaxonomySeed Load() => Override ?? _embedded.Load();
}

public sealed class ListLogger<T> : ILogger<T>
{
    public List<LogLevel> Levels { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
}

/// <summary>All taxonomy tests share one in-memory database (its name is fixed by PersistenceFeature), so they run serially and each one seeds the set it needs first.</summary>
public sealed class TaxonomyFactory : ProfileFactory
{
    public SwappableSource Source { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.AddSingleton<ITaxonomySeedSource>(Source));
    }

    public async Task<int> SeedAsync(TaxonomySeed? set = null, bool clean = true)
    {
        var seeder = Services.GetRequiredService<TaxonomySeeder>();
        await seeder.Completed.WaitAsync(TimeSpan.FromSeconds(30)); // the host's own run is finished, so ours is the only writer
        var known = Expected.Problems.Concat(Expected.Techs).Select(x => x.Slug).ToList();
        if (clean) await WithDb(async db => // rows a previous test added are not part of this test's starting point
        {
            db.Categories.RemoveRange(db.Categories.Where(c => !known.Contains(c.Slug)));
            return await db.SaveChangesAsync();
        });
        Source.Override = set;
        return await seeder.SeedOnceAsync(CancellationToken.None);
    }

    public async Task<T> WithDb<T>(Func<DirectoryDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DirectoryDbContext>());
    }
}

[CollectionDefinition("taxonomy", DisableParallelization = true)]
public sealed class TaxonomyCollection : ICollectionFixture<TaxonomyFactory>;
