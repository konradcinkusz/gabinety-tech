using System.Globalization;
using System.Text.Json;
using Gabinety.Web.Api;

namespace Gabinety.Web.Components.Pages.Gabinet;

/// <summary>The structured data of a public profile (SPEC 6.5): a port of <c>profile/jsonLd.ts</c>. The text of a profile is user text, so the output is escaped for a script element.</summary>
public static class ProfileJsonLd
{
    /// <summary>SPEC 6.5: aggregateRating only when the review count is at least 3.</summary>
    public const int MinReviewsForRating = 3;

    /// <summary>A link is shown and listed only when it is https (F-09): the server already refuses anything else, this is the second check.</summary>
    public static string? SafeHttps(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? u.AbsoluteUri : null;

    public static string Github(string login) => "https://github.com/" + Uri.EscapeDataString(login);

    public static string Build(PractitionerProfileDto p, string url)
    {
        var portfolio = SafeHttps(p.PortfolioUrl);
        var ld = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "ProfessionalService",
            ["name"] = p.DisplayName,
            ["description"] = p.Headline,
            ["url"] = url,
        };
        var same = new List<string>();
        if (portfolio is not null) same.Add(portfolio);
        if (p.GithubLogin is { Length: > 0 } g) same.Add(Github(g));
        if (same.Count > 0) ld["sameAs"] = same;
        ld["makesOffer"] = p.Offerings.Select(o => new Dictionary<string, object?>
        {
            ["@type"] = "Offer", ["name"] = o.Title, ["description"] = o.Description,
            ["price"] = (o.Price.AmountMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture), ["priceCurrency"] = o.Price.Currency,
        }).ToList();
        if (p.Rating is { } r && r.Count >= MinReviewsForRating)
            ld["aggregateRating"] = new Dictionary<string, object?> { ["@type"] = "AggregateRating", ["ratingValue"] = r.Average, ["reviewCount"] = r.Count, ["bestRating"] = 5, ["worstRating"] = 1 };
        // Polish letters stay readable (the display name is in the HTML as it is); "<" is escaped so no text of a profile can close the script element or open a comment.
        return JsonSerializer.Serialize(ld, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).Replace("<", "\\u003c", StringComparison.Ordinal);
    }
}
