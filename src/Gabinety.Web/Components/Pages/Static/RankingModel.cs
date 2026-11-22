using Gabinety.Web.Api;

namespace Gabinety.Web.Components.Pages.Static;

public sealed record RankingInput(int N, double R, int C, int X, int S, double P, int K);

public sealed record RankingPart(string Key, double Value, double Weight, double Points);

/// <summary>The worked example of <c>/zasady-rankingu</c> (CONTRACTS 9.3), computed from the weights and constants the Directory returned, never from numbers of this project (stale numbers are not the rules).</summary>
public static class RankingModel
{
    /// <summary>The inputs of the example: the second published vector of CONTRACTS 9.3.</summary>
    public static readonly RankingInput Example = new(10, 4.8, 20, 1, 0, 0.8, 5);

    public static readonly string[] PartKeys = ["rating", "reliability", "availability", "completeness"];

    public static (IReadOnlyList<RankingPart> Parts, double Score) Worked(RankingRulesDto rules, RankingInput? input = null)
    {
        var i = input ?? Example;
        var k = rules.Constants;
        var values = new[]
        {
            (i.N * i.R + k.M * k.Mu) / (i.N + k.M) / 5,
            (i.C + 3.0) / (i.C + i.X + i.S + 4),
            Math.Min(1, (double)i.K / k.SlotsForFullAvailability),
            i.P,
        };
        var weights = new[] { rules.Weights.Rating, rules.Weights.Reliability, rules.Weights.Availability, rules.Weights.Completeness };
        var parts = PartKeys.Select((key, n) => new RankingPart(key, values[n], weights[n], 100 * weights[n] * values[n])).ToList();
        // Round half away from zero to two decimals (CONTRACTS 9.3).
        return (parts, Math.Round(parts.Sum(p => p.Points), 2, MidpointRounding.AwayFromZero));
    }
}

/// <summary>The commission shown on <c>/cennik</c> (D-23, CONTRACTS 9.1): the constant is the owner's, the rate is runtime configuration under the key the Billing service reads.</summary>
public static class Fee
{
    public const int DefaultBps = 1500;
    public const string ConfigKey = "Billing:FirstConsultationFeeBps";
    /// <summary>An example price of 250,00 zł: the first published vector of CONTRACTS 9.1.</summary>
    public const long ExamplePriceMinor = 25_000;
    /// <summary>The second example of <c>/cennik</c>: an illustrative consultation of 120,00 zł. It is not a published vector and not an owner's constant; the split is computed with the same rule and the same rate.</summary>
    public const long SecondExamplePriceMinor = 12_000;

    /// <summary>An integer in 0..10000; anything else falls back to the default.</summary>
    public static int BpsFrom(string? raw) => raw?.Trim() is { Length: > 0 } t && t.All(char.IsAsciiDigit) && int.TryParse(t, out var bps) && bps <= 10_000 ? bps : DefaultBps;

    /// <summary>CONTRACTS 9.1: (price * bps + 5000) / 10000 with integer division, which rounds half up.</summary>
    public static long For(long priceMinor, int bps) => (priceMinor * bps + 5000) / 10_000;
}
