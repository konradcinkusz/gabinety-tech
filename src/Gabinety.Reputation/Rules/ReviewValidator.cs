namespace Gabinety.Reputation.Rules;

/// <summary>Validation that runs BEFORE the rules: the trimmed text must be 20 to 2000 characters.</summary>
public sealed class ReviewValidator
{
    public const int MinLength = 20, MaxLength = 2000;

    public IReadOnlyList<string> Validate(string? text)
    {
        var length = text?.Trim().Length ?? 0;
        if (length == 0) return ["text_required"];
        if (length < MinLength) return ["text_too_short"];
        if (length > MaxLength) return ["text_too_long"];
        return [];
    }
}
