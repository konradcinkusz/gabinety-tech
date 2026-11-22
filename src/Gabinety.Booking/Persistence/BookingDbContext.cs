using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Features.Payouts;
using Gabinety.Booking.Notifications;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;

namespace Gabinety.Booking.Persistence;

/// <summary>Weekly availability window of a practitioner in Europe/Warsaw local time (CONTRACTS 4.1). Boundaries are on :00 or :30.</summary>
public sealed class AvailabilityRule
{
    public Guid Id { get; set; }
    public Guid PractitionerId { get; set; }
    public short IsoWeekday { get; set; }
    public TimeOnly StartLocal { get; set; }
    public TimeOnly EndLocal { get; set; }
}

/// <summary>A blocked day; unique per practitioner and date.</summary>
public sealed class AvailabilityException
{
    public Guid Id { get; set; }
    public Guid PractitionerId { get; set; }
    public DateOnly DateLocal { get; set; }
}

/// <summary>Owns bookingdb (P3). The other Booking tables arrive with later tasks.</summary>
public sealed class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
{
    public DbSet<AvailabilityRule> Rules => Set<AvailabilityRule>();
    public DbSet<AvailabilityException> Exceptions => Set<AvailabilityException>();
    public DbSet<BookingEntity> Bookings => Set<BookingEntity>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<Payout> Payouts => Set<Payout>();
    public DbSet<PaymentAccount> PaymentAccounts => Set<PaymentAccount>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookingDbContext).Assembly);
}

sealed class AvailabilityRuleConfiguration : IEntityTypeConfiguration<AvailabilityRule>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AvailabilityRule> b)
    {
        b.ToTable("availability_rules");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.PractitionerId).HasColumnName("practitioner_id");
        b.Property(x => x.IsoWeekday).HasColumnName("iso_weekday");
        b.Property(x => x.StartLocal).HasColumnName("start_local").HasColumnType("time without time zone");
        b.Property(x => x.EndLocal).HasColumnName("end_local").HasColumnType("time without time zone");
        b.HasIndex(x => new { x.PractitionerId, x.IsoWeekday });
    }
}

sealed class AvailabilityExceptionConfiguration : IEntityTypeConfiguration<AvailabilityException>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AvailabilityException> b)
    {
        b.ToTable("availability_exceptions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.PractitionerId).HasColumnName("practitioner_id");
        b.Property(x => x.DateLocal).HasColumnName("date_local").HasColumnType("date");
        b.HasIndex(x => new { x.PractitionerId, x.DateLocal }).IsUnique();
    }
}

/// <summary>Only for `dotnet ef`; never used at runtime and never opens a connection.</summary>
public sealed class BookingDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<BookingDbContext>
{
    public BookingDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<BookingDbContext>()
        .UseNpgsql("Host=localhost;Database=design-time", o => o.MigrationsAssembly(typeof(BookingDbContext).Assembly.GetName().Name)).Options);
}
