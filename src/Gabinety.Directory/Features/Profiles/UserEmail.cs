using System.Net.Mail;
using System.Security.Claims;

namespace Gabinety.Directory.Features.Profiles;

/// <summary>
/// The e-mail address of the signed-in user, read from the access token (claim <c>email</c>, always present; CONTRACTS 1, <c>docs/spikes/authservice.md</c> section 1).
/// A service keeps its own copy of this small rule (Booking: <c>Notifications/UserEmail.cs</c>), not a shared one: the kernel is budgeted (SPEC 3.2).
/// Personal data: a caller stores it as a snapshot where a notification needs it and never logs it. A value that is not a bare <c>local@domain</c> yields null.
/// </summary>
internal static class UserEmail
{
    public const int MaxLength = 254;

    /// <summary>The claim as the JWT handler leaves it (<c>email</c>) or maps it (<see cref="ClaimTypes.Email"/>).</summary>
    public static string? From(ClaimsPrincipal user) =>
        Normalize(user.FindFirst("email")?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value);

    /// <summary>Trimmed address, or null when empty or not a single plain address (no display name, list, whitespace or control character: header injection).</summary>
    public static string? Normalize(string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v) || v.Length > MaxLength) return null;
        if (v.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '<' or '>' or ',' or ';')) return null;
        return MailAddress.TryCreate(v, out var a) && a.Address == v && a.Host.Contains('.') ? v : null;
    }
}
