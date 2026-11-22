using System.Reflection;
using System.Text.RegularExpressions;

namespace Gabinety.Web.Components.Pages.Static;

public enum BlockKind { Heading, Paragraph, BulletList, NumberedList }

/// <summary>One block of the owner's text. <see cref="Level"/> is the rendered heading level (2..4: the page owns the only h1); <see cref="Id"/> anchors a heading for the contents list.</summary>
public sealed record Block(BlockKind Kind, string Text = "", IReadOnlyList<string>? Items = null, int Level = 0, string? Id = null);

public sealed record LegalDoc(string Version, string Date, bool Draft, IReadOnlyList<Block> Blocks)
{
    /// <summary>The contents list: the sections (the file's <c>##</c>); the file's own <c>#</c> title is the page's second title and is not listed. A file with no <c>##</c> lists its <c>#</c>.</summary>
    public IEnumerable<Block> Headings => Blocks.Any(b => b is { Kind: BlockKind.Heading, Level: 3 }) ? Blocks.Where(b => b is { Kind: BlockKind.Heading, Level: 3 }) : Blocks.Where(b => b is { Kind: BlockKind.Heading, Level: 2 });
}

/// <summary>
/// The owner's legal text (<c>Content/Legal/*.md</c>, embedded) and the minimal Markdown subset it uses: headings, paragraphs, bulleted and numbered lists. The renderer produces
/// BLOCKS that Razor writes as encoded text, so no HTML of the file ever reaches the page (a <c>&lt;script&gt;</c> in the file is shown as text, never run). The file's <c>#</c> is the
/// page's h2 (the page owns the single h1). A file without version and date is refused: the text is the owner's data.
/// </summary>
public static partial class LegalMarkdown
{
    [GeneratedRegex(@"^---\r?\n(.*?)\r?\n---\r?\n?(.*)$", RegexOptions.Singleline)] private static partial Regex FrontMatter();
    [GeneratedRegex(@"^(#{1,3})\s+(.+)$")] private static partial Regex HeadingLine();
    [GeneratedRegex(@"^[-*]\s+(.+)$")] private static partial Regex Bullet();
    [GeneratedRegex(@"^\d+[.)]\s+(.+)$")] private static partial Regex Numbered();

    public static LegalDoc Parse(string source)
    {
        var m = FrontMatter().Match(source);
        if (!m.Success) throw new FormatException("legal_front_matter_missing");
        var meta = new Dictionary<string, string>();
        foreach (var line in m.Groups[1].Value.Split('\n'))
        {
            var i = line.IndexOf(':');
            if (i > 0) meta[line[..i].Trim()] = line[(i + 1)..].Trim();
        }
        if (!meta.TryGetValue("version", out var version) || version.Length == 0 || !meta.TryGetValue("date", out var date) || !Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$"))
            throw new FormatException("legal_version_or_date_missing");
        return new LegalDoc(version, date, meta.GetValueOrDefault("draft") == "true", Blocks(m.Groups[2].Value));
    }

    public static IReadOnlyList<Block> Blocks(string body)
    {
        var blocks = new List<Block>();
        var para = new List<string>();
        List<string>? items = null;
        var ordered = false;
        var used = new HashSet<string>();
        void FlushPara() { if (para.Count > 0) blocks.Add(new Block(BlockKind.Paragraph, string.Join(' ', para))); para.Clear(); }
        void FlushList() { if (items is not null) blocks.Add(new Block(ordered ? BlockKind.NumberedList : BlockKind.BulletList, Items: items)); items = null; }
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim();
            if (HeadingLine().Match(line) is { Success: true } h)
            {
                FlushPara(); FlushList();
                var level = h.Groups[1].Length + 1;
                blocks.Add(new Block(BlockKind.Heading, h.Groups[2].Value, Level: level, Id: IdFor(h.Groups[2].Value, used)));
            }
            else if ((Bullet().Match(line) is { Success: true } b ? b : Numbered().Match(line)) is { Success: true } li)
            {
                FlushPara();
                var isOrdered = !Bullet().IsMatch(line);
                if (items is not null && ordered != isOrdered) FlushList();
                items ??= [];
                ordered = isOrdered;
                items.Add(li.Groups[1].Value);
            }
            else if (line.Length == 0) { FlushPara(); FlushList(); }
            else { FlushList(); para.Add(line); }
        }
        FlushPara(); FlushList();
        return blocks;
    }

    /// <summary>An anchor id from a heading: ASCII letters and digits, the Polish letters folded, "-" between words, unique in the page.</summary>
    static string IdFor(string text, HashSet<string> used)
    {
        const string From = "ąćęłńóśźż", To = "acelnoszz";
        var chars = text.ToLowerInvariant().Select(c => From.IndexOf(c) is var i and >= 0 ? To[i] : c).Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        var id = Regex.Replace(new string(chars), "-+", "-").Trim('-');
        id = id.Length == 0 ? "sekcja" : "s-" + id;
        var unique = id;
        for (var n = 2; !used.Add(unique); n++) unique = $"{id}-{n}";
        return unique;
    }

    public static string Resource(string name) => $"Gabinety.Web.Content.Legal.{name}.md";

    /// <summary>The embedded text of a document (<c>regulamin</c>, <c>polityka-prywatnosci</c>).</summary>
    public static LegalDoc Load(string name)
    {
        using var stream = typeof(LegalMarkdown).Assembly.GetManifestResourceStream(Resource(name)) ?? throw new FileNotFoundException(name);
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
