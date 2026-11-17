using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gabinety.Booking.Notifications;

/// <summary>The kinds of CONTRACTS 4.1 <c>notifications.kind</c>. Stored as the enum NAME.</summary>
public enum NotificationKind { BookingConfirmed, Reminder24h, Reminder1h, Cancelled, ReviewRequest, DisputeOpened, DisputeResolved, PayoutSent }

/// <summary>
/// One e-mail to send to one user about one booking (CONTRACTS 4.1). Unique per <c>(booking_id, kind, to_user_id)</c>.
/// <see cref="DueAt"/> is the time of the NEXT attempt: the dispatcher moves it forward by the backoff when it claims the row, because the contract
/// has no separate "last attempt" column. <see cref="Attempts"/> is also the optimistic-concurrency token that makes a claim exclusive.
/// </summary>
public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
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
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.ToUserId).HasColumnName("to_user_id").HasMaxLength(64);
        b.Property(x => x.DueAt).HasColumnName("due_at");
        b.Property(x => x.SentAt).HasColumnName("sent_at");
        b.Property(x => x.Attempts).HasColumnName("attempts").IsConcurrencyToken();
        b.HasIndex(x => new { x.BookingId, x.Kind, x.ToUserId }).IsUnique();
    }
}
