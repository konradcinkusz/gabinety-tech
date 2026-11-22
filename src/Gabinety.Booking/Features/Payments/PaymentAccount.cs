using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gabinety.Booking.Features.Payments;

/// <summary>
/// The practitioner's connected account at the payment provider (T-09.05, ADR proposal 0011): one row per practitioner, written by the onboarding flow.
/// <see cref="Ready"/> is the provider's last word that the account can receive transfers; it is read when a booking is created and when a payout is made.
/// Only the provider's account id is stored: no identity or bank data of the practitioner ever reaches this service.
/// </summary>
public sealed class PaymentAccount
{
    public Guid PractitionerId { get; set; }
    public string Provider { get; set; } = "";
    public string ProviderAccountId { get; set; } = "";
    public bool Ready { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

sealed class PaymentAccountConfiguration : IEntityTypeConfiguration<PaymentAccount>
{
    public void Configure(EntityTypeBuilder<PaymentAccount> b)
    {
        b.ToTable("practitioner_payment_accounts");
        b.HasKey(x => x.PractitionerId);
        b.Property(x => x.PractitionerId).HasColumnName("practitioner_id").ValueGeneratedNever();
        b.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(20);
        b.Property(x => x.ProviderAccountId).HasColumnName("provider_account_id").HasMaxLength(100);
        b.Property(x => x.Ready).HasColumnName("ready");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasIndex(x => x.ProviderAccountId).IsUnique();
    }
}
