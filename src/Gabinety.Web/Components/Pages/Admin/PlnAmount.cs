using System.Globalization;

namespace Gabinety.Web.Components.Pages.Admin;

public enum AmountProblem { None, Format, Decimals, Range }

/// <summary>
/// A money amount typed by an administrator: <c>12,50</c> and <c>12.50</c> are 1250 grosz. Integer arithmetic on the digits: no floating point, more than two decimals is refused and never rounded
/// (SPEC 5 rule 3). A partial refund is strictly between 0 and the price (1 to price - 1 grosz); without a known price the server checks the upper bound.
/// </summary>
public static class PlnAmount
{
    public static (AmountProblem Problem, long Grosz) Parse(string? raw)
    {
        var text = (raw ?? "").Trim();
        var separator = text.IndexOfAny(['.', ',']);
        var whole = separator < 0 ? text : text[..separator];
        var fraction = separator < 0 ? "" : text[(separator + 1)..];
        if (whole.Length is 0 or > 9 || !whole.All(char.IsAsciiDigit) || !fraction.All(char.IsAsciiDigit) || (separator >= 0 && fraction.Length == 0) || fraction.Contains('.') || fraction.Contains(','))
            return (AmountProblem.Format, 0);
        if (fraction.Length > 2) return (AmountProblem.Decimals, 0);
        return (AmountProblem.None, long.Parse(whole, CultureInfo.InvariantCulture) * 100 + long.Parse(fraction.PadRight(2, '0'), CultureInfo.InvariantCulture));
    }

    /// <summary>Range of a partial refund: 1 up to price - 1. <paramref name="priceGrosz"/> null: only the lower bound is known here.</summary>
    public static AmountProblem Range(long grosz, long? priceGrosz) => grosz < 1 || (priceGrosz is { } price && grosz > price - 1) ? AmountProblem.Range : AmountProblem.None;
}
