using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Assistant.Persistence;

/// <summary>AssistantDbContext, provider switch (Database:Provider, CONTRACTS 10.3) and the post-start migration service (P4).</summary>
public sealed class PersistenceFeature : IServiceFeature
{
    public const string InMemoryName = "assistantdb";

    public void Add(IHostApplicationBuilder builder)
    {
        // Configuration is read when the context is first built, so a host that overrides it late still wins.
        builder.Services.AddDbContext<AssistantDbContext>((sp, o) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var cs = config.GetConnectionString("assistantdb");
            var provider = config["Database:Provider"];
            if (string.IsNullOrWhiteSpace(cs) || string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
                o.UseInMemoryDatabase(InMemoryName);
            else
                o.UseNpgsql(cs, n => n.EnableRetryOnFailure().MigrationsAssembly(typeof(AssistantDbContext).Assembly.GetName().Name));
        });
        builder.Services.AddSingleton<MigrationState>();
        builder.Services.AddHostedService<MigrationHostedService>();
    }

    public void Map(IEndpointRouteBuilder endpoints) { }
}
