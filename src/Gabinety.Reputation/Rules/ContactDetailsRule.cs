using System.Text.RegularExpressions;

namespace Gabinety.Reputation.Rules;

/// <summary>E-mail, phone number, @handle, t.me / wa.me link, or a messenger name followed by a handle or number.</summary>
public sealed partial class ContactDetailsRule : IReviewRule
{
    const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
    public string Code => "contact_details";

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+", O)] private static partial Regex Email();
    [GeneratedRegex(@"\B@\w{2,}", O)] private static partial Regex Handle();
    [GeneratedRegex(@"\b(?:t|wa)\.me/", O)] private static partial Regex MessengerLink();
    // Digit groups separated by single spaces or dashes, optional leading +: counted afterwards (7 or more digits).
    [GeneratedRegex(@"\+?\d{2,}(?:[ -]\d{2,})+|\d{9}", O)] private static partial Regex Phone();
    [GeneratedRegex(@"\b(?:whatsapp|telegram|signal|discord)\b.{0,20}?(?:@\w+|\+?\d(?:[ -]?\d){5,})", O)] private static partial Regex Messenger();
    [GeneratedRegex(@"^(?:\d{4}-\d{1,2}-\d{1,2}|\d{1,2}-\d{1,2}-\d{4})$", O)] private static partial Regex Date();

    public string? Check(ReviewText input)
    {
        var t = input.Clean;
        if (Email().IsMatch(t) || Handle().IsMatch(t) || MessengerLink().IsMatch(t) || Messenger().IsMatch(t)) return Code;
        foreach (Match m in Phone().Matches(t))
            if (m.Value.Count(char.IsAsciiDigit) >= 7 && !Date().IsMatch(m.Value)) return Code;
        return null;
    }
}
