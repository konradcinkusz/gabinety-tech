using Gabinety.Web.Api;
using Gabinety.Web.Components.Shared;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Practitioners;

public sealed record FilterItem(string Label, string Href, bool Current);

public sealed record FilterGroup(string Title, IReadOnlyList<FilterItem> Items, bool Segmented = false);

/// <summary>
/// What a list page says about itself, from its state and what the Directory returned: titles, counts, the filter links and the SEO verdict. Plain C#: the component only renders it.
/// SEO rules (SPEC 6.5): unique title and description, canonical URL (a variant points at the plain list), <c>noindex,follow</c> on a category with fewer than 3 approved practitioners
/// (the API's <c>indexable</c> flag) and on every filtered or paginated variant.
/// </summary>
public sealed class ListView(ListState state, TaxonomyDto taxonomy, SitemapDto sitemap)
{
    public ListState State { get; } = state;
    public string? ProblemName { get; } = state.Problem is null ? null : taxonomy.Problems.FirstOrDefault(c => c.Slug == state.Problem)?.NamePl;
    public string? TechName { get; } = state.Tech is null ? null : taxonomy.Techs.FirstOrDefault(c => c.Slug == state.Tech)?.NamePl;

    /// <summary>An unknown problem or technology is a 404, not an empty list.</summary>
    public bool IsKnown => (State.Problem is null || ProblemName is not null) && (State.Tech is null || TechName is not null);

    string Title => TechName is not null ? $"{ProblemName}: {TechName}" : ProblemName ?? Pl.ListPage.AllTitle;
    public string Heading => Title;
    public string Intro => TechName is not null ? string.Format(Pl.ListPage.CategoryTechIntro, Title) : ProblemName is not null ? string.Format(Pl.ListPage.CategoryIntro, Title) : Pl.ListPage.AllIntro;

    SitemapCategoryDto? Category => sitemap.Categories.FirstOrDefault(c => c.Path == State.Path);
    public int Count => Category?.PractitionerCount ?? 0;

    /// <summary>
    /// The count line (F-05): it must never contradict the list. The unfiltered first page of a category is counted by the sitemap; a filtered variant or a later page has no total (the
    /// Directory pages by cursor), so its count is the page's own items only when that page is the whole result (first page, no next cursor), and is omitted otherwise.
    /// </summary>
    public string? CountText(int itemsOnPage, bool hasNextPage) => ResultCount(itemsOnPage, hasNextPage) is { } n ? string.Format(Pl.ListPage.Count, n) : null;

    /// <summary>The number the count line states, or null when the page cannot state one (see <see cref="CountText"/>).</summary>
    public int? ResultCount(int itemsOnPage, bool hasNextPage) =>
        !State.IsVariant ? (itemsOnPage == 0 && !hasNextPage ? 0 : Count)
        : State.Cursor is null && !hasNextPage ? itemsOnPage : null;

    /// <summary>The Polish names of the problem categories, by slug: a card shows its problems by name, and a slug the taxonomy does not know is not shown.</summary>
    public IReadOnlyDictionary<string, string> ProblemNames { get; } = taxonomy.Problems.DistinctBy(c => c.Slug).ToDictionary(c => c.Slug, c => c.NamePl);

    /// <summary>
    /// The active narrowing filters as removable chips: each link drops that one filter and keeps the rest. The problem and the technology are the page itself (its path, heading and
    /// breadcrumb), not a filter, so they are not chips; "Wyczyść filtry" keeps them for the same reason.
    /// </summary>
    public IReadOnlyList<FilterItem> ActiveChips()
    {
        var chips = new List<FilterItem>();
        if (State.Duration is { } d) chips.Add(new FilterItem(string.Format(Pl.Filters.Minutes, d), State.WithDuration(null).Href(), false));
        if (State.MaxPriceMinor is { } price) chips.Add(new FilterItem(string.Format(Pl.Filters.UpTo, ListFormat.Price(price)), State.WithMaxPrice(null).Href(), false));
        if (!string.IsNullOrEmpty(State.Q)) chips.Add(new FilterItem(string.Format(Pl.ListPage.SearchChip, State.Q), (State with { Q = null, Cursor = null }).Href(), false));
        return chips;
    }

