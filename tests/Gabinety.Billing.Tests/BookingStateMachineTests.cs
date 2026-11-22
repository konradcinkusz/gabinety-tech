using Gabinety.Billing;
using S = Gabinety.Billing.BookingStatus;
using T = Gabinety.Billing.BookingTrigger;

namespace Gabinety.Billing.Tests;

public class BookingStateMachineTests
{
    private static readonly DateTimeOffset Start = new(2030, 1, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Early = Start.AddHours(-24);
    private static readonly DateTimeOffset Late = Start.AddHours(-24).AddSeconds(1);

    [Fact]
    public void Create_gives_PendingPayment() => Assert.Equal(S.PendingPayment, BookingStateMachine.Create());

    // Every legal row of §8.1. Everything else must be rejected.
    private static readonly (S From, T Trigger, DateTimeOffset Now, S To)[] Legal =
    [
        (S.PendingPayment, T.PaymentSucceeded, Early, S.Confirmed),
        (S.PendingPayment, T.HoldExpired, Early, S.Expired),
        (S.PendingPayment, T.PaymentFailed, Early, S.Expired),
        (S.Confirmed, T.ClientCancels, Early, S.CancelledByClient),
        (S.Confirmed, T.ClientCancels, Late, S.LateCancelledByClient),
        (S.Confirmed, T.PractitionerCancels, Early, S.CancelledByPractitioner),
        (S.Confirmed, T.CompletionWindowElapsed, Early, S.Completed),
        (S.Confirmed, T.ClientNoShowReported, Early, S.ClientNoShow),
        (S.Confirmed, T.ClientDisputes, Early, S.Disputed),
        (S.Completed, T.ClientDisputes, Early, S.Disputed), // the caller bounds it by start + 72 h
        (S.Disputed, T.AdminResolves, Early, S.Resolved),
    ];

    [Fact]
    public void Every_legal_transition()
    {
        foreach (var (from, trigger, now, to) in Legal)
        {
            var r = BookingStateMachine.Next(from, trigger, Start, now);
            Assert.True(r.Allowed, $"{from}+{trigger}");
            Assert.Equal(to, r.Next);
        }
    }

    [Fact]
    public void Every_other_pair_is_rejected_exhaustively()
    {
        var legal = Legal.Select(l => (l.From, l.Trigger)).ToHashSet();
        var rejected = 0;
        foreach (var from in Enum.GetValues<S>())
            foreach (var trigger in Enum.GetValues<T>())
            {
                if (legal.Contains((from, trigger))) continue;
                foreach (var now in new[] { Early, Late, Start.AddDays(1) })
                {
                    var r = BookingStateMachine.Next(from, trigger, Start, now);
                    Assert.False(r.Allowed, $"{from}+{trigger}");
                    Assert.Null(r.Next);
                }
                rejected++;
            }
        Assert.Equal(Enum.GetValues<S>().Length * Enum.GetValues<T>().Length - legal.Count, rejected);
    }

    [Fact]
    public void Terminal_statuses_accept_nothing()
    {
        S[] terminal = [S.CancelledByClient, S.LateCancelledByClient, S.CancelledByPractitioner, S.Expired, S.ClientNoShow, S.Resolved];
        foreach (var from in terminal)
            foreach (var trigger in Enum.GetValues<T>())
                Assert.False(BookingStateMachine.Next(from, trigger, Start, Early).Allowed);
        // Completed accepts one thing: the client's dispute (the caller bounds it by start + 72 h, ADR proposal 0011)
        foreach (var trigger in Enum.GetValues<T>().Where(t => t != T.ClientDisputes))
            Assert.False(BookingStateMachine.Next(S.Completed, trigger, Start, Early).Allowed);
    }

    [Fact]
    public void Client_cancel_boundary_matches_refund_policy()
    {
        Assert.Equal(S.CancelledByClient, BookingStateMachine.Next(S.Confirmed, T.ClientCancels, Start, Start.AddHours(-24)).Next);
        Assert.Equal(S.LateCancelledByClient, BookingStateMachine.Next(S.Confirmed, T.ClientCancels, Start, Start.AddHours(-24).AddSeconds(1)).Next);
    }
}
