namespace Gabinety.Web.Components.Pages.Admin;

/// <summary>The practitioner state machine as an administrator sees it (SPEC 8.2): what may be done from each status. The API still decides; this only decides which buttons are shown.</summary>
public static class VerificationRules
{
    public const string Approve = "approve", Reject = "reject", Suspend = "suspend", Reinstate = "reinstate";
    public static readonly string[] QueueStatuses = ["Submitted", "Approved", "Suspended", "Rejected"];
    public const int PageSize = 12;

    public static IReadOnlyList<string> Allowed(string? status) => status switch
    {
        "Submitted" => [Approve, Reject], "Approved" => [Suspend], "Suspended" => [Reinstate], _ => [],
    };

    public static bool NeedsReason(string verb) => verb is Reject or Suspend;

    public static string Status(string? raw) => AdminQuery.Choice(raw, QueueStatuses, "Submitted");
}

/// <summary>The statuses of the review moderation page (the wire names of CONTRACTS 5.2).</summary>
public static class ReviewRules
{
    public const string Held = "Held", Published = "Published", Rejected = "Rejected";
    public static readonly string[] Statuses = [Held, Published, Rejected];
    public const int PageSize = 20;

    public static string Status(string? raw) => AdminQuery.Choice(raw, Statuses, Held);
}
