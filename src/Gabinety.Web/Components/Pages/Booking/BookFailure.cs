using Gabinety.Web.Api;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Booking;

/// <summary>What the visitor reads when creating the booking failed, from the code and status of the answer only (never from a title, detail or message of the server).</summary>
public sealed record BookFailure(string Text, bool OffersProfile)
{
    public static BookFailure Of(ApiFailure f) => f.Code switch
    {
        "slot_taken" or "slot_unavailable" => new(BookFlowText.Errors.SlotTaken, true),
        "offering_inactive" => new(BookFlowText.Errors.Inactive, true),
        "own_offering" => new(BookFlowText.Errors.Own, true),
        "practitioner_not_bookable" => new(BookFlowText.Errors.NotBookable, true),
        "practitioner_payments_not_ready" => new(BookFlowText.Errors.PaymentsNotReady, true),
        _ when f.Status == 429 => new(BookFlowText.Errors.RateLimited, false),
        _ when f.Status == 0 || f.Status >= 500 => new(BookFlowText.Errors.Unavailable, false),
        _ => new(BookFlowText.Errors.Generic, false),
    };
}
