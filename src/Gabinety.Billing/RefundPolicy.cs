namespace Gabinety.Billing;

public enum MoneyEvent
{
    ClientCancelledEarly,
    ClientCancelledLate,
    PractitionerCancelled,
    HoldExpiredPaymentSucceeded,
    CompletedNoDispute,
    ClientNoShow,
    DisputeRelease,
    DisputeRefundFull,
    DisputeRefundPartial,
}

public readonly record struct Settlement(Money Refund, Money PractitionerShare, Money Fee);

/// <summary>CONTRACTS 9.2 as pure functions. No clock: the caller passes <c>now</c>.</summary>
public static class RefundPolicy
{
    public static readonly TimeSpan FullRefundWindow = TimeSpan.FromHours(24);

    public static MoneyEvent ForClientCancellation(DateTimeOffset start, DateTimeOffset now)
        => start - now >= FullRefundWindow ? MoneyEvent.ClientCancelledEarly : MoneyEvent.ClientCancelledLate;

    public static Settlement Settle(MoneyEvent e, Money price, int rateBps, bool pairAlreadyCharged, Money? refund = null)
    {
        if (refund.HasValue != (e == MoneyEvent.DisputeRefundPartial))
            throw new ArgumentException("A refund amount is required for, and only for, DisputeRefundPartial.", nameof(refund));
        var zero = price with { Minor = 0 };

        switch (e)
        {
            case MoneyEvent.ClientCancelledEarly:
            case MoneyEvent.PractitionerCancelled:
            case MoneyEvent.HoldExpiredPaymentSucceeded:
            case MoneyEvent.DisputeRefundFull:
                return new Settlement(price, zero, zero);
            case MoneyEvent.ClientCancelledLate:
            case MoneyEvent.CompletedNoDispute:
            case MoneyEvent.ClientNoShow:
            case MoneyEvent.DisputeRelease:
                var full = Fees.Calculate(price, rateBps, pairAlreadyCharged);
                return new Settlement(zero, full.Payout, full.Fee);
            case MoneyEvent.DisputeRefundPartial:
                var part = Fees.CalculatePartialRefund(price, refund!.Value, rateBps, pairAlreadyCharged);
                return new Settlement(refund.Value, part.Payout, part.Fee);
            default:
                throw new ArgumentOutOfRangeException(nameof(e));
        }
    }
}
