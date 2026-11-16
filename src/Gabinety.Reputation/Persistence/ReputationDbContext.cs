using Gabinety.Contracts.Reputation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gabinety.Reputation.Persistence;

/// <summary>Owns reputationdb (P3): reviews, rating_summaries, moderation_events (CONTRACTS 5.1). Enums are stored as strings.</summary>
public sealed class ReputationDbContext(DbContextOptions<ReputationDbContext> options) : DbContext(options)
{
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<RatingSummary> RatingSummaries => Set<RatingSummary>();
    public DbSet<ModerationEvent> ModerationEvents => Set<ModerationEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReputationDbContext).Assembly);
}

sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        b.ToTable("reviews");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.BookingId).HasColumnName("booking_id");
        b.Property(x => x.PractitionerId).HasColumnName("practitioner_id");
        b.Property(x => x.ClientUserId).HasColumnName("client_user_id").IsRequired();
        b.Property(x => x.Rating).HasColumnName("rating");
        b.Property(x => x.Text).HasColumnName("text").HasMaxLength(2000).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.HoldReasons).HasColumnName("hold_reasons"); // List<string> is a text[] on PostgreSQL
        b.Property(x => x.TriageFlag).HasColumnName("triage_flag").HasMaxLength(16)
            .HasConversion(v => v.ToString().ToLowerInvariant(), s => Enum.Parse<TriageFlag>(s, true)); // none|hold|unavailable
        b.Property(x => x.TriageModelId).HasColumnName("triage_model_id");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.PublishedAt).HasColumnName("published_at");
        b.Property(x => x.ModeratedBy).HasColumnName("moderated_by");
        b.Property(x => x.ModerationNote).HasColumnName("moderation_note");
        b.Property(x => x.ReplyText).HasColumnName("reply_text").HasMaxLength(1000);
        b.Property(x => x.RepliedAt).HasColumnName("replied_at");
        b.ToTable(t => t.HasCheckConstraint("ck_reviews_rating", "rating between 1 and 5"));
        b.HasIndex(x => x.BookingId).IsUnique().HasDatabaseName("ux_reviews_booking_id");
        b.HasIndex(x => new { x.PractitionerId, x.Status, x.PublishedAt, x.Id }).IsDescending(false, false, true, false)
            .HasDatabaseName("ix_reviews_practitioner_status_published");
    }
}

sealed class RatingSummaryConfiguration : IEntityTypeConfiguration<RatingSummary>
{
    public void Configure(EntityTypeBuilder<RatingSummary> b)
    {
        b.ToTable("rating_summaries");
        b.HasKey(x => x.PractitionerId);
        b.Property(x => x.PractitionerId).HasColumnName("practitioner_id").ValueGeneratedNever();
        b.Property(x => x.Count).HasColumnName("count");
        b.Property(x => x.Sum).HasColumnName("sum");
        b.Property(x => x.Version).HasColumnName("version");
        b.Property(x => x.PushedVersion).HasColumnName("pushed_version");
    }
}

sealed class ModerationEventConfiguration : IEntityTypeConfiguration<ModerationEvent>
{
    public void Configure(EntityTypeBuilder<ModerationEvent> b)
    {
        b.ToTable("moderation_events");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ReviewId).HasColumnName("review_id");
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Actor).HasColumnName("actor").IsRequired();
        b.Property(x => x.Note).HasColumnName("note");
        b.Property(x => x.At).HasColumnName("at");
        b.HasIndex(x => x.ReviewId).HasDatabaseName("ix_moderation_events_review_id");
    }
}

/// <summary>Only for `dotnet ef`; never used at runtime and never opens a connection.</summary>
public sealed class ReputationDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<ReputationDbContext>
{
    public ReputationDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ReputationDbContext>()
        .UseNpgsql("Host=localhost;Database=design-time", o => o.MigrationsAssembly(typeof(ReputationDbContext).Assembly.GetName().Name)).Options);
}
