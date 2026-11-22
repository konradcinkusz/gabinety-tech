using Gabinety.Billing;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.ServiceDefaults;

namespace Gabinety.Booking.Notifications.Scheduling;

/// <summary>
/// Writes the notification rows of CONTRACTS 8.1 inside the transition's own transaction (T-04.09b); it never saves, so a failure anywhere in the
/// transition rolls the rows back with it.
/// <list type="bullet">
/// <item><c>PendingPayment</c> to <c>Confirmed</c>: <c>BookingConfirmed</c> to the client and the practitioner (due now), <c>Reminder24h</c> and <c>Reminder1h</c>
/// to the client (due 24 h and 1 h before the start), <c>ReviewRequest</c> to the client (due 2 h after the end). A reminder whose time has already passed is
/// not scheduled: a booking made 20 h before the start has no "24 h" reminder, and a reminder sent after the start would be wrong.</item>
/// <item>A cancellation (any of the three) or a reported client no-show: the unsent reminders and the review request are removed (no review after a
/// cancellation or a no-show, CONTRACTS 8.1); sent rows and the confirmations stay. A cancellation by the practitioner also schedules <c>Cancelled</c> to the client, due now.</item>
/// </list>
/// Rows go through <see cref="INotificationWriter.ScheduleAsync"/>, which checks saved rows too, so a replay never duplicates (unique per booking, kind, user).
/// </summary>
public sealed class NotificationSchedulingHook(INotificationWriter writer) : IBookingTransitionHook
{
    public static readonly TimeSpan ReviewDelay = TimeSpan.FromHours(2);
    static readonly NotificationKind[] RemovedWhenNotHeld = [NotificationKind.Reminder24h, NotificationKind.Reminder1h, NotificationKind.ReviewRequest];

    public async Task OnTransitionAsync(BookingTransitionContext context, CancellationToken ct)
    {
        switch (context.To)
        {
            case BookingStatus.Confirmed when context.From == BookingStatus.PendingPayment:
                await ScheduleConfirmedAsync(context, ct);
                break;
            case BookingStatus.CancelledByClient or BookingStatus.LateCancelledByClient or BookingStatus.CancelledByPractitioner or BookingStatus.ClientNoShow:
                await writer.CancelUnsentAsync(context.Booking.Id, RemovedWhenNotHeld, ct);
                // the practitioner's cancel (their own, or the cascade of a suspension) is news to the client: the Cancelled template of T-04.09a; the client's own cancel is not
                if (context.To == BookingStatus.CancelledByPractitioner)
                    await writer.ScheduleAsync(context.Booking, NotificationKind.Cancelled, context.Booking.ClientUserId, context.Now, ct);
                break;
        }
    }

    async Task ScheduleConfirmedAsync(BookingTransitionContext c, CancellationToken ct)
    {
        var b = c.Booking;
        await writer.ScheduleAsync(b, NotificationKind.BookingConfirmed, b.ClientUserId, c.Now, ct);
        await writer.ScheduleAsync(b, NotificationKind.BookingConfirmed, b.PractitionerUserId, c.Now, ct);

        var r24 = b.StartUtc.AddHours(-24);
        if (r24 > c.Now) await writer.ScheduleAsync(b, NotificationKind.Reminder24h, b.ClientUserId, r24, ct);
        var r1 = b.StartUtc.AddHours(-1);
        if (r1 > c.Now) await writer.ScheduleAsync(b, NotificationKind.Reminder1h, b.ClientUserId, r1, ct);

        await writer.ScheduleAsync(b, NotificationKind.ReviewRequest, b.ClientUserId, b.EndUtc + ReviewDelay, ct);
    }
}

/// <summary>Registers the scheduling hook; <see cref="BookingTransitions"/> runs it inside every transition.</summary>
public sealed class NotificationSchedulingFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) => builder.Services.AddScoped<IBookingTransitionHook, NotificationSchedulingHook>();

    public void Map(IEndpointRouteBuilder endpoints) { }
}
