using Gabinety.Directory.Features.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gabinety.Directory.Features.Verification;

public enum VerificationEventKind { Submitted, Approved, Rejected, Suspended, Reinstated, GithubLinked, NipChanged }

/// <summary>Row of `verification_events` (CONTRACTS 3.1). Append-only: <see cref="VerificationEvents"/> is the only writer and offers no update or delete.</summary>
public sealed class VerificationEvent
{
    public Guid Id { get; set; }
    public Guid PractitionerId { get; set; }
    public VerificationEventKind Kind { get; set; }
    public string ActorUserId { get; set; } = "";
    public string? Note { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>Row of `moderators`: a user id that may moderate (the table is only filled here; later tasks read it).</summary>
public sealed class Moderator { public string UserId { get; set; } = ""; }

public sealed class VerificationEventConfiguration : IEntityTypeConfiguration<VerificationEvent>
{
    public void Configure(EntityTypeBuilder<VerificationEvent> b)
    {
        b.ToTable("verification_events");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.PractitionerId).HasColumnName("practitioner_id");
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ActorUserId).HasColumnName("actor_user_id").HasMaxLength(64).IsRequired();
        b.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
        b.Property(x => x.At).HasColumnName("at");
        b.HasOne<Practitioner>().WithMany().HasForeignKey(x => x.PractitionerId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.PractitionerId, x.At });
    }
}

public sealed class ModeratorConfiguration : IEntityTypeConfiguration<Moderator>
{
    public void Configure(EntityTypeBuilder<Moderator> b)
    {
        b.ToTable("moderators");
        b.HasKey(x => x.UserId);
        b.Property(x => x.UserId).HasColumnName("user_id").HasMaxLength(64);
    }
}
