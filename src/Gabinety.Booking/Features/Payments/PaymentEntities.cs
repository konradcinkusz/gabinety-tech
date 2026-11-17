using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gabinety.Booking.Features.Payments;

public enum WebhookOutcome { Applied, Duplicate, Ignored, Rejected }

/// <summary>One provider event seen once, keyed by (provider, event id): the key is what makes the webhook idempotent (CONTRACTS 4.1, 4.3).</summary>
public sealed class WebhookEvent
{
    public string Provider { get; set; } = "";
    public string EventId { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
    public WebhookOutcome Outcome { get; set; }
}

/// <summary>Append-only money ledger. The amount is always positive; the kind gives the direction (CONTRACTS 4.1).</summary>
public sealed class LedgerEntry
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public LedgerKind Kind { get; set; }
    public long AmountMinor { get; set; }
    public string? PairKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> b)
    {
        b.ToTable("webhook_events");
        b.HasKey(x => new { x.Provider, x.EventId });
        b.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(20);
        b.Property(x => x.EventId).HasColumnName("event_id").HasMaxLength(100);
        b.Property(x => x.ReceivedAt).HasColumnName("received_at");
        b.Property(x => x.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(20);
    }
}

sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> b)
    {
        b.ToTable("ledger_entries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.BookingId).HasColumnName("booking_id");
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.AmountMinor).HasColumnName("amount_minor");
        b.Property(x => x.PairKey).HasColumnName("pair_key").HasMaxLength(80);
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.HasIndex(x => x.BookingId);
        // One platform fee per client/practitioner pair, race-safe: the loser's insert fails (CONTRACTS 4.1, 9.1).
        b.HasIndex(x => x.PairKey).IsUnique().HasFilter("kind = 'PlatformFee'");
    }
}
