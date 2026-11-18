using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Gabinety.Ranking;

namespace Gabinety.Ranking.Tests;

/// <summary>
/// CONTRACTS 9.3: a change of a ranking weight, constant or version adds a row to docs/operations/RANKING-CHANGELOG.md.
/// The last row records the SHA-256 of the canonical form of <see cref="RankingRules"/>; these tests recompute it.
/// </summary>
public class ChangelogTests
{
    const string ChangelogRelativePath = "docs/operations/RANKING-CHANGELOG.md";

    /// <summary>
    /// Canonical form (documented in RANKING-CHANGELOG.md): every public const field of RankingRules, ordered by field name (ordinal),
    /// each as one line "Name=Value\n" (LF, also after the last line); strings verbatim, integers in invariant decimal, doubles with the
    /// round-trip format "R" in the invariant culture; the whole text is UTF-8 without a BOM; the hash is lowercase hex SHA-256.
    /// Reflection means a constant added later changes the hash instead of escaping it.
    /// </summary>
    internal static string CanonicalForm()
    {
        var fields = typeof(RankingRules).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.IsLiteral && !f.IsInitOnly)
            .OrderBy(f => f.Name, StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var f in fields)
            sb.Append(f.Name).Append('=').Append(Format(f.GetRawConstantValue())).Append('\n');
        return sb.ToString();
    }

    static string Format(object? v) => v switch
    {
        string s => s,
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException($"RankingRules has a constant of type {v?.GetType().Name}; extend the canonical form in ChangelogTests and RANKING-CHANGELOG.md.")
    };

    internal static string Hash(string canonical) =>
        Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(canonical))).ToLowerInvariant();

    internal sealed record Row(string Date, string Version, string Hash);

    /// <summary>
    /// Reads the changelog table: the first Markdown table whose header has the columns Date, Version and Hash (any order, any case,
    /// extra columns allowed). Tolerates CRLF, uneven spacing, missing outer pipes, backticks or bold around a cell, and text around the table.
    /// </summary>
    internal static IReadOnlyList<Row> ParseRows(string markdown)
    {
        var rows = new List<Row>();
        int date = -1, version = -1, hash = -1;
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (!line.Contains('|')) { if (date >= 0) break; continue; }   // the table ended
            var cells = SplitCells(line);
            if (date < 0)
            {
                var names = cells.Select(c => Clean(c).ToLowerInvariant()).ToList();
                date = names.IndexOf("date"); version = names.IndexOf("version"); hash = names.IndexOf("hash");
                if (date < 0 || version < 0 || hash < 0) date = version = hash = -1;
                continue;
            }
            if (cells.All(c => Regex.IsMatch(c.Trim(), @"^:?-{1,}:?$"))) continue;   // the |---|---| separator
            var max = Math.Max(date, Math.Max(version, hash));
            if (cells.Count <= max) continue;
            rows.Add(new Row(Clean(cells[date]), Clean(cells[version]), Clean(cells[hash]).ToLowerInvariant()));
        }
        return rows;
    }

    static List<string> SplitCells(string line)
    {
        if (line.StartsWith('|')) line = line[1..];
        if (line.EndsWith('|')) line = line[..^1];
        return line.Split('|').ToList();
    }

    static string Clean(string cell) => cell.Trim().Trim('`', '*', '_').Trim();

    static string ReadChangelog()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ChangelogRelativePath);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException($"{ChangelogRelativePath} not found above {AppContext.BaseDirectory}.");
    }

    [Fact]
    public void The_last_changelog_row_records_the_current_rules()
    {
        var rows = ParseRows(ReadChangelog());
        Assert.True(rows.Count > 0, $"{ChangelogRelativePath} has no table rows (columns Date, Version, Hash).");
        var last = rows[^1];
        var actual = Hash(CanonicalForm());

        Assert.True(last.Hash == actual && last.Version == RankingRules.Version,
            "RankingRules no longer matches the last row of " + ChangelogRelativePath + ".\n" +
            $"  RankingRules.Version now : {RankingRules.Version}\n" +
            $"  hash of RankingRules now : {actual}\n" +
            $"  last changelog row       : version {last.Version}, hash {last.Hash}\n" +
            "If you changed a ranking weight, constant or the version on purpose (CONTRACTS 9.3), then in the same PR:\n" +
            "  1. bump RankingRules.Version to a NEW string (the changelog must not repeat a version);\n" +
            $"  2. append a NEW LAST row to {ChangelogRelativePath} with the date, that version, hash {actual}, the constants and why (do not edit the old rows);\n" +
            "  3. update the public page /zasady-rankingu if its wording needs it (its numbers come from GET /api/v1/ranking-rules).\n" +
            "If you did not mean to change the rules, revert your change to RankingRules.\n" +
            "Canonical form that was hashed:\n" + CanonicalForm());
    }

    [Fact]
    public void Every_changelog_row_has_a_distinct_version_and_a_sha256_hash()
    {
        var rows = ParseRows(ReadChangelog());
        Assert.All(rows, r => Assert.Matches("^[0-9a-f]{64}$", r.Hash));
        Assert.Equal(rows.Count, rows.Select(r => r.Version).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_documented_canonical_text_of_v1_reproduces_its_recorded_hash()
    {
        // The text below is the one printed in RANKING-CHANGELOG.md. It is frozen history, so it never changes with the rules.
        const string v1 = "M=5\nMinReviewsToShowRating=3\nMu=3.8\nSlotsForFullAvailability=10\nVersion=2026-11-v1\n" +
                          "WeightAvailability=0.15\nWeightCompleteness=0.15\nWeightRating=0.45\nWeightReliability=0.25\n";
        var row = Assert.Single(ParseRows(ReadChangelog()), r => r.Version == "2026-11-v1");
        Assert.Equal(row.Hash, Hash(v1));
    }

    [Fact]
    public void The_parser_reads_the_last_row_of_an_untidy_table()
    {
        const string md = "# Title\r\n\r\nintro | with a pipe\r\n\r\n| Date | Version | Why | Hash |\r\n|:--|---|---|---:|\r\n" +
                          "|2026-11-16|`v1`|first|`AAAA`|\r\n  | 2027-01-01 | **v2** | second | bb |  \r\n\r\nnot a row\r\n";
        var rows = ParseRows(md);
        Assert.Equal(new[] { "v1", "v2" }, rows.Select(r => r.Version));
        Assert.Equal("bb", rows[^1].Hash);
        Assert.Equal("aaaa", rows[0].Hash);
    }
}
