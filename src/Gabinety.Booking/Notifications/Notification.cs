using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gabinety.Booking.Notifications;

/// <summary>The kinds of CONTRACTS 4.1 <c>notifications.kind</c>. Stored as the enum NAME.</summary>
public enum NotificationKind
{
    BookingConfirmed, Reminder24h, Reminder1h, Cancelled, ReviewRequest, DisputeOpened, DisputeResolved, PayoutSent, Rescheduled,
    /// <summary>ADR proposal 0013: collaboration proposals. Their rows carry <c>ProposalId</c> instead of <c>BookingId</c>.</summary>
    ProposalReceived, ProposalCountered, ProposalAccepted, ProposalRejected, ProposalExpired,
}

/// <summary>
/// One e-mail to send to one user about one booking (CONTRACTS 4.1) or, since T-13.02, about one collaboration proposal (exactly one of <see cref="BookingId"/> and <see cref="ProposalId"/> is set).
/// Unique per <c>(booking_id, kind, to_user_id)</c> for a booking, and per <c>(proposal_id, kind, to_user_id, round)</c> for a proposal, so each round of a negotiation can send one e-mail.
/// <see cref="DueAt"/> is the time of the NEXT attempt: the dispatcher moves it forward by the backoff when it claims the row, because the contract
/// has no separate "last attempt" column. <see cref="Attempts"/> is also the optimistic-concurrency token that makes a claim exclusive.
/// </summary>
public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid? BookingId { get; set; }
    public Guid? ProposalId { get; set; }
    /// <summary>The proposal round the e-mail is about; 0 for a booking row.</summary>
    public int Round { get; set; }
    public NotificationKind Kind { get; set; }
    public string ToUserId { get; set; } = "";
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public int Attempts { get; set; }
}

sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.BookingId).HasColumnName("booking_id");
        b.Property(x => x.ProposalId).HasColumnName("proposal_id");
        b.Property(x => x.Round).HasColumnName("round");
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.ToUserId).HasColumnName("to_user_id").HasMaxLength(64);
        b.Property(x => x.DueAt).HasColumnName("due_at");
        b.Property(x => x.SentAt).HasColumnName("sent_at");
        b.Property(x => x.Attempts).HasColumnName("attempts").IsConcurrencyToken();
        // Two filtered indexes, not one composite: NULLs are distinct in PostgreSQL, so a plain unique index would not constrain either kind of row.
        b.HasIndex(x => new { x.BookingId, x.Kind, x.ToUserId }).IsUnique().HasFilter("booking_id IS NOT NULL");
        b.HasIndex(x => new { x.ProposalId, x.Kind, x.ToUserId, x.Round }).IsUnique().HasFilter("proposal_id IS NOT NULL");
    }
}
