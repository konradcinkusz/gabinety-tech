using Gabinety.Contracts.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gabinety.Directory.Features.Profiles;

/// <summary>Row of `practitioners` (CONTRACTS 3.1). Later tasks fill the rating, reputation, stats and ranking columns.</summary>
public sealed class Practitioner
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Slug { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Headline { get; set; } = "";
    public string Bio { get; set; } = "";
    public string? GithubLogin { get; set; }
    public bool GithubLinked { get; set; }
    public string? Nip { get; set; }
    public bool NipValid { get; set; }
    public string? PortfolioUrl { get; set; }
    public string? MeetingUrlDefault { get; set; }
    public PractitionerStatus Status { get; set; }
    public string? StatusReason { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int RatingCount { get; set; }
    public decimal RatingAvg { get; set; }
    public long ReputationVersion { get; set; }
    public int CompletedCount { get; set; }
    public int PractitionerCancelCount { get; set; }
    public int NoShowUpheldCount { get; set; }
    public int OpenSlots14d { get; set; }
    public long StatsVersion { get; set; }
    public decimal ProfileCompleteness { get; set; }
    public decimal RankingScore { get; set; }
    public string RankingVersion { get; set; } = "";
    /// <summary>
    /// The practitioner's e-mail, copied from the access token they used when they created or last used their profile (T-03.09). Booking asks for it
    /// (<c>GET /internal/v1/practitioners/{userId}/contact</c>) to send notifications. Personal data: never public, never logged, nulled by erasure.
    /// </summary>
    public string? ContactEmail { get; set; }
    public string? GithubChallengeCode { get; set; }
    public string? GithubChallengeLogin { get; set; }
    public DateTimeOffset? GithubChallengeExpiresAt { get; set; }
}

public sealed class PractitionerConfiguration : IEntityTypeConfiguration<Practitioner>
{
    public void Configure(EntityTypeBuilder<Practitioner> b)
    {
        b.ToTable("practitioners");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.UserId).HasColumnName("user_id").HasMaxLength(64).IsRequired();
        b.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(60).IsRequired();
        b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(80).IsRequired();
        b.Property(x => x.Headline).HasColumnName("headline").HasMaxLength(120).IsRequired();
        b.Property(x => x.Bio).HasColumnName("bio").HasMaxLength(2000).IsRequired();
        b.Property(x => x.GithubLogin).HasColumnName("github_login").HasMaxLength(39);
        b.Property(x => x.GithubLinked).HasColumnName("github_linked");
        b.Property(x => x.Nip).HasColumnName("nip").HasMaxLength(10);
        b.Property(x => x.NipValid).HasColumnName("nip_valid");
        b.Property(x => x.PortfolioUrl).HasColumnName("portfolio_url").HasMaxLength(300);
        b.Property(x => x.MeetingUrlDefault).HasColumnName("meeting_url_default").HasMaxLength(300);
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.StatusReason).HasColumnName("status_reason").HasMaxLength(500);
        b.Property(x => x.ApprovedAt).HasColumnName("approved_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.Property(x => x.RatingCount).HasColumnName("rating_count");
        b.Property(x => x.RatingAvg).HasColumnName("rating_avg").HasPrecision(3, 2);
        b.Property(x => x.ReputationVersion).HasColumnName("reputation_version");
        b.Property(x => x.CompletedCount).HasColumnName("completed_count");
        b.Property(x => x.PractitionerCancelCount).HasColumnName("practitioner_cancel_count");
        b.Property(x => x.NoShowUpheldCount).HasColumnName("no_show_upheld_count");
        b.Property(x => x.OpenSlots14d).HasColumnName("open_slots_14d");
        b.Property(x => x.StatsVersion).HasColumnName("stats_version");
        b.Property(x => x.ProfileCompleteness).HasColumnName("profile_completeness").HasPrecision(3, 2);
        b.Property(x => x.RankingScore).HasColumnName("ranking_score").HasPrecision(5, 2);
        b.Property(x => x.RankingVersion).HasColumnName("ranking_version").HasMaxLength(20).IsRequired();
        b.Property(x => x.ContactEmail).HasColumnName("contact_email").HasMaxLength(254);
        b.Property(x => x.GithubChallengeCode).HasColumnName("github_challenge_code").HasMaxLength(40);
        b.Property(x => x.GithubChallengeLogin).HasColumnName("github_challenge_login").HasMaxLength(39);
        b.Property(x => x.GithubChallengeExpiresAt).HasColumnName("github_challenge_expires_at");
        // ux on lower(github_login) where github_linked is raw SQL in migration T-03.05 (EF cannot model an expression index)
        b.HasIndex(x => x.UserId).IsUnique();
        b.HasIndex(x => x.Slug).IsUnique();
        b.HasIndex(x => new { x.Status, x.RankingScore, x.ApprovedAt, x.Id }).IsDescending(false, true, false, false); // for later: the public listing
    }
}
