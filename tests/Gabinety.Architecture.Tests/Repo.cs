using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Gabinety.Architecture.Tests;

/// <summary>Read-only view of the repository tree used by the architecture rules.</summary>
static class Repo
{
    public static readonly string Root = FindRoot();

    static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "GabinetyTech.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("solution root (GabinetyTech.sln) not found");
    }

    public static string Rel(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    static bool IsBuildOutput(string f) => f.Contains("/obj/") || f.Contains("/bin/") || f.Contains("\\obj\\") || f.Contains("\\bin\\");

    public static IEnumerable<string> Files(string subDir, string pattern) =>
        System.IO.Directory.EnumerateFiles(Path.Combine(Root, subDir), pattern, SearchOption.AllDirectories).Where(f => !IsBuildOutput(f)).Order();

    /// <summary>One project file under src/ or tools/ with its project and package references.</summary>
    public sealed record Project(string Name, string Dir, string File, IReadOnlyList<string> ProjectRefs, IReadOnlyList<string> PackageRefs);

    public static IReadOnlyList<Project> Projects() =>
        new[] { "src", "tools" }.SelectMany(d => Files(d, "*.csproj")).Select(f =>
        {
            var xml = XDocument.Load(f);
            string[] Includes(string item) => xml.Descendants(item).Select(e => (string?)e.Attribute("Include")).OfType<string>().ToArray();
            return new Project(Path.GetFileNameWithoutExtension(f), Path.GetDirectoryName(f)!, Rel(f),
                Includes("ProjectReference").Select(p => Path.GetFileNameWithoutExtension(p.Replace('\\', '/'))).Order().ToArray(),
                Includes("PackageReference").Order().ToArray());
        }).ToList();

    /// <summary>C# files under src/ with comments removed and plain string contents blanked (see <see cref="CodeScan.Sanitize"/>).</summary>
    public static IEnumerable<(string Rel, string Code)> SourceFiles(string subDir = "src") =>
        Files(subDir, "*.cs").Select(f => (Rel(f), CodeScan.Sanitize(File.ReadAllText(f))));
}

/// <summary>Small lexical scanners. Not a parser: precise enough for the guards, and unit-tested in <c>ScannerTests</c>.</summary>
static partial class CodeScan
{
    /// <summary>
    /// Removes // and /* */ comments. Contents of ordinary, verbatim and raw string literals become empty;
    /// in interpolated strings only the code inside {holes} is kept. Newlines are preserved.
    /// </summary>
    public static string Sanitize(string s)
    {
        var o = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; i--; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) { if (s[i] == '\n') o.Append('\n'); i++; }
                i++; continue;
            }
            if (c == '\'' ) { var j = i + 1; if (j < s.Length && s[j] == '\\') j++; j++; if (j < s.Length && s[j] == '\'') { o.Append("''"); i = j; continue; } }
            var interp = false; var verbatim = false; var p = i;
            while (p < s.Length && (s[p] == '$' || s[p] == '@')) { if (s[p] == '$') interp = true; else verbatim = true; p++; }
            if (p < s.Length && s[p] == '"' && (p == i || !char.IsLetterOrDigit(i > 0 ? s[i - 1] : ' ')))
            {
                var q = p; while (q < s.Length && s[q] == '"') q++;
                var quotes = q - p;
                var raw = quotes >= 3;
                i = SkipString(s, o, raw ? q : p + 1, raw ? quotes : 1, raw, verbatim, interp);
                continue;
            }
            o.Append(c);
        }
        return o.ToString();
    }

    static int SkipString(string s, StringBuilder o, int i, int quotes, bool raw, bool verbatim, bool interp)
    {
        o.Append("\"\"");
        for (; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\n') o.Append('\n');
            if (raw ? Closes(s, i, quotes) : c == '"' && !(verbatim && i + 1 < s.Length && s[i + 1] == '"')) return i + (raw ? quotes - 1 : 0);
            if (verbatim && c == '"') { i++; continue; }
            if (!raw && !verbatim && c == '\\') { i++; continue; }
            if (interp && c == '{')
            {
                if (i + 1 < s.Length && s[i + 1] == '{') { i++; continue; }
                var depth = 1; o.Append(' ');
                for (i++; i < s.Length && depth > 0; i++) { if (s[i] == '{') depth++; else if (s[i] == '}') { if (--depth == 0) break; } o.Append(s[i]); }
                o.Append(' ');
            }
        }
        return i;
    }

    static bool Closes(string s, int i, int quotes) { for (var k = 0; k < quotes; k++) if (i + k >= s.Length || s[i + k] != '"') return false; return true; }

    [GeneratedRegex(@"\b(?:Log(?:Trace|Debug|Information|Warning|Error|Critical)|Log|[A-Za-z_]*[lL]ogger[A-Za-z_]*\s*\.\s*[A-Za-z_]\w*)\s*(?:<[^>()]*>)?\s*\(")]
    private static partial Regex LogCall();

    // A forbidden identifier as a whole word; `.Length` right after it (a length, not the text) is allowed.
    [GeneratedRegex(@"(?<![\w])(Brief|BriefText|Bio|Text|Reply)\b(?!\s*\??\.\s*Length\b)")]
    private static partial Regex FreeTextName();

    /// <summary>Logging calls in sanitized code whose argument list names a free-text member. Returns (line, offending name).</summary>
    public static IEnumerable<(int Line, string Name)> FreeTextInLogCalls(string sanitized)
    {
        foreach (Match m in LogCall().Matches(sanitized))
        {
            var start = m.Index + m.Length; var depth = 1; var end = start;
            while (end < sanitized.Length && depth > 0) { if (sanitized[end] == '(') depth++; else if (sanitized[end] == ')') depth--; end++; }
            var hit = FreeTextName().Match(sanitized[start..end]);
            if (hit.Success) yield return (sanitized.AsSpan(0, m.Index).Count('\n') + 1, hit.Value);
        }
    }
}
