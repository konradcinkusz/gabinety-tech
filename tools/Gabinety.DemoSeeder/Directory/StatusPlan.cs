namespace Gabinety.DemoSeeder;

public enum DemoStatus { Draft, Submitted, Approved, Rejected, Suspended }
public enum Trigger { Submit, Approve, Reject, Suspend, Reinstate }

/// <summary>
/// The transitions that take a practitioner from where it is to where the demo wants it, along CONTRACTS 8.2. Forward only: a row that is already past
/// the target (an end-to-end journey approved the "Submitted" one) or cannot reach it is left alone, so a re-run never undoes anything.
/// </summary>
public static class StatusPlan
{
    public static IReadOnlyList<Trigger> Triggers(DemoStatus current, DemoStatus target) => (current, target) switch
    {
        (DemoStatus.Draft or DemoStatus.Rejected, DemoStatus.Submitted) => [Trigger.Submit],
        (DemoStatus.Draft or DemoStatus.Rejected, DemoStatus.Approved) => [Trigger.Submit, Trigger.Approve],
        (DemoStatus.Draft or DemoStatus.Rejected, DemoStatus.Suspended) => [Trigger.Submit, Trigger.Approve, Trigger.Suspend],
        (DemoStatus.Draft, DemoStatus.Rejected) => [Trigger.Submit, Trigger.Reject],
        (DemoStatus.Submitted, DemoStatus.Approved) => [Trigger.Approve],
        (DemoStatus.Submitted, DemoStatus.Suspended) => [Trigger.Approve, Trigger.Suspend],
        (DemoStatus.Submitted, DemoStatus.Rejected) => [Trigger.Reject],
        (DemoStatus.Suspended, DemoStatus.Approved) => [Trigger.Reinstate],
        (DemoStatus.Approved, DemoStatus.Suspended) => [Trigger.Suspend],
        _ => [],
    };
}
