using Gabinety.Contracts.Directory;

namespace Gabinety.Directory.Features.Profiles;

public enum PractitionerTrigger { Submit, Approve, Reject, Suspend, Reinstate, NipChanged }

/// <summary>CONTRACTS 8.2 as a pure transition table; a pair that is not in the table is illegal.</summary>
public static class PractitionerStateMachine
{
    static readonly IReadOnlyDictionary<(PractitionerStatus, PractitionerTrigger), PractitionerStatus> Table =
        new Dictionary<(PractitionerStatus, PractitionerTrigger), PractitionerStatus>
        {
            [(PractitionerStatus.Draft, PractitionerTrigger.Submit)] = PractitionerStatus.Submitted,
            [(PractitionerStatus.Rejected, PractitionerTrigger.Submit)] = PractitionerStatus.Submitted,
            [(PractitionerStatus.Submitted, PractitionerTrigger.Approve)] = PractitionerStatus.Approved,
            [(PractitionerStatus.Submitted, PractitionerTrigger.Reject)] = PractitionerStatus.Rejected,
            [(PractitionerStatus.Approved, PractitionerTrigger.Suspend)] = PractitionerStatus.Suspended,
            [(PractitionerStatus.Suspended, PractitionerTrigger.Reinstate)] = PractitionerStatus.Approved,
            [(PractitionerStatus.Approved, PractitionerTrigger.NipChanged)] = PractitionerStatus.Submitted,
        };

    public static bool TryTransition(PractitionerStatus from, PractitionerTrigger trigger, out PractitionerStatus to) =>
        Table.TryGetValue((from, trigger), out to);
}
