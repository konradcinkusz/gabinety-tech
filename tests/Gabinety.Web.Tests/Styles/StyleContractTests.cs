using System.Globalization;
using System.Text.RegularExpressions;

namespace Gabinety.Web.Tests.Stylesheets;

/// <summary>
/// BLAZOR-WEB-APP section 9: the visual design is a small frozen contract that a test reads from disk. Tokens in ONE file (wwwroot/tokens.css, replaced by the design contract's token block),
/// colour only through tokens everywhere else, both themes, AA contrast of the text pairs, focus and reduced motion, the shared class names.
/// </summary>
public partial class StyleContractTests
{
    static readonly string Tokens = Repo.Read("wwwroot", "tokens.css");
    static readonly string AppCss = Repo.Read("wwwroot", "app.css");

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)] private static partial Regex Comment();
    [GeneratedRegex(@"(--[\w-]+)\s*:\s*([^;]+);")] private static partial Regex Declaration();

    static string Strip(string css) => Comment().Replace(css, "");

    /// <summary>The light block (the first <c>:root</c>) and the dark block (inside <c>@media (prefers-color-scheme: dark)</c>).</summary>
    static (Dictionary<string, string> Light, Dictionary<string, string> Dark) Themes()
    {
        var css = Strip(Tokens);
        var dark = Regex.Match(css, @"@media\s*\(prefers-color-scheme:\s*dark\)\s*\{\s*:root\s*\{(.*?)\}\s*\}", RegexOptions.Singleline);
        Assert.True(dark.Success, "tokens.css needs a @media (prefers-color-scheme: dark) { :root { ... } } block");
        var light = Regex.Match(css[..dark.Index], @":root\s*\{(.*)\}", RegexOptions.Singleline);
        Assert.True(light.Success, "tokens.css needs a light :root block before the dark one");
        Dictionary<string, string> Read(string block) => Declaration().Matches(block).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
        return (Read(light.Groups[1].Value), Read(dark.Groups[1].Value));
    }

    [Fact]
    public void Both_themes_exist_and_dark_redefines_tokens_and_nothing_else()
    {
        var (light, dark) = Themes();
        Assert.NotEmpty(dark);
        Assert.All(dark.Keys, k => Assert.True(light.ContainsKey(k), $"dark redefines {k}, which light does not define"));
        var outside = Regex.Replace(Strip(Tokens), @"@media\s*\(prefers-color-scheme:\s*dark\)\s*\{\s*:root\s*\{.*?\}\s*\}", "", RegexOptions.Singleline);
        Assert.DoesNotContain("prefers-color-scheme", outside);
        // every theme dependent token (colours and shadows) has a dark value: dark is not an afterthought
        foreach (var k in light.Keys.Where(k => k.StartsWith("--color-") || k.StartsWith("--shadow-"))) Assert.True(dark.ContainsKey(k), $"{k} has no dark value");
        Assert.Matches(@"color-scheme:\s*light dark", Strip(Tokens));
    }

    [Fact]
    public void The_token_vocabulary_of_the_guide_is_complete()
    {
        var (light, _) = Themes();
        string[] required =
        [
            "--color-bg", "--color-surface", "--color-surface-raised", "--color-text", "--color-text-secondary", "--color-text-muted",
            "--color-accent", "--color-accent-hover", "--color-accent-contrast", "--color-accent-soft",
            "--color-success", "--color-success-soft", "--color-warning", "--color-warning-soft", "--color-danger", "--color-danger-soft",
            "--color-border", "--color-border-strong", "--color-focus", "--shadow-sm", "--shadow-md",
            "--radius-sm", "--radius-md", "--space-1", "--space-4", "--gutter", "--content-width", "--font-sans", "--font-mono", "--text-sm", "--text-md", "--text-2xl",
            // the design contract's tokens (DESIGN-CONTRACT 3.1, 3.2, 3.4)
            "--color-surface-sunken", "--color-accent-soft-text", "--color-danger-contrast", "--color-scrim", "--space-8", "--prose-width", "--form-width", "--tap", "--text-xs", "--text-3xl",
        ];
        Assert.All(required, k => Assert.True(light.ContainsKey(k), $"missing token {k}"));
        Assert.Matches(@"system-ui", light["--font-sans"]); // system fonts only
        Assert.DoesNotMatch(@"url\(|@font-face|@import", Tokens + AppCss);
        Assert.Equal("1rem", light["--gutter"]); // 16 px
        Assert.Equal("2.75rem", light["--tap"]); // 44 px
    }

    [Fact]
    public void The_documented_token_block_is_the_shipped_one()
    {
        // docs/ui/tokens.css is what the design contract links to: one source, so the document cannot drift from the product
        Assert.Equal(Tokens, File.ReadAllText(Path.Combine(Repo.Root, "docs", "ui", "tokens.css")));
    }

    // ---- colour only through tokens ----

    static readonly string[] Named = ["white", "black", "red", "green", "blue", "yellow", "orange", "purple", "gray", "grey", "silver", "navy", "teal", "maroon", "lime", "aqua", "fuchsia", "olive", "pink", "brown", "gold", "cyan", "magenta", "crimson", "tomato", "coral", "indigo", "violet", "salmon", "khaki", "lightgray", "darkgray", "darkgrey", "lightgrey", "whitesmoke", "gainsboro"];

    static IEnumerable<string> StrayColours(string css)
    {
        foreach (Match decl in Regex.Matches(Strip(css), @"(?<prop>[\w-]+)\s*:\s*(?<value>[^;{}]+)(?=[;}])"))
        {
            var value = decl.Groups["value"].Value;
            var withoutVars = Regex.Replace(value, @"var\(--[\w-]+\)", "");
            if (Regex.IsMatch(withoutVars, @"#[0-9a-fA-F]{3,8}\b")) yield return $"{decl.Groups["prop"].Value}: {value.Trim()} (hex)";
            if (Regex.IsMatch(withoutVars, @"\b(rgb|rgba|hsl|hsla|hwb|lab|lch|oklab|oklch|color|color-mix)\(", RegexOptions.IgnoreCase)) yield return $"{decl.Groups["prop"].Value}: {value.Trim()} (function)";
            foreach (var n in Named)
                if (Regex.IsMatch(withoutVars, $@"(?<![\w-]){n}(?![\w-])", RegexOptions.IgnoreCase)) yield return $"{decl.Groups["prop"].Value}: {value.Trim()} (named colour {n})";
        }
    }

    public static IEnumerable<object[]> ColourFreeFiles() =>
        new[] { "wwwroot/app.css" }.Concat(Repo.WebFiles("*.razor.css").Select(Repo.Rel)).Select(f => new object[] { f });

    [Theory, MemberData(nameof(ColourFreeFiles))]
    public void No_stray_colours_outside_the_token_file(string file)
    {
        var stray = StrayColours(File.ReadAllText(Path.Combine(Repo.Web, file))).ToList();
        Assert.True(stray.Count == 0, $"{file}: colour must come from a var(--token): {string.Join("; ", stray)}");
    }

    [Fact]
    public void The_scanner_itself_catches_a_stray_colour()
    {
        // mutation check of the rule: each of these must be reported, and a token reference must not be
        Assert.Single(StrayColours(".a { color: #fff; }"));
        Assert.Single(StrayColours(".a { background: rgb(0 0 0 / 0.5); }"));
        Assert.Single(StrayColours(".a { border: 1px solid red; }"));
        Assert.Single(StrayColours(".a { box-shadow: 0 1px 2px rgba(0,0,0,.2); }"));
        Assert.Empty(StrayColours(".a { color: var(--color-text); border: 1px solid var(--color-border); background: transparent; }"));
        Assert.Empty(StrayColours(".a { /* #fff red */ margin: 0; }"));
    }

    [Fact]
    public void Colour_tokens_are_defined_in_the_token_file_only_so_swapping_the_block_is_trivial()
    {
        foreach (var f in new[] { "wwwroot/app.css" }.Concat(Repo.WebFiles("*.razor.css").Select(Repo.Rel)))
            Assert.DoesNotMatch(@"--(color|shadow)-[\w-]+\s*:", Strip(File.ReadAllText(Path.Combine(Repo.Web, f))));
        // and every token app.css and the scoped files use is one the token file defines
        var (light, _) = Themes();
        var used = new[] { "wwwroot/app.css" }.Concat(Repo.WebFiles("*.razor.css").Select(Repo.Rel)).SelectMany(f => Regex.Matches(Strip(File.ReadAllText(Path.Combine(Repo.Web, f))), @"var\((--[\w-]+)\)").Select(m => m.Groups[1].Value)).Distinct();
        Assert.All(used, t => Assert.True(light.ContainsKey(t), $"var({t}) is used but not defined in tokens.css"));
    }

    // ---- contrast ----

    static double Luminance(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => $"{c}{c}"));
        double Channel(int i) { var c = int.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(0) + 0.7152 * Channel(1) + 0.0722 * Channel(2);
    }

    static double Contrast(string a, string b) { var (x, y) = (Luminance(a), Luminance(b)); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05); }

    public static IEnumerable<object[]> TextPairs() =>
        from theme in new[] { "light", "dark" }
        from pair in new[]
        {
            ("--color-text", "--color-bg"), ("--color-text", "--color-surface"), ("--color-text", "--color-surface-raised"),
            ("--color-text-secondary", "--color-bg"), ("--color-text-secondary", "--color-surface"), ("--color-text-secondary", "--color-surface-raised"),
            ("--color-text-muted", "--color-bg"), ("--color-text-muted", "--color-surface"),
            ("--color-accent", "--color-bg"), ("--color-accent", "--color-surface"), ("--color-accent-hover", "--color-bg"),
            ("--color-accent-contrast", "--color-accent"), ("--color-accent-contrast", "--color-accent-hover"),
            ("--color-text", "--color-danger-soft"), ("--color-text", "--color-warning-soft"), ("--color-text", "--color-success-soft"), ("--color-text", "--color-accent-soft"),
            ("--color-success", "--color-success-soft"), ("--color-danger", "--color-bg"), ("--color-danger", "--color-danger-soft"), ("--color-text-secondary", "--color-accent-soft"),
            // the remaining pairs of DESIGN-CONTRACT 3.3
            ("--color-text", "--color-surface-sunken"), ("--color-text-secondary", "--color-surface-sunken"), ("--color-text-muted", "--color-surface-raised"), ("--color-text-muted", "--color-surface-sunken"),
            ("--color-accent", "--color-surface-raised"), ("--color-accent-hover", "--color-surface-raised"), ("--color-accent-soft-text", "--color-accent-soft"), ("--color-accent", "--color-accent-soft"),
            ("--color-success", "--color-surface-raised"), ("--color-warning", "--color-warning-soft"), ("--color-warning", "--color-surface-raised"), ("--color-danger", "--color-surface-raised"),
            ("--color-danger-contrast", "--color-danger"),
        }
        select new object[] { theme, pair.Item1, pair.Item2 };

    [Theory, MemberData(nameof(TextPairs))]
    public void Text_pairs_reach_AA_contrast_in_both_themes(string theme, string fg, string bg)
    {
        var (light, dark) = Themes();
        var t = theme == "light" ? light : new Dictionary<string, string>(light.Concat(dark).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value));
        var ratio = Contrast(t[fg], t[bg]);
        Assert.True(ratio >= 4.5, $"{theme}: {fg} {t[fg]} on {bg} {t[bg]} is {ratio:0.00}:1, below 4.5:1");
    }

    [Theory]
    [InlineData("light", "--color-focus", "--color-bg")] [InlineData("dark", "--color-focus", "--color-bg")]
    [InlineData("light", "--color-border-strong", "--color-bg")] [InlineData("dark", "--color-border-strong", "--color-bg")]
    [InlineData("light", "--color-border-strong", "--color-surface-raised")] [InlineData("dark", "--color-border-strong", "--color-surface-raised")]
    [InlineData("light", "--color-focus", "--color-surface-raised")] [InlineData("dark", "--color-focus", "--color-surface-raised")]
    [InlineData("light", "--color-focus", "--color-accent-soft")] [InlineData("dark", "--color-focus", "--color-accent-soft")]
    public void Focus_rings_and_control_borders_reach_3_to_1(string theme, string fg, string bg)
    {
        var (light, dark) = Themes();
        var t = theme == "light" ? light : new Dictionary<string, string>(light.Concat(dark).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value));
        Assert.True(Contrast(t[fg], t[bg]) >= 3.0, $"{theme}: {fg} on {bg}");
    }

    // ---- behaviour of the shared sheet ----

    /// <summary>The frozen class vocabulary of DESIGN-CONTRACT section 4 that the shared sheet owns (domain components of 4.7 and the shell of 4.6 live in their scoped sheets).</summary>
    static readonly string[] SharedClasses =
    [
        ".container", ".container--form", ".container--prose", ".stack", ".stack--tight", ".stack--loose", ".cluster", ".cluster--between", ".grid-2", ".grid-2--even", ".divider", ".sr-only", ".visually-hidden", ".skip-link",
        ".card", ".card--link", ".card__header", ".card__body", ".card__footer", ".callout", ".callout--info", ".callout--success", ".callout--warning", ".callout--danger",
        ".badge", ".badge--neutral", ".badge--info", ".badge--success", ".badge--warning", ".badge--danger", ".chip", ".chip--selected", ".dl", ".prose", ".empty-state", ".skeleton", ".num", ".muted",
        ".btn", ".btn--primary", ".btn--secondary", ".btn--ghost", ".btn--danger", ".btn--ghost-danger", ".btn--block", ".link", ".tabs", ".tabs__link", ".segmented",
        ".form", ".field", ".field__label", ".field__optional", ".field__hint", ".field__control", ".field__counter", ".field__error", ".field--invalid", ".option", ".option-group", ".form-summary", ".form-actions",
        ".table-wrap", ".table", ".table--cards", ".row-actions", ".breadcrumb", ".pagination", ".dialog", ".spinner", ".loading",
    ];

    [GeneratedRegex(@"@(media|supports)[^{]*\{(?:[^{}]*\{[^{}]*\})*[^{}]*\}|@keyframes[^{]*\{(?:[^{}]*\{[^{}]*\})*[^{}]*\}", RegexOptions.Singleline)] private static partial Regex AtBlock();

    static IEnumerable<string> DuplicateSelectors(string css) =>
        Regex.Matches(AtBlock().Replace(Strip(css), ""), @"([^{}]+)\{[^{}]*\}")
            .Select(m => Regex.Replace(m.Groups[1].Value.Trim(), @"\s+", " ")) // the whole selector list of a rule: "h1, h2, h3" and "h1" are different rules
            .GroupBy(sel => sel).Where(g => g.Count() > 1).Select(g => g.Key);

    [Fact]
    public void Every_selector_of_the_shared_sheet_is_defined_once()
    {
        // stacked "additive" blocks once redefined .btn--ghost, .field__error and .form two and three times: the last one won and an edit of the first did nothing
        Assert.Empty(DuplicateSelectors(AppCss));
        Assert.Single(DuplicateSelectors(".a { margin: 0; } .b { padding: 0; } .a { padding: 0; } @media (min-width: 40rem) { .a { margin: 1rem; } }")); // the check itself works; a breakpoint override is not a duplicate
    }

    [Fact]
    public void Focus_reduced_motion_and_the_shared_classes_are_in_the_shared_sheet()
    {
        var css = Strip(AppCss);
        Assert.Contains(":focus-visible", css);
        Assert.Matches(@"@media\s*\(prefers-reduced-motion:\s*reduce\)", css);
        foreach (var cls in SharedClasses)
            Assert.Matches($@"(^|[\s,}}]){Regex.Escape(cls)}(?![\w-])", css);
        Assert.Matches(@"\.table-wrap\s*\{[^}]*overflow-x:\s*auto", css); // a wide table scrolls inside its wrapper, never the page
        Assert.Matches(@"body\s*\{[^}]*overflow-wrap:\s*break-word", css); // no horizontal page scroll at 390 px ("anywhere" would also shrink table columns to one letter)
        Assert.Matches(@":focus-visible\s*\{[^}]*outline:\s*3px", css); // DESIGN-CONTRACT 7.2
        Assert.Matches(@"\.skip-link:focus", css);
    }

    [Fact]
    public void Colour_is_never_the_only_channel_for_a_status_badge_or_callout()
    {
        // a danger callout is announced (role=alert, tested elsewhere) and carries a border, not only a tint
        Assert.Matches(@"\.callout\s*\{[^}]*border-left-width:\s*4px", Strip(AppCss));
        Assert.Matches(@"\.badge--success\s*\{[^}]*border-color", Strip(AppCss));
    }
}
