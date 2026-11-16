using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gabinety.Assistant.Persistence;

/// <summary>
/// One usage record per model call (CONTRACTS 6). No prompt and no reply is stored: the only text columns are short
/// enumerations, the model id and a salted user hash. Enums are stored as strings (the wire values).
/// </summary>
public sealed class UsageRow
{
    public Guid Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Feature { get; set; } = "";        // brief | review-triage
    public string ModelId { get; set; } = "";
    public string ProviderKind { get; set; } = "";   // copilot | custom | fake
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public decimal? AiCredits { get; set; }
    public decimal? UsdEstimate { get; set; }        // null = unknown, never zero
    public int LatencyMs { get; set; }
    public string Outcome { get; set; } = "";        // ok | invalid_output | timeout | unavailable | over_budget | policy_refused
    public string? UserHash { get; set; }
}

/// <summary>Owns assistantdb (P3): assistant_usage.</summary>
public sealed class AssistantDbContext(DbContextOptions<AssistantDbContext> options) : DbContext(options)
{
    public DbSet<UsageRow> Usage => Set<UsageRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AssistantDbContext).Assembly);
}

sealed class UsageRowConfiguration : IEntityTypeConfiguration<UsageRow>
{
    public void Configure(EntityTypeBuilder<UsageRow> b)
    {
        b.ToTable("assistant_usage");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.At).HasColumnName("at");
        b.Property(x => x.Feature).HasColumnName("feature").HasMaxLength(16).IsRequired();
        b.Property(x => x.ModelId).HasColumnName("model_id").HasMaxLength(80).IsRequired();
        b.Property(x => x.ProviderKind).HasColumnName("provider_kind").HasMaxLength(16).IsRequired();
        b.Property(x => x.InputTokens).HasColumnName("input_tokens");
        b.Property(x => x.OutputTokens).HasColumnName("output_tokens");
        b.Property(x => x.AiCredits).HasColumnName("ai_credits");
        b.Property(x => x.UsdEstimate).HasColumnName("usd_estimate");
        b.Property(x => x.LatencyMs).HasColumnName("latency_ms");
        b.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(16).IsRequired();
        b.Property(x => x.UserHash).HasColumnName("user_hash").HasMaxLength(16);
        b.HasIndex(x => x.At).HasDatabaseName("ix_assistant_usage_at");
        b.HasIndex(x => new { x.Feature, x.At }).HasDatabaseName("ix_assistant_usage_feature_at");
    }
}

/// <summary>Only for `dotnet ef`; never used at runtime and never opens a connection.</summary>
public sealed class AssistantDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<AssistantDbContext>
{
    public AssistantDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AssistantDbContext>()
        .UseNpgsql("Host=localhost;Database=design-time", o => o.MigrationsAssembly(typeof(AssistantDbContext).Assembly.GetName().Name)).Options);
}
