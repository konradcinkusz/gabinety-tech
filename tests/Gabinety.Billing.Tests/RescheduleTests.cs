using Gabinety.Billing;

namespace Gabinety.Billing.Tests;

public class RescheduleTests
{
    private static readonly DateTimeOffset Start = new(2030, 1, 10, 12, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> NotConfirmedStatuses()
        => Enum.GetValues<BookingStatus>()
            .Where(s => s != BookingStatus.Confirmed)
            .Select(s => new object[] { s });

    [Fact]
    public void Exactly_24h_before_start_is_allowed()
        => Assert.Equal(RescheduleDecision.Allowed,
            Reschedule.Decide(BookingStatus.Confirmed, Start, Start.Add(-RefundPolicy.FullRefundWindow), 0));

    [Fact]
    public void Twenty_three_hours_59_minutes_59_seconds_before_start_is_inside_window()
        => Assert.Equal(RescheduleDecision.InsideWindow,
            Reschedule.Decide(BookingStatus.Confirmed, Start, Start.Add(-RefundPolicy.FullRefundWindow).AddSeconds(1), 0));

    [Fact]
    public void Second_reschedule_is_already_rescheduled()
        => Assert.Equal(RescheduleDecision.AlreadyRescheduled,
            Reschedule.Decide(BookingStatus.Confirmed, Start, Start.AddDays(-5), 1));

    [Theory]
    [MemberData(nameof(NotConfirmedStatuses))]
    public void Every_status_other_than_confirmed_is_not_confirmed(BookingStatus status)
        => Assert.Equal(RescheduleDecision.NotConfirmed,
            Reschedule.Decide(status, Start, Start.AddDays(-5), 0));

    [Fact]
    public void Status_is_checked_before_count_and_count_before_window()
    {
        var inside = Start.AddHours(-1);
        Assert.Equal(RescheduleDecision.NotConfirmed,
            Reschedule.Decide(BookingStatus.Disputed, Start, inside, 1));
        Assert.Equal(RescheduleDecision.AlreadyRescheduled,
            Reschedule.Decide(BookingStatus.Confirmed, Start, inside, 1));
        Assert.Equal(RescheduleDecision.InsideWindow,
            Reschedule.Decide(BookingStatus.Confirmed, Start, inside, 0));
    }
}
