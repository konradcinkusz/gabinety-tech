using Gabinety.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gabinety.Booking.Features.Bookings.Entities;

/// <summary>A booking of one offering at one time (CONTRACTS 4.1). Status is stored as the enum NAME: the exclusion constraint reads it as a string.</summary>
public sealed class Booking
{
    public Guid Id { get; set; }
    public string ClientUserId { get; set; } = "";
    /// <summary>What the practitioner sees instead of a name: "Klient" and the first letter of the client's token name, taken when the booking was made (ADR-0012 section 6). No e-mail, no surname. Null for rows made before the column existed and after an erasure.</summary>
    public string? ClientDisplayName { get; set; }
    /// <summary>
    /// The client's e-mail, copied from the validated access token when the booking is created (T-04.09c), so a notification has an address without any lookup.
    /// Personal data: never logged, never in a response or an export, set to null by the erasure endpoint. Null for a booking made before the column existed.
    /// </summary>
    public string? ClientEmail { get; set; }
    public Guid PractitionerId { get; set; }
    public string PractitionerUserId { get; set; } = "";
    public Guid OfferingId { get; set; }
    public string OfferingTitle { get; set; } = "";
    public short DurationMinutes { get; set; }
    public long PriceMinor { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public BookingStatus Status { get; set; }
    public string? StatusReason { get; set; }
    public string BriefText { get; set; } = "";
    public string? BriefProblemSlug { get; set; }
    public bool BriefAiAssisted { get; set; }
    public string? MeetingUrlSnapshot { get; set; }
    public DateTimeOffset? HoldExpiresAt { get; set; }
    public long? FeeMinor { get; set; }
    public long? PayoutMinor { get; set; }
    public DateTimeOffset? PayoutDueAt { get; set; }
    public Guid IdempotencyKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset? DisputeOpenedAt { get; set; }
    /// <summary>not_as_described, practitioner_no_show or other (T-04.07b). Read by the admin list (T-04.07c).</summary>
    public string? DisputeKind { get; set; }
    /// <summary>The client's free text. Never logged. The erasure endpoint (T-04.10b) must anonymise it.</summary>
    public string? DisputeText { get; set; }
    public string? DisputeResolution { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> b)
    {
        b.ToTable("bookings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.ClientUserId).HasColumnName("client_user_id").HasMaxLength(64);
        b.Property(x => x.ClientDisplayName).HasColumnName("client_display_name").HasMaxLength(60);
        b.Property(x => x.ClientEmail).HasColumnName("client_email").HasMaxLength(254);
        b.Property(x => x.PractitionerId).HasColumnName("practitioner_id");
        b.Property(x => x.PractitionerUserId).HasColumnName("practitioner_user_id").HasMaxLength(64);
        b.Property(x => x.OfferingId).HasColumnName("offering_id");
        b.Property(x => x.OfferingTitle).HasColumnName("offering_title").HasMaxLength(120);
        b.Property(x => x.DurationMinutes).HasColumnName("duration_minutes");
        b.Property(x => x.PriceMinor).HasColumnName("price_minor");
        b.Property(x => x.StartUtc).HasColumnName("start_utc");
        b.Property(x => x.EndUtc).HasColumnName("end_utc");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.StatusReason).HasColumnName("status_reason").HasMaxLength(500);
        b.Property(x => x.BriefText).HasColumnName("brief_text").HasMaxLength(2000);
        b.Property(x => x.BriefProblemSlug).HasColumnName("brief_problem_slug").HasMaxLength(60);
        b.Property(x => x.BriefAiAssisted).HasColumnName("brief_ai_assisted");
        b.Property(x => x.MeetingUrlSnapshot).HasColumnName("meeting_url_snapshot").HasMaxLength(300);
        b.Property(x => x.HoldExpiresAt).HasColumnName("hold_expires_at");
        b.Property(x => x.FeeMinor).HasColumnName("fee_minor");
        b.Property(x => x.PayoutMinor).HasColumnName("payout_minor");
        b.Property(x => x.PayoutDueAt).HasColumnName("payout_due_at");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at");
        b.Property(x => x.EndedAt).HasColumnName("ended_at");
        b.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
        b.Property(x => x.DisputeOpenedAt).HasColumnName("dispute_opened_at");
        b.Property(x => x.DisputeKind).HasColumnName("dispute_kind").HasMaxLength(30);
        b.Property(x => x.DisputeText).HasColumnName("dispute_text").HasMaxLength(1000);
        b.Property(x => x.DisputeResolution).HasColumnName("dispute_resolution").HasMaxLength(20);
        b.Property(x => x.ResolvedAt).HasColumnName("resolved_at");
        b.HasIndex(x => new { x.PractitionerId, x.StartUtc });
        b.HasIndex(x => new { x.ClientUserId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.Status, x.HoldExpiresAt });
    }
}
