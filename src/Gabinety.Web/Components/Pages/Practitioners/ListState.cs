using System.Globalization;
using System.Text.RegularExpressions;

namespace Gabinety.Web.Components.Pages.Practitioners;

/// <summary>
/// The state of a list page and the links between its variants: plain URLs, because the filters are plain links (no JavaScript needed). A port of <c>web/src/lib/seo/listUrl.ts</c>: same
/// parsing limits, same link shapes, so a link of one host reads the same on the other. A plain class: tested with strings, no renderer (BLAZOR-WEB-APP section 10).
/// </summary>
public sealed partial record ListState(string? Problem = null, string? Tech = null, int? Duration = null, long? MaxPriceMinor = null, string? Q = null, string? Cursor = null, string? Sort = null, bool Contract = false)
{
    public static readonly int[] Durations = [30, 60];
    /// <summary>Price ceiling presets offered as links, in grosz (money is an integer grosz everywhere).</summary>
    public static readonly long[] PricePresets = [15000, 30000, 50000];
    /// <summary>
    /// The sort keys of the <c>sortuj</c> query value with their Directory wire values (CONTRACTS 3.2). The default (<see cref="DefaultSortKey"/>, the Directory's rank) is never
    /// written to a URL: it is read as no sort.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> SortWire = new Dictionary<string, string>
    {
        ["cena-rosnaco"] = "price_asc", ["cena-malejaco"] = "price_desc", ["termin"] = "next_slot",
    };
    public const string DefaultSortKey = "trafnosc";
    const int MaxQ = 60, MaxCursor = 512;

    [GeneratedRegex(@"^[1-9]\d{0,8}$")]
    private static partial Regex PriceFormat();

    /// <summary>The route values and the query as the page received them; anything out of range is dropped, never an error.</summary>
    public static ListState Parse(string? problem, string? tech, string? duration, string? maxPriceMinor, string? q, string? cursor, string? sort = null, string? contract = null)
    {
        var s = new ListState();
        if (!string.IsNullOrEmpty(problem)) s = s with { Problem = problem, Tech = string.IsNullOrEmpty(tech) ? null : tech };
        if (int.TryParse(duration, NumberStyles.None, CultureInfo.InvariantCulture, out var d) && Durations.Contains(d)) s = s with { Duration = d };
        if (maxPriceMinor is not null && PriceFormat().IsMatch(maxPriceMinor)) s = s with { MaxPriceMinor = long.Parse(maxPriceMinor, CultureInfo.InvariantCulture) };
        var query = q?.Trim();
        if (!string.IsNullOrEmpty(query) && query.Length <= MaxQ) s = s with { Q = query };
        if (!string.IsNullOrEmpty(cursor) && cursor.Length <= MaxCursor) s = s with { Cursor = cursor };
        if (sort is not null && SortWire.ContainsKey(sort)) s = s with { Sort = sort };
        if (contract == "1") s = s with { Contract = true }; // ADR proposal 0013: only practitioners who take collaboration proposals
        return s;
    }

    public ListState WithProblem(string? problem) => this with { Problem = problem, Tech = null, Cursor = null };
    public ListState WithTech(string? tech) => this with { Tech = tech, Cursor = null };
    public ListState WithDuration(int? duration) => this with { Duration = duration, Cursor = null };
    public ListState WithMaxPrice(long? maxPriceMinor) => this with { MaxPriceMinor = maxPriceMinor, Cursor = null };
    public ListState WithContract(bool contract) => this with { Contract = contract, Cursor = null };
    /// <summary>A new order starts again from the first page: a cursor encodes the sort it was made for. The default key is no sort.</summary>
    public ListState WithSort(string? sortKey) => this with { Sort = sortKey is not null && SortWire.ContainsKey(sortKey) ? sortKey : null, Cursor = null };
    public ListState FirstPage() => this with { Cursor = null };

    /// <summary>"Clear filters": the category and the chosen order stay, everything narrowing it goes.</summary>
    public ListState Cleared() => new(Problem, Tech, Sort: Sort);

    /// <summary>The Directory's <c>sort</c> value, or none for the rank order (the default is not sent).</summary>
    public string? ApiSort => Sort is null ? null : SortWire[Sort];

    public string Path => Problem is null ? "/programisci" : Tech is null ? $"/programisci/{Problem}" : $"/programisci/{Problem}/{Tech}";

    public string Href()
    {
        var parts = new List<string>();
        if (Duration is { } d) parts.Add($"duration={d}");
        if (MaxPriceMinor is { } m) parts.Add($"maxPriceMinor={m}");
        if (Contract) parts.Add("contract=1");
        if (!string.IsNullOrEmpty(Q)) parts.Add($"q={Uri.EscapeDataString(Q)}");
        if (Sort is not null) parts.Add($"sortuj={Sort}");
        if (!string.IsNullOrEmpty(Cursor)) parts.Add($"cursor={Uri.EscapeDataString(Cursor)}");
        return parts.Count == 0 ? Path : $"{Path}?{string.Join('&', parts)}";
    }

    /// <summary>True when the page is a filtered, sorted or paginated variant of its category (not indexed; a variant canonicals to the plain list).</summary>
    public bool IsVariant => Duration is not null || MaxPriceMinor is not null || Contract || !string.IsNullOrEmpty(Q) || Sort is not null || !string.IsNullOrEmpty(Cursor);
}
