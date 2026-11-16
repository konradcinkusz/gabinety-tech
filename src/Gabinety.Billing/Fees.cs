namespace Gabinety.Billing;

public readonly record struct FeeResult(Money Kept, Money Fee, Money Payout);

/// <summary>CONTRACTS 9.1. Integer arithmetic only; overflow throws.</summary>
public static class Fees
{
    public static FeeResult Calculate(Money price, int rateBps, bool pairAlreadyCharged)
    {
        if (price.Minor < 0) throw new ArgumentOutOfRangeException(nameof(price));
        if (rateBps < 0) throw new ArgumentOutOfRangeException(nameof(rateBps));
        var fee = pairAlreadyCharged ? 0L : checked((price.Minor * rateBps + 5000) / 10000);
        return new FeeResult(price, price with { Minor = fee }, price with { Minor = checked(price.Minor - fee) });
    }

    public static FeeResult CalculatePartialRefund(Money price, Money refund, int rateBps, bool pairAlreadyCharged)
    {
        if (refund.Minor < 0 || refund.Minor > price.Minor) throw new ArgumentOutOfRangeException(nameof(refund));
        return Calculate(price.Minus(refund), rateBps, pairAlreadyCharged);
    }
}