    /// <summary>The number of active narrowing filters: the "Filtry (2)" of the phone disclosure.</summary>
    public int ActiveFilters => (State.Problem is null ? 0 : 1) + (State.Tech is null ? 0 : 1) + (State.Duration is null ? 0 : 1) + (State.MaxPriceMinor is null ? 0 : 1) + (string.IsNullOrEmpty(State.Q) ? 0 : 1);

    /// <summary>The path to this category: Programiści, the problem, the technology (the last is the current page).</summary>
    public IReadOnlyList<(string Label, string? Href)> Breadcrumb()
    {
        if (ProblemName is null) return [];
        var crumbs = new List<(string, string?)> { (Pl.ListPage.AllTitle, "/programisci"), (ProblemName, TechName is null ? null : $"/programisci/{State.Problem}") };
        if (TechName is not null) crumbs.Add((TechName, null));
        return crumbs;
    }
    public bool IsIndexable => !State.IsVariant && (Category?.Indexable ?? false);
    public string Robots => IsIndexable ? "index,follow" : "noindex,follow";

    public string MetaTitle => TechName is not null ? string.Format(Pl.ListPage.MetaTechTitle, ProblemName, TechName)
        : ProblemName is not null ? string.Format(Pl.ListPage.MetaProblemTitle, ProblemName) : Pl.ListPage.MetaAllTitle;
    public string MetaDescription => TechName is not null ? string.Format(Pl.ListPage.MetaTechDescription, ProblemName, TechName, Count)
        : ProblemName is not null ? string.Format(Pl.ListPage.MetaProblemDescription, ProblemName, Count) : Pl.ListPage.MetaAllDescription;

    /// <summary>A variant points at the plain list.</summary>
    public string CanonicalPath => State.Path;

    public IReadOnlyList<FilterGroup> Filters()
    {
        var any = Pl.Filters.Any;
        var problems = new List<FilterItem> { new(any, State.WithProblem(null).Href(), State.Problem is null) };
        problems.AddRange(taxonomy.Problems.Select(c => new FilterItem(c.NamePl, State.WithProblem(c.Slug).Href(), State.Problem == c.Slug)));
        var groups = new List<FilterGroup> { new(Pl.Filters.Problem, problems) };
        if (State.Problem is not null)
        {
            var techs = new List<FilterItem> { new(any, State.WithTech(null).Href(), State.Tech is null) };
            techs.AddRange(taxonomy.Techs.Select(c => new FilterItem(c.NamePl, State.WithTech(c.Slug).Href(), State.Tech == c.Slug)));
            groups.Add(new FilterGroup(Pl.Filters.Tech, techs));
        }
        var durations = new List<FilterItem> { new(any, State.WithDuration(null).Href(), State.Duration is null) };
        durations.AddRange(ListState.Durations.Select(d => new FilterItem(string.Format(Pl.Filters.Minutes, d), State.WithDuration(d).Href(), State.Duration == d)));
        groups.Add(new FilterGroup(Pl.Filters.Duration, durations, Segmented: true));
        var prices = new List<FilterItem> { new(Pl.Filters.AnyPrice, State.WithMaxPrice(null).Href(), State.MaxPriceMinor is null) };
        prices.AddRange(ListState.PricePresets.Select(p => new FilterItem(string.Format(Pl.Filters.UpTo, Money.Pln(p)), State.WithMaxPrice(p).Href(), State.MaxPriceMinor == p)));
        groups.Add(new FilterGroup(Pl.Filters.Price, prices, Segmented: true));
        return groups;
    }

}
