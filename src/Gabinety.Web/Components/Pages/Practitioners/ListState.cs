using System.Globalization;
using System.Text.RegularExpressions;

namespace Gabinety.Web.Components.Pages.Practitioners;

/// <summary>
/// The state of a list page and the links between its variants: plain URLs, because the filters are plain links (no JavaScript needed). A port of <c>web/src/lib/seo/listUrl.ts</c>: same
/// parsing limits, same link shapes, so a link of one host reads the same on the other. A plain class: tested with strings, no renderer (BLAZOR-WEB-APP section 10).
/// </summary>
public sealed partial record ListState(string? Problem = null, string? Tech = null, int? Duration = null, long? MaxPriceMinor = null, string? Q = null, string? Cursor = null)
{
    public static readonly int[] Durations = [30, 60];
    /// <summary>Price ceiling presets offered as links, in grosz (money is an integer grosz everywhere).</summary>
    public static readonly long[] PricePresets = [15000, 30000, 50000];
    const int MaxQ = 60, MaxCursor = 512;

    [GeneratedRegex(@"^[1-9]\d{0,8}$")]
    private static partial Regex PriceFormat();

    /// <summary>The route values and the query as the page received them; anything out of range is dropped, never an error.</summary>
    public static ListState Parse(string? problem, string? tech, string? duration, string? maxPriceMinor, string? q, string? cursor)
    {
        var s = new ListState();
        if (!string.IsNullOrEmpty(problem)) s = s with { Problem = problem, Tech = string.IsNullOrEmpty(tech) ? null : tech };
        if (int.TryParse(duration, NumberStyles.None, CultureInfo.InvariantCulture, out var d) && Durations.Contains(d)) s = s with { Duration = d };
        if (maxPriceMinor is not null && PriceFormat().IsMatch(maxPriceMinor)) s = s with { MaxPriceMinor = long.Parse(maxPriceMinor, CultureInfo.InvariantCulture) };
        var query = q?.Trim();
        if (!string.IsNullOrEmpty(query) && query.Length <= MaxQ) s = s with { Q = query };
        if (!string.IsNullOrEmpty(cursor) && cursor.Length <= MaxCursor) s = s with { Cursor = cursor };
        return s;
    }

    public ListState WithProblem(string? problem) => this with { Problem = problem, Tech = null, Cursor = null };
    public ListState WithTech(string? tech) => this with { Tech = tech, Cursor = null };
    public ListState WithDuration(int? duration) => this with { Duration = duration, Cursor = null };
    public ListState WithMaxPrice(long? maxPriceMinor) => this with { MaxPriceMinor = maxPriceMinor, Cursor = null };
    public ListState FirstPage() => this with { Cursor = null };

    /// <summary>"Clear filters": the category stays, everything narrowing it goes.</summary>
    public ListState Cleared() => new(Problem, Tech);

    public string Path => Problem is null ? "/programisci" : Tech is null ? $"/programisci/{Problem}" : $"/programisci/{Problem}/{Tech}";

    public string Href()
    {
        var parts = new List<string>();
        if (Duration is { } d) parts.Add($"duration={d}");
        if (MaxPriceMinor is { } m) parts.Add($"maxPriceMinor={m}");
        if (!string.IsNullOrEmpty(Q)) parts.Add($"q={Uri.EscapeDataString(Q)}");
        if (!string.IsNullOrEmpty(Cursor)) parts.Add($"cursor={Uri.EscapeDataString(Cursor)}");
        return parts.Count == 0 ? Path : $"{Path}?{string.Join('&', parts)}";
    }

    /// <summary>True when the page is a filtered or paginated variant of its category (such variants are not indexed).</summary>
    public bool IsVariant => Duration is not null || MaxPriceMinor is not null || !string.IsNullOrEmpty(Q) || !string.IsNullOrEmpty(Cursor);
}
