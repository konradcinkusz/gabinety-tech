namespace Gabinety.Reputation.Rules;

/// <summary>A character, or a word (case-insensitive), repeated more than 8 times in a row.</summary>
public sealed class RepetitionRule : IReviewRule
{
    const int Max = 8;
    public string Code => "repetition";

    public string? Check(ReviewText input) => CharRun(input.Clean) || WordRun(input.Clean) ? Code : null;

    static bool CharRun(string s)
    {
        var run = 0;
        for (var i = 0; i < s.Length; i++)
        {
            run = i > 0 && char.ToLowerInvariant(s[i]) == char.ToLowerInvariant(s[i - 1]) ? run + 1 : 1;
            if (run > Max) return true;
        }
        return false;
    }

    static bool WordRun(string s)
    {
        string? prev = null;
        var run = 0;
        foreach (var w in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var word = w.Trim(',', '.', ';', ':', '!', '?', '"', '\'', '(', ')', '-');
            if (word.Length == 0) continue;
            run = prev != null && string.Equals(prev, word, StringComparison.OrdinalIgnoreCase) ? run + 1 : 1;
            prev = word;
            if (run > Max) return true;
        }
        return false;
    }
}
