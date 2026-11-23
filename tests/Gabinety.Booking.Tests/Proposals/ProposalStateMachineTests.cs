using Gabinety.Booking.Features.Proposals;
using Gabinety.Contracts.Booking;

namespace Gabinety.Booking.Tests;

/// <summary>The transitions of ADR proposal 0013 section 3, pure.</summary>
public class ProposalStateMachineTests
{
    static readonly ProposalStatus[] Open = [ProposalStatus.AwaitingPractitioner, ProposalStatus.AwaitingClient];
    static readonly ProposalStatus[] Closed = [ProposalStatus.Accepted, ProposalStatus.Rejected, ProposalStatus.Withdrawn, ProposalStatus.Expired];
    static readonly ProposalActor[] Parties = [ProposalActor.Client, ProposalActor.Practitioner];

    [Fact]
    public void A_new_proposal_waits_for_the_practitioner() => Assert.Equal(ProposalStatus.AwaitingPractitioner, ProposalStateMachine.Create());

    [Theory]
    [InlineData(ProposalStatus.AwaitingPractitioner, ProposalActor.Practitioner)]
    [InlineData(ProposalStatus.AwaitingClient, ProposalActor.Client)]
    public void The_turn_belongs_to_the_awaited_party(ProposalStatus status, ProposalActor turn) => Assert.Equal(turn, ProposalStateMachine.Turn(status));

    [Fact]
    public void A_closed_proposal_has_nobodys_turn() => Assert.All(Closed, s => Assert.Null(ProposalStateMachine.Turn(s)));

    [Theory]
    [InlineData(ProposalStatus.AwaitingPractitioner, ProposalTrigger.Accept, ProposalActor.Practitioner, ProposalStatus.Accepted)]
    [InlineData(ProposalStatus.AwaitingPractitioner, ProposalTrigger.Counter, ProposalActor.Practitioner, ProposalStatus.AwaitingClient)]
    [InlineData(ProposalStatus.AwaitingPractitioner, ProposalTrigger.Reject, ProposalActor.Practitioner, ProposalStatus.Rejected)]
    [InlineData(ProposalStatus.AwaitingClient, ProposalTrigger.Accept, ProposalActor.Client, ProposalStatus.Accepted)]
    [InlineData(ProposalStatus.AwaitingClient, ProposalTrigger.Counter, ProposalActor.Client, ProposalStatus.AwaitingPractitioner)]
    [InlineData(ProposalStatus.AwaitingClient, ProposalTrigger.Reject, ProposalActor.Client, ProposalStatus.Rejected)]
    [InlineData(ProposalStatus.AwaitingPractitioner, ProposalTrigger.Withdraw, ProposalActor.Client, ProposalStatus.Withdrawn)]
    [InlineData(ProposalStatus.AwaitingClient, ProposalTrigger.Withdraw, ProposalActor.Client, ProposalStatus.Withdrawn)]
    [InlineData(ProposalStatus.AwaitingPractitioner, ProposalTrigger.Expire, ProposalActor.System, ProposalStatus.Expired)]
    [InlineData(ProposalStatus.AwaitingClient, ProposalTrigger.Expire, ProposalActor.System, ProposalStatus.Expired)]
    public void The_allowed_transitions(ProposalStatus from, ProposalTrigger trigger, ProposalActor by, ProposalStatus to) =>
        Assert.Equal(to, ProposalStateMachine.Next(from, trigger, by));

    [Fact]
    public void The_party_out_of_turn_cannot_accept_counter_or_reject()
    {
        foreach (var t in new[] { ProposalTrigger.Accept, ProposalTrigger.Counter, ProposalTrigger.Reject })
        {
            Assert.Null(ProposalStateMachine.Next(ProposalStatus.AwaitingPractitioner, t, ProposalActor.Client));
            Assert.Null(ProposalStateMachine.Next(ProposalStatus.AwaitingClient, t, ProposalActor.Practitioner));
        }
    }

    [Fact]
    public void Only_the_client_withdraws_and_only_the_system_expires()
    {
        foreach (var s in Open)
        {
            Assert.Null(ProposalStateMachine.Next(s, ProposalTrigger.Withdraw, ProposalActor.Practitioner));
            Assert.Null(ProposalStateMachine.Next(s, ProposalTrigger.Withdraw, ProposalActor.System));
            Assert.Null(ProposalStateMachine.Next(s, ProposalTrigger.Expire, ProposalActor.Client));
            Assert.Null(ProposalStateMachine.Next(s, ProposalTrigger.Expire, ProposalActor.Practitioner));
        }
    }

    [Fact]
    public void Nothing_leaves_a_closed_state()
    {
        foreach (var s in Closed)
            foreach (var t in Enum.GetValues<ProposalTrigger>())
                foreach (var by in Enum.GetValues<ProposalActor>())
                    Assert.Null(ProposalStateMachine.Next(s, t, by));
    }

    [Fact]
    public void The_system_never_accepts_counters_or_rejects()
    {
        foreach (var s in Open)
            foreach (var t in new[] { ProposalTrigger.Accept, ProposalTrigger.Counter, ProposalTrigger.Reject })
                Assert.Null(ProposalStateMachine.Next(s, t, ProposalActor.System));
    }

    [Theory]
    [InlineData(ProposalTrigger.Accept, ProposalAction.Accepted)]
    [InlineData(ProposalTrigger.Counter, ProposalAction.Countered)]
    [InlineData(ProposalTrigger.Reject, ProposalAction.Rejected)]
    [InlineData(ProposalTrigger.Withdraw, ProposalAction.Withdrawn)]
    [InlineData(ProposalTrigger.Expire, ProposalAction.Expired)]
    public void Each_trigger_has_its_action(ProposalTrigger t, ProposalAction a) => Assert.Equal(a, ProposalStateMachine.ActionOf(t));

    [Fact]
    public void Permissions_follow_the_turn()
    {
        Assert.Equal(new ProposalPermissions(true, true, true, false), ProposalPermissions.For(ProposalStatus.AwaitingPractitioner, ProposalActor.Practitioner, 1));
        Assert.Equal(new ProposalPermissions(false, false, false, true), ProposalPermissions.For(ProposalStatus.AwaitingPractitioner, ProposalActor.Client, 1));
        Assert.Equal(new ProposalPermissions(true, true, true, true), ProposalPermissions.For(ProposalStatus.AwaitingClient, ProposalActor.Client, 2));
        Assert.Equal(new ProposalPermissions(false, false, false, false), ProposalPermissions.For(ProposalStatus.AwaitingClient, ProposalActor.Practitioner, 2));
    }

    [Fact]
    public void A_closed_proposal_permits_nothing() =>
        Assert.All(Closed.SelectMany(s => Parties.Select(p => ProposalPermissions.For(s, p, 3))), f => Assert.Equal(default, f));

    [Fact]
    public void The_counter_cap_closes_the_counter_flag_but_not_the_rest()
    {
        // round 1 is the proposal; after MaxCounters counters the round count is MaxCounters + 1
        var atCap = ProposalStateMachine.MaxCounters + 1;
        var f = ProposalPermissions.For(ProposalStatus.AwaitingPractitioner, ProposalActor.Practitioner, atCap);
        Assert.False(f.CanCounter);
        Assert.True(f.CanAccept); Assert.True(f.CanReject);
        Assert.True(ProposalPermissions.For(ProposalStatus.AwaitingPractitioner, ProposalActor.Practitioner, atCap - 1).CanCounter);
    }
}
