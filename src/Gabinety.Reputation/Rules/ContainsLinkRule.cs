using System.Text.RegularExpressions;

namespace Gabinety.Reputation.Rules;

/// <summary>More than one URL, or any URL whose host is not on (or under) the code-host allow-list.</summary>
public sealed partial class ContainsLinkRule(IReadOnlyCollection<string>? codeHosts = null) : IReviewRule
{
    public static readonly string[] DefaultCodeHosts = ["github.com", "gitlab.com", "bitbucket.org", "codeberg.org", "dev.azure.com"];
    readonly string[] _hosts = (codeHosts is { Count: > 0 } ? codeHosts : DefaultCodeHosts)
        .Select(h => h.Trim().ToLowerInvariant()).Where(h => h.Length > 0).ToArray();

    public string Code => "contains_link";

    // Scheme or www. URLs, or a bare domain.tld followed by a path.
    [GeneratedRegex(@"(?:https?://|www\.)[^\s<>""')\]]+|\b(?:[a-z0-9-]+\.)+[a-z]{2,}/[^\s<>""')\]]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Url();

    public string? Check(ReviewText input)
    {
        var count = 0;
        foreach (Match m in Url().Matches(input.Clean))
        {
            if (++count > 1 || !Allowed(HostOf(m.Value))) return Code;
        }
        return null;
    }

    bool Allowed(string host) => _hosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal));

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
