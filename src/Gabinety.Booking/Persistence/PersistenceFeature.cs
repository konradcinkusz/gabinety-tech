using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Persistence;

/// <summary>BookingDbContext, provider switch (Database:Provider, CONTRACTS 10.3) and the post-start migration service (P4).</summary>
public sealed class PersistenceFeature : IServiceFeature
{
    public const string InMemoryName = "bookingdb";

    public void Add(IHostApplicationBuilder builder)
    {
        // Configuration is read when the context is first built, so a host that overrides it late still wins.
        builder.Services.AddDbContext<BookingDbContext>((sp, o) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var cs = config.GetConnectionString("bookingdb");
            var provider = config["Database:Provider"];
            if (string.IsNullOrWhiteSpace(cs) || string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
                o.UseInMemoryDatabase(InMemoryName);
            else
                o.UseNpgsql(cs, n => n.EnableRetryOnFailure().MigrationsAssembly(typeof(BookingDbContext).Assembly.GetName().Name));
        });
        builder.Services.AddSingleton<MigrationState>();
        builder.Services.AddHostedService<MigrationHostedService>();
    }

    public void Map(IEndpointRouteBuilder endpoints) { }
}
