using Microsoft.EntityFrameworkCore;

namespace Gabinety.Directory.Persistence;

/// <summary>Owns directorydb (P3). Entities and their IEntityTypeConfiguration classes arrive with T-03.02 onward.</summary>
public sealed class DirectoryDbContext(DbContextOptions<DirectoryDbContext> options) : DbContext(options)
{
    public DbSet<Features.Profiles.Practitioner> Practitioners => Set<Features.Profiles.Practitioner>();
    public DbSet<Features.Offerings.Offering> Offerings => Set<Features.Offerings.Offering>();
    public DbSet<Features.Taxonomy.Category> Categories => Set<Features.Taxonomy.Category>();
    public DbSet<Features.Taxonomy.TaxonomyMeta> TaxonomyMeta => Set<Features.Taxonomy.TaxonomyMeta>();

    public DbSet<Features.Verification.VerificationEvent> VerificationEvents => Set<Features.Verification.VerificationEvent>();
    public DbSet<Features.Verification.Moderator> Moderators => Set<Features.Verification.Moderator>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DirectoryDbContext).Assembly);
}

/// <summary>Only for `dotnet ef`; never used at runtime and never opens a connection.</summary>
public sealed class DirectoryDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<DirectoryDbContext>
{
    public DirectoryDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<DirectoryDbContext>()
        .UseNpgsql("Host=localhost;Database=design-time", o => o.MigrationsAssembly(typeof(DirectoryDbContext).Assembly.GetName().Name)).Options);
}
