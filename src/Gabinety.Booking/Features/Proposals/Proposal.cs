using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gabinety.Booking.Features.Proposals;

/// <summary>
/// A collaboration proposal between a client and a practitioner (ADR proposal 0013, T-13.02). Not a booking: no payment, no fee. The columns <c>Rate…StartDate</c> hold the CURRENT terms
/// (the latest round's), so a list row needs no join; the history is <see cref="ProposalRound"/>. Personal data: <see cref="ClientEmail"/> is a snapshot of the token (never in a response before
/// acceptance, never logged, nulled by erasure); <see cref="CompanyName"/> is free text the client chose to give (never logged).
/// </summary>
public sealed class Proposal
{
    public Guid Id { get; set; }
    public string ClientUserId { get; set; } = "";
    /// <summary>"Klient D.": what the practitioner sees instead of a name (ADR-0012 section 6). Null after an erasure.</summary>
    public string? ClientDisplayName { get; set; }
    public string? ClientEmail { get; set; }
    public string? CompanyName { get; set; }
    public Guid PractitionerId { get; set; }
    public string PractitionerUserId { get; set; } = "";
    public ProposalKind Kind { get; set; }
    public ProposalStatus Status { get; set; }
    /// <summary>The number of the last round. Also the concurrency token: two parties acting on the same round cannot both win.</summary>
    public int RoundCount { get; set; }
    public long RateMinor { get; set; }
    public int? HoursPerWeek { get; set; }
    public int? TotalHours { get; set; }
    public int? DurationMonths { get; set; }
    public DateOnly StartDate { get; set; }
    public Guid IdempotencyKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastActionAt { get; set; }
    /// <summary>When the awaiting party's time runs out; null once the proposal is closed.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public List<ProposalRound> Rounds { get; set; } = [];
}

/// <summary>One step of a negotiation; append-only. <see cref="Text"/> is the party's free text: never logged, anonymised by erasure.</summary>
public sealed class ProposalRound
{
    public Guid Id { get; set; }
    public Guid ProposalId { get; set; }
    public int Number { get; set; }
    public ProposalActor By { get; set; }
    public ProposalAction Action { get; set; }
    public long? RateMinor { get; set; }
    public int? HoursPerWeek { get; set; }
    public int? TotalHours { get; set; }
    public int? DurationMonths { get; set; }
    public DateOnly? StartDate { get; set; }
    public string? Text { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

sealed class ProposalConfiguration : IEntityTypeConfiguration<Proposal>
{
    /// <summary>The two states in which a proposal is open: one open proposal per client-practitioner pair.</summary>
    public const string OpenFilter = "status IN ('AwaitingPractitioner','AwaitingClient')";

    public void Configure(EntityTypeBuilder<Proposal> b)
    {
        b.ToTable("proposals");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.ClientUserId).HasColumnName("client_user_id").HasMaxLength(64).IsRequired();
        b.Property(x => x.ClientDisplayName).HasColumnName("client_display_name").HasMaxLength(60);
        b.Property(x => x.ClientEmail).HasColumnName("client_email").HasMaxLength(254);
        b.Property(x => x.CompanyName).HasColumnName("company_name").HasMaxLength(120);
        b.Property(x => x.PractitionerId).HasColumnName("practitioner_id");
        b.Property(x => x.PractitionerUserId).HasColumnName("practitioner_user_id").HasMaxLength(64).IsRequired();
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.RoundCount).HasColumnName("round_count").IsConcurrencyToken();
        b.Property(x => x.RateMinor).HasColumnName("rate_minor");
        b.Property(x => x.HoursPerWeek).HasColumnName("hours_per_week");
        b.Property(x => x.TotalHours).HasColumnName("total_hours");
        b.Property(x => x.DurationMonths).HasColumnName("duration_months");
        b.Property(x => x.StartDate).HasColumnName("start_date").HasColumnType("date");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.LastActionAt).HasColumnName("last_action_at");
        b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        b.Property(x => x.ClosedAt).HasColumnName("closed_at");
        b.HasMany(x => x.Rounds).WithOne().HasForeignKey(r => r.ProposalId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ClientUserId, x.CreatedAt, x.Id });
        b.HasIndex(x => new { x.PractitionerUserId, x.CreatedAt, x.Id });
        b.HasIndex(x => new { x.Status, x.ExpiresAt });
        b.HasIndex(x => new { x.ClientUserId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.ClientUserId, x.PractitionerId }).IsUnique().HasFilter(OpenFilter).HasDatabaseName("ux_proposals_open_pair");
    }
}

sealed class ProposalRoundConfiguration : IEntityTypeConfiguration<ProposalRound>
{
    public void Configure(EntityTypeBuilder<ProposalRound> b)
    {
        b.ToTable("proposal_rounds");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.ProposalId).HasColumnName("proposal_id");
        b.Property(x => x.Number).HasColumnName("number");
        b.Property(x => x.By).HasColumnName("by").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Action).HasColumnName("action").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RateMinor).HasColumnName("rate_minor");
        b.Property(x => x.HoursPerWeek).HasColumnName("hours_per_week");
        b.Property(x => x.TotalHours).HasColumnName("total_hours");
        b.Property(x => x.DurationMonths).HasColumnName("duration_months");
        b.Property(x => x.StartDate).HasColumnName("start_date").HasColumnType("date");
        b.Property(x => x.Text).HasColumnName("text").HasMaxLength(1000);
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.HasIndex(x => new { x.ProposalId, x.Number }).IsUnique();
    }
}
