using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Gabinety.Booking.Features.Proposals;

/// <summary>
/// What a proposal's free text (the message and the company name) may not contain: an e-mail, a phone number, an @handle, a t.me or wa.me link, a messenger name followed by a handle or
/// a number, or a link to anything but a code host. Anti-leakage, not security (SPEC 6.1): a determined user can obfuscate. A per-service copy of the rules of Directory
/// (<c>ContactDetailDetector</c>) and Reputation (<c>ContactDetailsRule</c>, <c>ContainsLinkRule</c>), because a service may not reference another (ADR proposal 0013). Pure; never logs.
/// ISO dates ("2027-01-15") are not phone numbers: a proposal states a start date in words.
/// </summary>
public static partial class ProposalTextRules
{
    public static readonly string[] CodeHosts = ["github.com", "gitlab.com", "bitbucket.org", "codeberg.org", "dev.azure.com"];
    const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+", O)] private static partial Regex Email();
    [GeneratedRegex(@"\B@\w{2,}", O)] private static partial Regex Handle();
    [GeneratedRegex(@"\b(?:t|wa)\.me/", O)] private static partial Regex MessengerLink();
    [GeneratedRegex(@"\+?\d{2,}(?:[ -]\d{2,})+|\d{9}", O)] private static partial Regex Phone();
    [GeneratedRegex(@"\b(?:whatsapp|telegram|signal|discord)\b.{0,20}?(?:@\w+|\+?\d(?:[ -]?\d){5,})", O)] private static partial Regex Messenger();
    [GeneratedRegex(@"^(?:\d{4}-\d{1,2}-\d{1,2}|\d{1,2}-\d{1,2}-\d{4})$", O)] private static partial Regex Date();
    [GeneratedRegex(@"(?:https?://|www\.)[^\s<>""')\]]+|\b(?:[a-z0-9-]+\.)+[a-z]{2,}/[^\s<>""')\]]*", O)] private static partial Regex Url();

    /// <summary>Control and zero-width characters removed, whitespace runs collapsed to one space, ends trimmed. Newlines become spaces: a proposal is a short note.</summary>
    public static string Clean(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var sb = new StringBuilder(raw.Length);
        var pendingSpace = false;
        foreach (var ch in raw)
        {
            if (char.IsWhiteSpace(ch)) { pendingSpace = sb.Length > 0; continue; }
            if (char.GetUnicodeCategory(ch) is UnicodeCategory.Control or UnicodeCategory.Format) continue;
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>True when the (already cleaned) text carries a contact detail or a link that is not to a code host.</summary>
    public static bool HasContactDetails(string clean)
    {
        if (clean.Length == 0) return false;
        if (Email().IsMatch(clean) || Handle().IsMatch(clean) || MessengerLink().IsMatch(clean) || Messenger().IsMatch(clean)) return true;
        foreach (Match m in Phone().Matches(clean))
            if (m.Value.Count(char.IsAsciiDigit) >= 7 && !Date().IsMatch(m.Value)) return true;
        var links = 0;
        foreach (Match m in Url().Matches(clean))
            if (++links > 1 || !AllowedHost(HostOf(m.Value))) return true;
        return false;
    }

    static bool AllowedHost(string host) => CodeHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal));

    static string HostOf(string url)
    {
        var s = url;
        var scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];
        var end = s.IndexOfAny(['/', '?', '#']);
        if (end >= 0) s = s[..end];
        s = s[(s.LastIndexOf('@') + 1)..]; // drop user-info: https://github.com@evil.test/
        var colon = s.IndexOf(':');
        if (colon >= 0) s = s[..colon];
        return s.Trim('.').ToLowerInvariant();
    }
}
