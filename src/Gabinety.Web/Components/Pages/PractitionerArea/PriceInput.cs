using System.Globalization;
using System.Text.RegularExpressions;

namespace Gabinety.Web.Components.Pages.PractitionerArea;

public enum PriceProblem { None, Format, Range }

/// <summary>
/// An offer's price as the practitioner types it ("250", "250,50", a dot works too) and as integer grosz (SPEC 5 rule 3: money is a <c>long</c> grosz). The text is read with integer arithmetic only,
/// never through a float, so 250,55 cannot become 25054. CONTRACTS 3.1: 5000 to 150000 grosz (the Directory may move these through <c>Directory:Price:*</c>; the server stays the authority).
/// </summary>
public static partial class PriceInput
{
    public const long MinGrosz = 5_000, MaxGrosz = 150_000;

    [GeneratedRegex(@"^(\d{1,7})(?:[.,](\d{1,2}))?$")] private static partial Regex Shape();

    public static (PriceProblem Problem, long Grosz) Parse(string? input)
    {
        var m = Shape().Match((input ?? "").Trim());
        if (!m.Success) return (PriceProblem.Format, 0);
        var grosz = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 100 + (m.Groups[2].Success ? long.Parse(m.Groups[2].Value.PadRight(2, '0'), CultureInfo.InvariantCulture) : 0);
        return grosz is < MinGrosz or > MaxGrosz ? (PriceProblem.Range, grosz) : (PriceProblem.None, grosz);
    }

    /// <summary>The value for the price field: <c>250</c> or <c>250,50</c>.</summary>
    public static string ToInput(long grosz) => grosz % 100 == 0 ? (grosz / 100).ToString(CultureInfo.InvariantCulture) : $"{grosz / 100},{grosz % 100:00}";
}
