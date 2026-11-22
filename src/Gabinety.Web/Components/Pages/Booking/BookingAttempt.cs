using System.Security.Cryptography;
using System.Text;

namespace Gabinety.Web.Components.Pages.Booking;

/// <summary>
/// One Idempotency-Key per attempt (CONTRACTS 0). The key is made on the first submit and kept across retries of the SAME request (a network error, a 5xx, a double submit); it is replaced as soon as
/// the body differs, because the same key with another body is a 409. The form carries the key and the fingerprint of the body it was made for in hidden fields, so no server state is needed.
/// </summary>
public static class BookingAttempt
{
    /// <summary>A fingerprint of what the request says, not of the text: only the hash goes back to the browser, never the brief.</summary>
    public static string Fingerprint(Guid offeringId, DateTimeOffset startUtc, string brief, string? problemSlug, bool aiAssisted) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{offeringId:N}|{startUtc.UtcTicks}|{brief.Trim()}|{problemSlug}|{aiAssisted}")))[..32];

    /// <summary>The key to send: the posted one when it was made for this same body, otherwise a new one.</summary>
    public static Guid KeyFor(string? postedKey, string? postedFingerprint, string fingerprint, Func<Guid> newKey) =>
        Guid.TryParse(postedKey, out var key) && key != Guid.Empty && string.Equals(postedFingerprint, fingerprint, StringComparison.Ordinal) ? key : newKey();
}
