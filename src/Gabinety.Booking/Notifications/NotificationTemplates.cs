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

    /// <summary>The templates of collaboration proposals (ADR proposal 0013): a lead, and a link to the thread in the recipient's own area. No text of any party, no address.</summary>
    static Template ForProposal(NotificationKind kind, bool forPractitioner)
    {
        var path = forPractitioner ? "/gabinet-programisty/propozycje/{0}" : "/moje/propozycje/{0}";
        return kind switch
        {
            NotificationKind.ProposalReceived => new("Masz nową propozycję współpracy", "Otrzymałeś nową propozycję współpracy.", "Zobacz propozycję", path),
            NotificationKind.ProposalCountered => new("Nowa kontrpropozycja współpracy", "Druga strona przesłała kontrpropozycję.", "Zobacz propozycję", path),
            NotificationKind.ProposalAccepted => new("Propozycja współpracy została zaakceptowana", "Propozycja współpracy została zaakceptowana. Dane kontaktowe drugiej strony znajdziesz na stronie propozycji.", "Zobacz propozycję", path),
            NotificationKind.ProposalRejected => new("Propozycja współpracy została odrzucona", "Druga strona odrzuciła propozycję współpracy.", "Zobacz propozycję", path),
            NotificationKind.ProposalExpired => new("Propozycja współpracy wygasła", "Propozycja współpracy wygasła, bo nikt na nią nie odpowiedział w terminie.", "Zobacz propozycję", path),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a proposal kind"),
        };
    }

    public static bool IsProposalKind(NotificationKind kind) =>
        kind is NotificationKind.ProposalReceived or NotificationKind.ProposalCountered or NotificationKind.ProposalAccepted or NotificationKind.ProposalRejected or NotificationKind.ProposalExpired;

    /// <summary>A proposal e-mail: the lead and a link, no date line (a proposal has no start time of its own).</summary>
    public static RenderedEmail RenderProposal(NotificationKind kind, string siteBaseUrl, Guid proposalId, bool forPractitioner)
    {
        var t = ForProposal(kind, forPractitioner);
        var link = siteBaseUrl.TrimEnd('/') + string.Format(CultureInfo.InvariantCulture, t.Path, proposalId);
        var text = $"{t.Lead}\n\n{t.LinkLabel}: {link}\n\nGabinety Tech\n";
        var html = $"<!doctype html>\n<html lang=\"pl\"><head><meta charset=\"utf-8\"><title>{t.Subject}</title></head>\n"
                 + $"<body><p>{t.Lead}</p><p><a href=\"{link}\">{t.LinkLabel}</a></p><p>Gabinety Tech</p></body></html>\n";
        return new RenderedEmail(t.Subject, text, html);
    }

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
