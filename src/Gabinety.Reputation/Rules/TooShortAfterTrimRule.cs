namespace Gabinety.Reputation.Rules;

/// <summary>Fewer than 20 characters once control and zero-width characters are removed and whitespace is collapsed.</summary>
public sealed class TooShortAfterTrimRule : IReviewRule
{
    public string Code => "too_short_after_trim";
    public string? Check(ReviewText input) => input.Clean.Length < ReviewValidator.MinLength ? Code : null;
}
