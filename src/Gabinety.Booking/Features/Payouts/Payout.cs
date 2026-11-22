using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gabinety.Booking.Features.Payouts;

public enum PayoutStatus { Pending, Sent, Failed }

/// <summary>One payout to a practitioner per booking (CONTRACTS 4.1). The payout job (T-04.08d) creates the rows; T-09.05 adds the durable retry columns.</summary>
public sealed class Payout
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid PractitionerId { get; set; }
    public long AmountMinor { get; set; }
    public PayoutStatus Status { get; set; }
    public string? ProviderRef { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    /// <summary>How many times a run has taken this payout up (T-09.05). The job's own bound, durable across restarts and instances; it replaces the in-memory counter.</summary>
    public int AttemptCount { get; set; }
    /// <summary>Not before this instant may a run take the payout up: the backoff after a failure, or the lease of the run that has it in hand (a crashed owner's lease just runs out). Null: at once.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }
}

sealed class PayoutConfiguration : IEntityTypeConfiguration<Payout>
{
    public void Configure(EntityTypeBuilder<Payout> b)
    {
        b.ToTable("payouts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.BookingId).HasColumnName("booking_id");
        b.Property(x => x.PractitionerId).HasColumnName("practitioner_id");
        b.Property(x => x.AmountMinor).HasColumnName("amount_minor");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ProviderRef).HasColumnName("provider_ref").HasMaxLength(100);
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.SentAt).HasColumnName("sent_at");
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0);
        b.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        b.HasIndex(x => x.BookingId).IsUnique(); // one payout per booking
    }
}
