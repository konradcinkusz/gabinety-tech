using System.Globalization;
using System.Text;

namespace Gabinety.Reputation.Rules;

/// <summary>Terms from configuration (Reviews:BlockedTerms), matched as whole words, case-insensitive, Polish diacritics folded.</summary>
public sealed class BlockedTermRule(IEnumerable<string>? terms) : IReviewRule
{
    readonly string[][] _terms = (terms ?? []).Select(t => Words(t)).Where(w => w.Length > 0).ToArray();
    public string Code => "blocked_term";

    public string? Check(ReviewText input)
    {
        if (_terms.Length == 0) return null;
        var words = Words(input.Clean);
        return _terms.Any(t => Contains(words, t)) ? Code : null;
    }

    static bool Contains(string[] words, string[] term)
    {
        for (var i = 0; i + term.Length <= words.Length; i++)
            if (term.Select((w, k) => words[i + k] == w).All(x => x)) return true;
        return false;
    }

    static string[] Words(string s)
    {
        var words = new List<string>();
        var cur = new StringBuilder();
        foreach (var ch in Fold(s))
        {
            if (char.IsLetterOrDigit(ch) || ch == '_') cur.Append(ch);
            else if (cur.Length > 0) { words.Add(cur.ToString()); cur.Clear(); }
        }
        if (cur.Length > 0) words.Add(cur.ToString());
        return [.. words];
    }

    /// <summary>Lower-case, decompose and drop combining marks; ł has no decomposition so it is mapped by hand.</summary>
    static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.ToLowerInvariant().Replace('ł', 'l').Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString();
    }
}
