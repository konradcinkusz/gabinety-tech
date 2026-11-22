using System.Security.Cryptography;
using System.Text;
using Gabinety.Web.Api;

namespace Gabinety.Web.Components.Pages.Proposals;

/// <summary>
/// One Idempotency-Key per attempt (CONTRACTS 0), as the booking form does: the key is made on the first submit and kept across retries of the SAME proposal (a network error, a 5xx, a double submit); it
/// is replaced as soon as the body differs, because the same key for another practitioner or kind is a 409. The form carries the key and the fingerprint of the body it was made for in hidden fields.
/// </summary>
public static class ProposalAttempt
{
    /// <summary>A fingerprint of what the request says, not of the text: only the hash goes back to the browser, never the message.</summary>
    public static string Fingerprint(Guid practitionerId, string kind, ProposalTermsDto terms, string text, string? company) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{practitionerId:N}|{kind}|{terms.Rate.AmountMinor}|{terms.HoursPerWeek}|{terms.TotalHours}|{terms.DurationMonths}|{terms.StartDate:yyyy-MM-dd}|{text.Trim()}|{company?.Trim()}")))[..32];

    /// <summary>The key to send: the posted one when it was made for this same body, otherwise a new one.</summary>
    public static Guid KeyFor(string? postedKey, string? postedFingerprint, string fingerprint, Func<Guid> newKey) =>
        Guid.TryParse(postedKey, out var key) && key != Guid.Empty && string.Equals(postedFingerprint, fingerprint, StringComparison.Ordinal) ? key : newKey();
}
