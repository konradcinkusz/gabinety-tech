using System.Globalization;
using Gabinety.Booking.Features.Slots;

namespace Gabinety.Booking.Notifications;

public sealed record RenderedEmail(string Subject, string Text, string Html);

/// <summary>
/// One Polish template per kind, plain text plus a minimal HTML version. They take NO brief and no free text of anyone: only the booking id, its
/// start time and the site address ("masz nową rezerwację" and a link to the page, CONTRACTS section 6 Booking). Nothing user-written can reach
/// the HTML, so there is nothing to escape; the values are a Guid, a formatted date and the configured site address.
/// </summary>
public static class NotificationTemplates
{
    sealed record Template(string Subject, string Lead, string LinkLabel, string Path);

    static Template For(NotificationKind kind, bool forPractitioner) => kind switch
    {
        NotificationKind.BookingConfirmed when forPractitioner => new("Masz nową rezerwację", "Masz nową rezerwację.", "Szczegóły rezerwacji", Booking),
        NotificationKind.BookingConfirmed => new("Rezerwacja potwierdzona", "Twoja rezerwacja została potwierdzona.", "Szczegóły rezerwacji", Booking),
        NotificationKind.Reminder24h => new("Przypomnienie: konsultacja już jutro", "Przypominamy, że Twoja konsultacja odbędzie się za 24 godziny.", "Szczegóły rezerwacji", Booking),
        NotificationKind.Reminder1h => new("Przypomnienie: konsultacja za godzinę", "Przypominamy, że Twoja konsultacja zaczyna się za godzinę.", "Szczegóły rezerwacji", Booking),
        NotificationKind.Cancelled => new("Rezerwacja została anulowana", "Rezerwacja została anulowana.", "Szczegóły rezerwacji", Booking),
        NotificationKind.ReviewRequest => new("Jak minęła konsultacja? Wystaw opinię", "Dziękujemy za skorzystanie z platformy. Podziel się opinią o konsultacji.", "Wystaw opinię", "/moje/opinie/{0}"),
        NotificationKind.DisputeOpened => new("Zgłoszono spór dotyczący rezerwacji", "Do rezerwacji zgłoszono spór. Wypłata jest wstrzymana do jego rozstrzygnięcia.", "Szczegóły rezerwacji", Booking),
        NotificationKind.DisputeResolved => new("Spór dotyczący rezerwacji został rozstrzygnięty", "Spór dotyczący rezerwacji został rozstrzygnięty.", "Szczegóły rezerwacji", Booking),
        NotificationKind.PayoutSent => new("Wypłata została wysłana", "Wypłata za konsultację została wysłana.", "Szczegóły rezerwacji", Booking),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "no template for this kind"),
    };

    const string Booking = "/moje/rezerwacje/{0}";

    /// <param name="siteBaseUrl"><c>Site:BaseUrl</c>; a trailing slash is trimmed here too.</param>
    public static RenderedEmail Render(NotificationKind kind, string siteBaseUrl, Guid bookingId, DateTimeOffset startUtc, bool forPractitioner = false)
    {
        var t = For(kind, forPractitioner);
        var link = siteBaseUrl.TrimEnd('/') + string.Format(CultureInfo.InvariantCulture, t.Path, bookingId);
        var when = TimeZoneInfo.ConvertTime(startUtc, SlotCalculator.Warsaw).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        var text = $"{t.Lead}\nTermin: {when} (czas polski)\n\n{t.LinkLabel}: {link}\n\nGabinety Tech\n";
        var html = $"<!doctype html>\n<html lang=\"pl\"><head><meta charset=\"utf-8\"><title>{t.Subject}</title></head>\n"
                 + $"<body><p>{t.Lead}</p><p>Termin: {when} (czas polski)</p><p><a href=\"{link}\">{t.LinkLabel}</a></p><p>Gabinety Tech</p></body></html>\n";
        return new RenderedEmail(t.Subject, text, html);
    }
}
