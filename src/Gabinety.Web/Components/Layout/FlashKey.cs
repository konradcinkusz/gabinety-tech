using Gabinety.Web.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Gabinety.Web.Components.Layout;

/// <summary>The flash messages the shell can show, by key (<c>?flash=signed-out</c>). An unknown key shows nothing: text from a query string is never rendered.</summary>
public static class FlashKey
{
    public const string Parameter = "flash", SignedOut = "signed-out";
    /// <summary>Keys of the practitioner area (<c>PlPractitioner.Flash</c>).</summary>
    public const string Saved = "saved", ProfileSubmitted = "profile-submitted", GabinetCreated = "gabinet-created", OfferDisabled = "offer-disabled", OfferEnabled = "offer-enabled";
    static readonly Dictionary<string, string> Messages = new()
    {
        [SignedOut] = Pl.Flash.SignedOut,
        ["admin-approved"] = PlAdmin.Flash.Approved, ["admin-rejected"] = PlAdmin.Flash.Rejected, ["admin-suspended"] = PlAdmin.Flash.Suspended, ["admin-reinstated"] = PlAdmin.Flash.Reinstated,
        ["admin-dispute-resolved"] = PlAdmin.Flash.DisputeResolved, ["admin-review-published"] = PlAdmin.Flash.ReviewPublished, ["admin-review-rejected"] = PlAdmin.Flash.ReviewRejected,
        [Saved] = PlPractitioner.Flash.Saved, [ProfileSubmitted] = PlPractitioner.Flash.Submitted, [GabinetCreated] = PlPractitioner.Flash.Created,
        [OfferDisabled] = PlPractitioner.Flash.OfferDisabled, [OfferEnabled] = PlPractitioner.Flash.OfferEnabled,
    };

    public static string? Resolve(string uri)
    {
        var query = new Uri(uri, UriKind.RelativeOrAbsolute) is { IsAbsoluteUri: true } u ? u.Query : "";
        return QueryHelpers.ParseQuery(query).TryGetValue(Parameter, out var v) && Messages.TryGetValue(v.ToString(), out var text) ? text : null;
    }
}
