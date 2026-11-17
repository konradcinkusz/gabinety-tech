using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gabinety.Booking.Features.Bookings.Create;

/// <summary>The payment of one booking (CONTRACTS 4.1). Status is stored as the enum name.</summary>
public sealed class Payment
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public string Provider { get; set; } = "";
    public string? ProviderPaymentId { get; set; }
    public long AmountMinor { get; set; }
    public PaymentStatus Status { get; set; }
    public long RefundedMinor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>The stored answer of a create request, keyed by (caller, Idempotency-Key) (CONTRACTS 0). The body never contains brief text.</summary>
public sealed class IdempotencyRecord
{
    public string ClientUserId { get; set; } = "";
    public Guid IdempotencyKey { get; set; }
    public string RequestHash { get; set; } = "";
    public int ResponseStatus { get; set; }
    public string ResponseBody { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.BookingId).HasColumnName("booking_id");
        b.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(20);
        b.Property(x => x.ProviderPaymentId).HasColumnName("provider_payment_id").HasMaxLength(100);
        b.Property(x => x.AmountMinor).HasColumnName("amount_minor");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RefundedMinor).HasColumnName("refunded_minor");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasIndex(x => x.BookingId).IsUnique();
    }
}

sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("idempotency_records");
        b.HasKey(x => new { x.ClientUserId, x.IdempotencyKey });
        b.Property(x => x.ClientUserId).HasColumnName("client_user_id").HasMaxLength(64);
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key");
        b.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(64);
        b.Property(x => x.ResponseStatus).HasColumnName("response_status");
        b.Property(x => x.ResponseBody).HasColumnName("response_body").HasColumnType("text");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
    }
}
