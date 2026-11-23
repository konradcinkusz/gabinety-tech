using Gabinety.Contracts.Booking;

namespace Gabinety.Booking.Features.Proposals;

public enum ProposalTrigger { Accept, Counter, Reject, Withdraw, Expire }

/// <summary>
/// The transitions of a collaboration proposal (ADR proposal 0013 section 3). Pure: no I/O, no clock. The party whose turn it is may accept, counter or reject; only the client may
/// withdraw; only the system expires. One accept of the other side's latest terms is mutual acceptance, because the author of those terms has already agreed to them.
/// </summary>
public static class ProposalStateMachine
{
    /// <summary>At most this many counter-proposals in one thread (an owner constant, ADR proposal 0013 section 6).</summary>
    public const int MaxCounters = 6;

    public static ProposalStatus Create() => ProposalStatus.AwaitingPractitioner;

    public static bool IsOpen(ProposalStatus s) => s is ProposalStatus.AwaitingPractitioner or ProposalStatus.AwaitingClient;

    /// <summary>Whose turn it is, or null when the proposal is closed.</summary>
    public static ProposalActor? Turn(ProposalStatus s) => s switch
    {
        ProposalStatus.AwaitingPractitioner => ProposalActor.Practitioner,
        ProposalStatus.AwaitingClient => ProposalActor.Client,
        _ => null,
    };

    /// <summary>The state after <paramref name="trigger"/> by <paramref name="by"/>, or null when it is not allowed from <paramref name="from"/>.</summary>
    public static ProposalStatus? Next(ProposalStatus from, ProposalTrigger trigger, ProposalActor by)
    {
        if (!IsOpen(from)) return null;
        var mine = Turn(from) == by;
        return trigger switch
        {
            ProposalTrigger.Accept when mine => ProposalStatus.Accepted,
            ProposalTrigger.Reject when mine => ProposalStatus.Rejected,
            ProposalTrigger.Counter when mine => by == ProposalActor.Client ? ProposalStatus.AwaitingPractitioner : ProposalStatus.AwaitingClient,
            ProposalTrigger.Withdraw when by == ProposalActor.Client => ProposalStatus.Withdrawn,
            ProposalTrigger.Expire when by == ProposalActor.System => ProposalStatus.Expired,
            _ => null,
        };
    }

    public static ProposalAction ActionOf(ProposalTrigger t) => t switch
    {
        ProposalTrigger.Accept => ProposalAction.Accepted,
        ProposalTrigger.Counter => ProposalAction.Countered,
        ProposalTrigger.Reject => ProposalAction.Rejected,
        ProposalTrigger.Withdraw => ProposalAction.Withdrawn,
        _ => ProposalAction.Expired,
    };
}

/// <summary>What a viewer may do right now: the four flags of <see cref="ProposalView"/>, computed from the same table as <see cref="ProposalStateMachine"/>.</summary>
public readonly record struct ProposalPermissions(bool CanAccept, bool CanCounter, bool CanReject, bool CanWithdraw)
{
    public static ProposalPermissions For(ProposalStatus status, ProposalActor viewer, int roundCount) => new(
        ProposalStateMachine.Next(status, ProposalTrigger.Accept, viewer) is not null,
        ProposalStateMachine.Next(status, ProposalTrigger.Counter, viewer) is not null && roundCount <= ProposalStateMachine.MaxCounters,
        ProposalStateMachine.Next(status, ProposalTrigger.Reject, viewer) is not null,
        ProposalStateMachine.Next(status, ProposalTrigger.Withdraw, viewer) is not null);
}
