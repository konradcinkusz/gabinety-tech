namespace Gabinety.Web.Components.Pages.Bookings;

/// <summary>A link that comes from a service (the meeting link a practitioner typed) is rendered only when it is an absolute http or https address: never <c>javascript:</c>, <c>data:</c> or a relative path.</summary>
public static class SafeUrl
{
    public static string? Http(string? url) =>
        !string.IsNullOrWhiteSpace(url) && url.All(c => c >= 0x20) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) && u.Scheme is "http" or "https" && string.IsNullOrEmpty(u.UserInfo) ? u.AbsoluteUri : null;
}
