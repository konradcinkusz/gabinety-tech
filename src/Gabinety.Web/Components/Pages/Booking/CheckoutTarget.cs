namespace Gabinety.Web.Components.Pages.Booking;

/// <summary>
/// Where the browser goes after a booking is created (<c>checkoutUrl</c>): a page of this site (the mock checkout) or the payment provider's own page (Stripe Checkout, https). Anything else is refused:
/// the URL comes from a service, and a redirect to it must never be a way to leave for <c>javascript:</c>, a protocol-relative host or plain http elsewhere. Port of <c>safeCheckoutTarget</c> in
/// <c>web/src/components/booking/lib.ts</c>.
/// </summary>
public static class CheckoutTarget
{
    /// <returns>a site-relative path (with query) or an absolute https URL; null when the target is refused.</returns>
    public static string? Safe(string? url, string siteOrigin, bool mockPayments = false)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Any(c => c < 0x20 || c == '\\')) return null;
        if (url.StartsWith('/')) return url.StartsWith("//") ? null : url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || !string.IsNullOrEmpty(u.UserInfo)) return null;
        // Fake profile only (the mock provider): Booking builds the mock checkout address from ITS Site:BaseUrl, which is this host's origin (Site:BaseUrl, :3000 in the fake profile).
        // The mock page exists on this host only in a fake profile (WebOptions.MockPayments), so an absolute address whose path is exactly the mock checkout of a booking is followed as a path of this site.
        // Never in a deployed profile (mockPayments false), and the path is matched exactly (a guid, no query), so it cannot be used to go anywhere else.
        if (mockPayments && System.Text.RegularExpressions.Regex.IsMatch(u.AbsolutePath, "^/checkout/mock/[0-9a-fA-F-]{36}$") && string.IsNullOrEmpty(u.Query) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)) return u.AbsolutePath;
        if (Uri.TryCreate(siteOrigin, UriKind.Absolute, out var site) && u.GetLeftPart(UriPartial.Authority).Equals(site.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)) return u.PathAndQuery;
        return u.Scheme == Uri.UriSchemeHttps ? u.AbsoluteUri : null;
    }
}
