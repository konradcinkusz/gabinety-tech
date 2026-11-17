using Gabinety.DemoSeeder;

namespace Gabinety.Integration.Tests.Seeder;

public class StatusPlanTests
{
    public static IEnumerable<object[]> Cases() =>
    [
        [DemoStatus.Draft, DemoStatus.Draft, ""],
        [DemoStatus.Draft, DemoStatus.Submitted, "Submit"],
        [DemoStatus.Draft, DemoStatus.Approved, "Submit,Approve"],
        [DemoStatus.Draft, DemoStatus.Suspended, "Submit,Approve,Suspend"],
        [DemoStatus.Draft, DemoStatus.Rejected, "Submit,Reject"],
        [DemoStatus.Submitted, DemoStatus.Approved, "Approve"],
        [DemoStatus.Submitted, DemoStatus.Rejected, "Reject"],
        [DemoStatus.Rejected, DemoStatus.Approved, "Submit,Approve"],
        [DemoStatus.Suspended, DemoStatus.Approved, "Reinstate"],
        [DemoStatus.Approved, DemoStatus.Suspended, "Suspend"],
        [DemoStatus.Approved, DemoStatus.Approved, ""],
        [DemoStatus.Suspended, DemoStatus.Suspended, ""],
        [DemoStatus.Approved, DemoStatus.Draft, ""],       // never walks backwards
        [DemoStatus.Approved, DemoStatus.Submitted, ""],
        [DemoStatus.Suspended, DemoStatus.Rejected, ""],
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_plan_walks_forward_through_legal_transitions_only(DemoStatus current, DemoStatus target, string expected) =>
        Assert.Equal(expected, string.Join(",", StatusPlan.Triggers(current, target)));
}
