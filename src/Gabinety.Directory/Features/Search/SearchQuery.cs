using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Gabinety.Directory.Features.Offerings;

namespace Gabinety.Directory.Features.Search;

/// <summary>One active offering must satisfy every given filter at once (CONTRACTS 3.2).</summary>
public sealed record OfferingFilter(string? Problem, string? Tech, int? Duration, long? MaxPriceMinor)
{
    public IQueryable<Offering> Apply(IQueryable<Offering> offerings)
    {
        var q = offerings.Where(o => o.IsActive);
        if (Problem is { } p) q = q.Where(o => o.ProblemSlug == p);
        if (Tech is { } t) q = q.Where(o => o.TechSlugs.Contains(t)); // `@>`/ANY on text[] in Npgsql, a list on InMemory
        if (Duration is { } d) { var dm = (short)d; q = q.Where(o => o.DurationMinutes == dm); }
        if (MaxPriceMinor is { } m) q = q.Where(o => o.PriceMinor <= m);
        return q;
    }

    /// <summary>The same rule in memory, for the page's already loaded offerings (fromPrice).</summary>
    public bool Matches(Offering o) => o.IsActive && (Problem is null || o.ProblemSlug == Problem) && (Tech is null || o.TechSlugs.Contains(Tech))
        && (Duration is null || o.DurationMinutes == Duration) && (MaxPriceMinor is null || o.PriceMinor <= MaxPriceMinor);
}

public sealed record Cursor(decimal Score, DateTimeOffset ApprovedAt, Guid Id)
{
    public string Encode() => Base64Url.EncodeToString(Encoding.ASCII.GetBytes(
        $"{Score.ToString(CultureInfo.InvariantCulture)}|{ApprovedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{Id:N}"));

    public static bool TryDecode(string text, out Cursor? cursor)
    {
        cursor = null;
        try
        {
            var parts = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(text)).Split('|');
            if (parts.Length != 3 || !decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var score)
                || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks is < 0 or > 3155378975999999999 || !Guid.TryParseExact(parts[2], "N", out var id)) return false;
            cursor = new Cursor(score, new DateTimeOffset(ticks, TimeSpan.Zero), id);
            return true;
        }
        catch (Exception e) when (e is FormatException or ArgumentException) { return false; }
    }
}

/// <summary>The validated query string of the list. <see cref="Error"/> names the first bad parameter.</summary>
public sealed class SearchQuery
{
    public const int DefaultLimit = 12, MaxLimit = 24, MaxQ = 60;
    static readonly Regex Slug = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public OfferingFilter Filter { get; private set; } = new(null, null, null, null);
    public string? Q { get; private set; }
    public Cursor? After { get; private set; }
    public int Limit { get; private set; } = DefaultLimit;
    public (string Field, string Message)? Error { get; private set; }

    public static SearchQuery Parse(IQueryCollection query)
    {
        var s = new SearchQuery();
        string? problem = null, tech = null, q = null, cursor = null;
        int? duration = null; long? max = null;

        bool Single(string key, out string? value)
        {
            value = null;
            var v = query[key];
            if (v.Count > 1) return s.Fail(key, "give it once");
            if (v.Count == 1) value = v[0];
            return true;
        }

        if (!Single("problem", out problem) || !Single("tech", out tech) || !Single("q", out q) || !Single("cursor", out cursor)
            || !Single("duration", out var d) || !Single("maxPriceMinor", out var m) || !Single("limit", out var l)) return s;

        if (problem is not null && !ValidSlug(problem)) { s.Fail("problem", "a slug of a-z, 0-9 and single hyphens"); return s; }
        if (tech is not null && !ValidSlug(tech)) { s.Fail("tech", "a slug of a-z, 0-9 and single hyphens"); return s; }
        if (d is not null)
        {
            if (d is not ("30" or "60")) { s.Fail("duration", "30 or 60"); return s; }
            duration = int.Parse(d, CultureInfo.InvariantCulture);
        }
        if (m is not null)
        {
            if (!long.TryParse(m, NumberStyles.None, CultureInfo.InvariantCulture, out var mm)) { s.Fail("maxPriceMinor", "a non-negative integer"); return s; }
            max = mm;
        }
        if (l is not null)
        {
            if (!int.TryParse(l, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 1) { s.Fail("limit", "an integer of at least 1"); return s; }
            s.Limit = Math.Min(n, MaxLimit);
        }
        if (q is not null)
        {
            var t = q.Trim();
            if (t.Length > MaxQ) { s.Fail("q", "at most 60 characters"); return s; }
            s.Q = t.Length == 0 ? null : t;
        }
        if (cursor is not null)
        {
            if (!Cursor.TryDecode(cursor, out var c)) { s.Fail("cursor", "not a cursor of this list"); return s; }
            s.After = c;
        }
        s.Filter = new OfferingFilter(problem, tech, duration, max);
        return s;
    }

    static bool ValidSlug(string v) => v.Length <= 60 && Slug.IsMatch(v);

    bool Fail(string field, string message) { Error = (field, message); return false; }
}
