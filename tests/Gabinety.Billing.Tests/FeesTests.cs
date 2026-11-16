using Gabinety.Billing;

namespace Gabinety.Billing.Tests;

public class FeesTests
{
    [Theory]
    [InlineData(25000, 3750, 21250)]
    [InlineData(33333, 5000, 28333)]
    [InlineData(5000, 750, 4250)]
    [InlineData(1, 0, 1)]
    [InlineData(150000, 22500, 127500)]
    [InlineData(0, 0, 0)]
    public void Vectors_of_9_1(long price, long fee, long payout)
    {
        var r = Fees.Calculate(Money.Pln(price), 1500, false);
        Assert.Equal(fee, r.Fee.Minor);
        Assert.Equal(payout, r.Payout.Minor);
    }

    [Fact]
    public void Rounds_half_up_on_an_exact_tie()
    {
        // 10 * 1500 / 10000 = 1.5 exactly; formula of 9.1 gives 2 (derived from the formula, not a 9.1 vector).
        var r = Fees.Calculate(Money.Pln(10), 1500, false);
        Assert.Equal(2, r.Fee.Minor);
        Assert.Equal(8, r.Payout.Minor);
    }

    [Fact]
    public void Pair_already_charged_means_no_fee()
    {
        var r = Fees.Calculate(Money.Pln(25000), 1500, true);
        Assert.Equal(0, r.Fee.Minor);
        Assert.Equal(25000, r.Payout.Minor);
    }

    [Fact]
    public void Fee_plus_payout_equals_price_for_10000_generated_prices()
    {
        var rng = new Random(12345);
        for (var i = 0; i < 10_000; i++)
        {
            var price = rng.NextInt64(0, 10_000_000);
            var bps = rng.Next(0, 10_001);
            var pair = rng.Next(2) == 0;
            var r = Fees.Calculate(Money.Pln(price), bps, pair);
            Assert.Equal(price, r.Fee.Minor + r.Payout.Minor);
            Assert.InRange(r.Fee.Minor, 0, price);
        }
    }

    [Fact]
    public void Partial_refund_charges_fee_on_kept_amount()
    {
        var r = Fees.CalculatePartialRefund(Money.Pln(25000), Money.Pln(5000), 1500, false);
        Assert.Equal(20000, r.Kept.Minor);
        Assert.Equal(3000, r.Fee.Minor);
        Assert.Equal(17000, r.Payout.Minor);
        Assert.Equal(20000, r.Fee.Minor + r.Payout.Minor);
    }

    [Fact]
    public void Partial_refund_pair_rule_and_bounds()
    {
        var r = Fees.CalculatePartialRefund(Money.Pln(25000), Money.Pln(5000), 1500, true);
        Assert.Equal(0, r.Fee.Minor);
        Assert.Equal(20000, r.Payout.Minor);
        Assert.Throws<ArgumentOutOfRangeException>(() => Fees.CalculatePartialRefund(Money.Pln(100), Money.Pln(101), 1500, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fees.CalculatePartialRefund(Money.Pln(100), Money.Pln(-1), 1500, false));
    }

    [Fact]
    public void Overflow_throws()
    {
        Assert.Throws<OverflowException>(() => Fees.Calculate(Money.Pln(long.MaxValue), 1500, false));
        Assert.Throws<OverflowException>(() => Money.Pln(long.MaxValue).Plus(Money.Pln(1)));
        Assert.Throws<OverflowException>(() => Money.Pln(long.MinValue).Minus(Money.Pln(1)));
    }

    [Fact]
    public void Money_does_not_mix_currencies()
    {
        Assert.Throws<InvalidOperationException>(() => Money.Pln(1).Plus(new Money(1, "EUR")));
        Assert.Throws<InvalidOperationException>(() => Money.Pln(1).Minus(new Money(1, "EUR")));
        Assert.Equal(new Money(3, "PLN"), Money.Pln(1).Plus(Money.Pln(2)));
    }
}
