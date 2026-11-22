using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Bookings;

public enum BadgeKind { Neutral, Info, Success, Warning, Danger }

/// <summary>A state shown as a badge: the Polish label (always shown, colour never carries the meaning alone) and the kind of the badge (DESIGN-CONTRACT 5.7, G-8). The wire names are those of CONTRACTS 8.1.</summary>
public sealed record Badge(string Label, BadgeKind Kind)
{
    public string CssClass => $"badge badge--{Kind.ToString().ToLowerInvariant()}";

    public static Badge ForBooking(string status) => status switch
    {
        "PendingPayment" => new(Pl.Bookings.Statuses[status], BadgeKind.Warning),
        "Confirmed" => new(Pl.Bookings.Statuses[status], BadgeKind.Info),
        "Completed" => new(Pl.Bookings.Statuses[status], BadgeKind.Success),
        "CancelledByClient" or "Expired" or "Resolved" => new(Pl.Bookings.Statuses[status], BadgeKind.Neutral),
        "LateCancelledByClient" or "Disputed" => new(Pl.Bookings.Statuses[status], BadgeKind.Warning),
        "CancelledByPractitioner" or "ClientNoShow" => new(Pl.Bookings.Statuses[status], BadgeKind.Danger),
        _ => new(Pl.Bookings.UnknownStatus, BadgeKind.Neutral),
    };

    public static Badge ForPayment(string status) => status switch
    {
        "Succeeded" => new(BookingsText.Payment.Succeeded, BadgeKind.Success),
        "Refunded" => new(BookingsText.Payment.Refunded, BadgeKind.Info),
        "PartiallyRefunded" => new(BookingsText.Payment.PartiallyRefunded, BadgeKind.Info),
        "Created" => new(BookingsText.Payment.Created, BadgeKind.Warning),
        "Failed" => new(BookingsText.Payment.Failed, BadgeKind.Danger),
        _ => new(BookingsText.Payment.Unknown, BadgeKind.Neutral),
    };
}
