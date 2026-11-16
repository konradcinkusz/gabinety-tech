using System.Globalization;
using System.Text;

namespace Gabinety.Reputation.Rules;

/// <summary>
/// The text a rule looks at: <see cref="Clean"/> is the input with control and zero-width (format) characters removed,
/// whitespace runs collapsed to one space and the ends trimmed. Pure; never logged.
/// </summary>
public sealed record ReviewText(string Raw, string Clean)
{
    public static ReviewText From(string? raw)
    {
        raw ??= "";
        var sb = new StringBuilder(raw.Length);
        var pendingSpace = false;
        foreach (var ch in raw)
        {
            if (char.IsWhiteSpace(ch)) { pendingSpace = sb.Length > 0; continue; }
            var cat = char.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.Control or UnicodeCategory.Format) continue;
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(ch);
        }
        return new ReviewText(raw, sb.ToString());
    }
}

public interface IReviewRule
{
    /// <summary>The reason code this rule reports (CONTRACTS 5.3).</summary>
    string Code { get; }

    /// <summary>The reason code when the text trips the rule, otherwise null. Never throws.</summary>
    string? Check(ReviewText input);
}
