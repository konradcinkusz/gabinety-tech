using Gabinety.Billing;

namespace Gabinety.Billing.Tests;

public class RefundPolicyTests
{
    private static readonly DateTimeOffset Start = new(2030, 1, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Exactly_24h_before_start_is_full_refund()
        => Assert.Equal(MoneyEvent.ClientCancelledEarly,
            RefundPolicy.ForClientCancellation(Start, Start.AddHours(-24)));

    [Fact]
    public void One_second_later_is_no_refund()
        => Assert.Equal(MoneyEvent.ClientCancelledLate,
            RefundPolicy.ForClientCancellation(Start, Start.AddHours(-24).AddSeconds(1)));

    [Fact]
    public void Long_before_is_full_refund_and_after_start_is_late()
    {
        Assert.Equal(MoneyEvent.ClientCancelledEarly, RefundPolicy.ForClientCancellation(Start, Start.AddDays(-30)));
        Assert.Equal(MoneyEvent.ClientCancelledLate, RefundPolicy.ForClientCancellation(Start, Start.AddHours(1)));
    }

    private static readonly Money Price = Money.Pln(25000);

    [Theory]
    [InlineData(MoneyEvent.ClientCancelledEarly, 25000, 0, 0)]
    [InlineData(MoneyEvent.ClientCancelledLate, 0, 21250, 3750)]
    [InlineData(MoneyEvent.PractitionerCancelled, 25000, 0, 0)]
    [InlineData(MoneyEvent.HoldExpiredPaymentSucceeded, 25000, 0, 0)]
    [InlineData(MoneyEvent.CompletedNoDispute, 0, 21250, 3750)]
    [InlineData(MoneyEvent.ClientNoShow, 0, 21250, 3750)]
    [InlineData(MoneyEvent.DisputeRelease, 0, 21250, 3750)]
    [InlineData(MoneyEvent.DisputeRefundFull, 25000, 0, 0)]
    public void Table_9_2_rows(MoneyEvent e, long refund, long practitioner, long fee)
    {
        var s = RefundPolicy.Settle(e, Price, 1500, false);
        Assert.Equal(refund, s.Refund.Minor);
        Assert.Equal(practitioner, s.PractitionerShare.Minor);
        Assert.Equal(fee, s.Fee.Minor);
    }

    [Fact]
    public void Dispute_refund_partial_row()
    {
        var s = RefundPolicy.Settle(MoneyEvent.DisputeRefundPartial, Price, 1500, false, Money.Pln(5000));
        Assert.Equal(5000, s.Refund.Minor);
        Assert.Equal(17000, s.PractitionerShare.Minor);
        Assert.Equal(3000, s.Fee.Minor);
    }

    [Fact]
    public void Partial_requires_refund_and_others_forbid_it()
    {
        Assert.Throws<ArgumentException>(() => RefundPolicy.Settle(MoneyEvent.DisputeRefundPartial, Price, 1500, false));
        Assert.Throws<ArgumentException>(() => RefundPolicy.Settle(MoneyEvent.DisputeRelease, Price, 1500, false, Money.Pln(1)));
    }

    [Theory]
    [InlineData(MoneyEvent.ClientCancelledLate)]
    [InlineData(MoneyEvent.CompletedNoDispute)]
    [InlineData(MoneyEvent.ClientNoShow)]
    [InlineData(MoneyEvent.DisputeRelease)]
    public void Pair_rule_zeroes_fee_where_a_fee_applies(MoneyEvent e)
    {
        var s = RefundPolicy.Settle(e, Price, 1500, true);
        Assert.Equal(0, s.Fee.Minor);
        Assert.Equal(25000, s.PractitionerShare.Minor);
    }

    [Fact]
    public void Every_money_event_is_covered()
    {
        foreach (var e in Enum.GetValues<MoneyEvent>())
            RefundPolicy.Settle(e, Price, 1500, false, e == MoneyEvent.DisputeRefundPartial ? Money.Pln(1) : null);
    }
}
