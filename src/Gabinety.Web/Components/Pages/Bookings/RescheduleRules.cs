using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Gabinet;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Bookings;

/// <summary>The reschedule page's rules (CONTRACTS 4.2): which failure says what, which offer of the profile is the booking's, and the start the form posts.</summary>
public static class RescheduleRules
{
    /// <summary>The Polish sentence of a refused move, by code. Anything else is the client area's common text (the status and the code only, never the server's words).</summary>
    public static string FailureText(ApiFailure f) => f.Code switch
    {
        "reschedule_not_allowed" => BookingsText.Reschedule.Refused,
        "slot_taken" => BookingsText.Reschedule.SlotTaken,
        "slot_unavailable" => BookingsText.Reschedule.SlotUnavailable,
        _ => ActionFailure.Text(f),
    };

    /// <summary>The booking view does not carry its offering's id (CONTRACTS 4.4), so the offer is found in the practitioner's profile by its title. Only one offer of that title counts; two of one name are not guessed.</summary>
    public static OfferingDto? OfferingOf(PractitionerProfileDto profile, string offeringTitle)
    {
        var matches = profile.Offerings.Where(o => o.Title == offeringTitle).Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>The start the picker's button carries: the UTC instant written with <c>Z</c>, read back exactly; null for anything else.</summary>
    public static DateTimeOffset? ParseStart(string? value) =>
        DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var start)
            ? start
            : null;
}
