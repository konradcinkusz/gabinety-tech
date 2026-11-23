namespace Gabinety.Billing;

public enum RescheduleDecision
{
    Allowed,
    NotConfirmed,
    AlreadyRescheduled,
    InsideWindow,
}

/// <summary>
/// CONTRACTS 8.1 reschedule row as a pure rule. No I/O, no clock: the caller passes <c>now</c>.
/// Checks run in order: status, then reschedule count, then the window.
/// </summary>
public static class Reschedule
{
    public static RescheduleDecision Decide(BookingStatus status, DateTimeOffset start, DateTimeOffset now, int rescheduleCount)
    {
        if (status != BookingStatus.Confirmed)
            return RescheduleDecision.NotConfirmed;
        if (rescheduleCount >= 1)
            return RescheduleDecision.AlreadyRescheduled;
        if (start - now < RefundPolicy.FullRefundWindow)
            return RescheduleDecision.InsideWindow;
        return RescheduleDecision.Allowed;
    }
}
