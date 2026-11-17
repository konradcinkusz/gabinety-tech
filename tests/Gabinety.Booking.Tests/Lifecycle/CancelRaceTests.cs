using System.Net;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>
/// A cancel that loses a race reads the booking while it is still Confirmed and the payment after the winner refunded it.
/// CI measured this as a 500 ("cannot be refunded") in one of ten rounds of the concurrent-cancel integration test; it must be a 409.
/// </summary>
public class CancelRaceTests
{
    [Theory]
    [InlineData(true)]  // client cancel: full refund planned
    [InlineData(false)] // practitioner cancel: full refund planned
    public async Task A_cancel_that_finds_the_payment_already_refunded_is_409_invalid_transition_not_500_and_asks_the_provider_for_nothing(bool byClient)
    {
        var provider = new RecordingProvider();
        await using var h = Life.Host(provider);
        var id = await Life.Seed(h, BookingStatus.Confirmed, TimeSpan.FromDays(3));
        // What the loser sees: the booking row it already read says Confirmed, but the winner has committed the refund by the time it reads the payment.
        await h.Seed(db =>
        {
            var p = db.Payments.Single(x => x.BookingId == id);
            p.Status = PaymentStatus.Refunded;
            p.RefundedMinor = p.AmountMinor;
        });

        var r = await Life.Cancel(h, id, byClient ? Life.Client : Life.Practitioner);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("invalid_transition", (await r.Body()).GetProperty("code").GetString());
        Assert.Empty(provider.Refunds);
        Assert.Equal(10000, (await Life.PaymentOf(h, id)).RefundedMinor); // nothing was recorded twice
        Assert.Empty(await Life.RefundRows(h, id));
    }
}
