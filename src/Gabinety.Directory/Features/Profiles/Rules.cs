using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Gabinety.Directory.Features.Profiles;

public interface INipValidator { bool IsValid(string nip); }

/// <summary>Ten digits with the weighted checksum; a registry lookup may replace it later.</summary>
public sealed class NipChecksumValidator : INipValidator
{
    static readonly int[] Weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];

    public bool IsValid(string nip)
    {
        if (nip is not { Length: 10 } || !nip.All(char.IsAsciiDigit)) return false;
        var sum = 0;
        for (var i = 0; i < 9; i++) sum += (nip[i] - '0') * Weights[i];
        var check = sum % 11;
        return check != 10 && check == nip[9] - '0';
    }
}

public interface IContactDetailDetector { bool ContainsContactDetails(string? text); }

/// <summary>Anti-leakage control, not security: an e-mail, a phone number (7+ digits, spaces or dashes allowed) or an @handle.</summary>
public sealed partial class ContactDetailDetector : IContactDetailDetector
{
    [GeneratedRegex(@"[\w.+-]+@[\w-]+(\.[\w-]+)+")] private static partial Regex Email();
    [GeneratedRegex(@"(?<![\w.,])\+?\d(?:[ \-]?\d){6,}(?![\w])")] private static partial Regex Phone();
    [GeneratedRegex(@"(?<![\w@])@\w{2,}")] private static partial Regex Handle();

    // Years are not phone numbers: "2015-2023", "2019 - obecnie", "2020 2021" are removed before the phone check, so a real number next to them is still found.
    [GeneratedRegex(@"\b(?:19|20)\d{2}\s?[-–—]\s?(?:(?:19|20)\d{2}|obecnie|teraz)\b|\b(?:19|20)\d{2} (?:19|20)\d{2}\b", RegexOptions.IgnoreCase)] private static partial Regex YearRange();

    public bool ContainsContactDetails(string? text) =>
        !string.IsNullOrEmpty(text) && (Email().IsMatch(text) || Phone().IsMatch(YearRange().Replace(text, " ")) || Handle().IsMatch(text));
}

public static partial class SlugGenerator
{
    const string Fallback = "programista";

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")] private static partial Regex Shape();
    [GeneratedRegex("[^a-z0-9]+")] private static partial Regex NonAlnum();

    public static bool IsValid(string slug) => slug.Length is >= 3 and <= 60 && Shape().IsMatch(slug);

    public static string FromName(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.ToLowerInvariant().Replace('ł', 'l').Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        var slug = NonAlnum().Replace(sb.ToString(), "-").Trim('-');
        if (slug.Length > 60) slug = slug[..60].Trim('-');
        return slug.Length < 3 ? Fallback : slug;
    }

    /// <summary>Suffix 1 is the bare slug; n >= 2 appends -n, shortening the base so the result stays within 60.</summary>
    public static string WithSuffix(string slug, int n)
    {
        if (n <= 1) return slug;
        var suffix = "-" + n;
        return slug[..Math.Min(slug.Length, 60 - suffix.Length)].TrimEnd('-') + suffix;
    }
}

/// <summary>CONTRACTS 3.5 URL rules: https, a named host (no IP, no localhost), at most 300 characters.</summary>
public static class UrlRules
{
    public static readonly string[] DefaultMeetingHosts = ["meet.google.com", "zoom.us", "teams.microsoft.com", "whereby.com", "meet.jit.si"];

    public static bool IsAcceptable(string url, out string host)
    {
        host = "";
        if (url.Length > 300 || !Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
        host = u.IdnHost.ToLowerInvariant();
        return host != "localhost" && !host.EndsWith(".localhost") && !IPAddress.TryParse(host.Trim('[', ']'), out _);
    }

    public static bool HostAllowed(string host, IEnumerable<string> allowed) =>
        allowed.Any(a => host == a || host.EndsWith("." + a));
}

/// <summary>Pure function of the profile facts (CONTRACTS 3.5), two decimal places.</summary>
public static class ProfileCompleteness
{
    public static decimal Compute(bool headline, bool bio200, bool offering, bool github, bool portfolio, bool nipValid, bool openSlots) =>
        (headline ? 0.15m : 0) + (bio200 ? 0.20m : 0) + (offering ? 0.25m : 0) + (github ? 0.10m : 0) +
        (portfolio ? 0.10m : 0) + (nipValid ? 0.10m : 0) + (openSlots ? 0.10m : 0);
}
