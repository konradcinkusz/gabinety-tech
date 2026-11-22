using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gabinety.Booking.Notifications.Preferences;

/// <summary>
/// One user's choice for one switchable notification kind (CONTRACTS 4.1 <c>notification_preferences</c>). No row means enabled.
/// <see cref="Kind"/> is only ever one of <see cref="NotificationPreferenceRules.Switchable"/>; the transactional kinds are always sent.
/// </summary>
public sealed class NotificationPreference
{
    public string UserId { get; set; } = "";
    public NotificationKind Kind { get; set; }
    public bool Enabled { get; set; }
}

/// <summary>The decision recorded in DECISIONS (2026-11-22): only the reminders and the review request can be switched off.</summary>
public static class NotificationPreferenceRules
{
    /// <summary>The switchable kinds in the order the API lists them.</summary>
    public static readonly IReadOnlyList<NotificationKind> Switchable = [NotificationKind.Reminder24h, NotificationKind.Reminder1h, NotificationKind.ReviewRequest];

    public static bool IsSwitchable(NotificationKind kind) => Switchable.Contains(kind);

    /// <summary>The wire name of a switchable kind (the enum name, exact casing), or null for any other text: a transactional kind, a number, another casing.</summary>
    public static NotificationKind? ParseSwitchable(string? text)
    {
        foreach (var kind in Switchable)
            if (string.Equals(kind.ToString(), text, StringComparison.Ordinal)) return kind;
        return null;
    }
}

sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> b)
    {
        b.ToTable("notification_preferences");
        b.HasKey(x => new { x.UserId, x.Kind });
        b.Property(x => x.UserId).HasColumnName("user_id").HasMaxLength(64);
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Enabled).HasColumnName("enabled");
    }
}
