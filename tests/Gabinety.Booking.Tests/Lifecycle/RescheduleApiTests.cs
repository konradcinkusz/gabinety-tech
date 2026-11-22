using System.Net;
using System.Net.Http.Json;
using Gabinety.Billing;
using Gabinety.Booking.Notifications;
using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

static class Resched
{
    // The host clock is Monday 2026-11-16 12:00 UTC and the practitioner works Mon-Fri 09:00-17:00 Warsaw time (UTC+1), so these are all on the grid.
    public const string Wednesday10 = "2026-11-18T10:00:00Z";
    public static readonly TimeSpan Day = TimeSpan.FromDays(1);

    public static Task<HttpResponseMessage> Post(CreateHost h, Guid id, string? as_, object? body) =>
        h.ClientFor(as_).PostAsJsonAsync($"/api/v1/bookings/{id}/reschedule", body ?? new { });

    public static Task<HttpResponseMessage> Move(CreateHost h, Guid id, string? as_, string startUtc) => Post(h, id, as_, new { startUtc });

    /// <summary>A host with the practitioner's work week, a confirmed booking <paramref name="startIn"/> ahead and its notification rows as confirmation would have written them.</summary>
    public static async Task<(CreateHost Host, RecordingProvider Provider, Guid Id)> Confirmed(TimeSpan startIn, BookingStatus status = BookingStatus.Confirmed)
    {
        var provider = new RecordingProvider();
        var h = Life.Host(provider);
        await h.WorkWeek();
        var id = await Life.Seed(h, status, startIn);
        var b = await Life.Row(h, id);
        await h.Seed(db =>
        {
            foreach (var (kind, due) in new[] { (NotificationKind.Reminder24h, b.StartUtc.AddHours(-24)), (NotificationKind.Reminder1h, b.StartUtc.AddHours(-1)), (NotificationKind.ReviewRequest, b.EndUtc.AddHours(2)) })
                db.Notifications.Add(new Notification { Id = Guid.NewGuid(), BookingId = id, Kind = kind, ToUserId = Life.Client, DueAt = due });
        });
        return (h, provider, id);
    }

    public static Task<List<Notification>> Rows(CreateHost h, Guid id) => h.Query(db => db.Notifications.AsNoTracking().Where(n => n.BookingId == id).ToListAsync());

    public static async Task<string> Code(HttpResponseMessage r) => (await r.Body()).GetProperty("code").GetString()!;
}

/// <summary>T-11.10, POST /api/v1/bookings/{id}/reschedule (CONTRACTS 4.2, 8.1): the decision, the party rules, the new slot and what a move leaves alone.</summary>
public class RescheduleApiTests
{
    static readonly TimeSpan H25 = TimeSpan.FromHours(25);

    [Fact]
    public async Task Rescheduling_25h_ahead_moves_the_booking_and_leaves_the_payment_and_the_ledger_untouched()
    {
        var (h, provider, id) = await Resched.Confirmed(H25);
        await using var _ = h;
        var paymentBefore = await Life.PaymentOf(h, id);
        var ledgerBefore = await Life.Ledger(h, id);

        var r = await Resched.Move(h, id, Life.Client, Resched.Wednesday10);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var view = await r.Body();
        Assert.Equal("Confirmed", view.GetProperty("status").GetString());
        Assert.Equal("2026-11-18T10:00:00Z", view.GetProperty("startUtc").GetString());
        Assert.Equal("2026-11-18T11:00:00Z", view.GetProperty("endUtc").GetString());
        Assert.False(view.GetProperty("canReschedule").GetBoolean()); // once
        Assert.True(view.GetProperty("canCancel").GetBoolean());

        var row = await Life.Row(h, id);
        Assert.Equal((DateTimeOffset.Parse(Resched.Wednesday10), DateTimeOffset.Parse(Resched.Wednesday10).AddHours(1)), (row.StartUtc, row.EndUtc));
        Assert.Equal((BookingStatus.Confirmed, (short)1, (DateTimeOffset?)h.Clock.GetUtcNow()), (row.Status, row.RescheduleCount, row.RescheduledAt));

        var paymentAfter = await Life.PaymentOf(h, id);
        Assert.Equal((paymentBefore.Id, paymentBefore.Status, paymentBefore.AmountMinor, paymentBefore.RefundedMinor, paymentBefore.UpdatedAt),
            (paymentAfter.Id, paymentAfter.Status, paymentAfter.AmountMinor, paymentAfter.RefundedMinor, paymentAfter.UpdatedAt));
        var ledgerAfter = await Life.Ledger(h, id);
        Assert.Equal(ledgerBefore.Select(l => (l.Id, l.Kind, l.AmountMinor)), ledgerAfter.Select(l => (l.Id, l.Kind, l.AmountMinor)));
        Assert.Empty(provider.Refunds);
    }

    [Fact]
    public async Task Exactly_24h_before_the_start_is_still_allowed()
    {
        var (h, _, id) = await Resched.Confirmed(TimeSpan.FromHours(24));
        await using var _ = h;
        Assert.Equal(HttpStatusCode.OK, (await Resched.Move(h, id, Life.Client, Resched.Wednesday10)).StatusCode);
    }

    [Theory]
    [InlineData(24 * 3600 - 1)]
    [InlineData(23 * 3600)]
    [InlineData(60)]
    public async Task Inside_24h_is_422_reschedule_not_allowed_and_nothing_changes(int startInSeconds)
    {
        var (h, _, id) = await Resched.Confirmed(TimeSpan.FromSeconds(startInSeconds));
        await using var _ = h;
        var before = await Life.Row(h, id);

        var r = await Resched.Move(h, id, Life.Client, Resched.Wednesday10);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("reschedule_not_allowed", await Resched.Code(r));
        var after = await Life.Row(h, id);
        Assert.Equal((before.StartUtc, before.EndUtc, before.RescheduleCount, before.RescheduledAt), (after.StartUtc, after.EndUtc, (short)0, (DateTimeOffset?)null));
        Assert.DoesNotContain(await Resched.Rows(h, id), n => n.Kind == NotificationKind.Rescheduled);
    }

    [Fact]
    public async Task A_second_reschedule_is_409_invalid_transition_and_keeps_the_first_move()
    {
        var (h, _, id) = await Resched.Confirmed(H25);
        await using var _ = h;
        Assert.Equal(HttpStatusCode.OK, (await Resched.Move(h, id, Life.Client, Resched.Wednesday10)).StatusCode);

        var r = await Resched.Move(h, id, Life.Client, "2026-11-19T10:00:00Z");

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("invalid_transition", await Resched.Code(r));
        var row = await Life.Row(h, id);
        Assert.Equal((DateTimeOffset.Parse(Resched.Wednesday10), (short)1), (row.StartUtc, row.RescheduleCount));
        Assert.Single(await Resched.Rows(h, id), n => n.Kind == NotificationKind.Rescheduled && n.ToUserId == Life.Client);
    }

    [Theory]
    [InlineData(BookingStatus.PendingPayment)]
    [InlineData(BookingStatus.CancelledByClient)]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.Disputed)]
    public async Task A_booking_that_is_not_Confirmed_is_409_invalid_transition(BookingStatus status)
    {
        var (h, _, id) = await Resched.Confirmed(H25, status);
        await using var _ = h;
        var r = await Resched.Move(h, id, Life.Client, Resched.Wednesday10);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("invalid_transition", await Resched.Code(r));
        Assert.Equal(0, (await Life.Row(h, id)).RescheduleCount);
    }

    [Fact]
    public async Task The_practitioner_of_the_booking_is_403_a_stranger_and_an_unknown_id_are_404_and_no_credentials_is_401()
    {
        var (h, _, id) = await Resched.Confirmed(H25);
        await using var _ = h;

        var practitioner = await Resched.Move(h, id, Life.Practitioner, Resched.Wednesday10);
        Assert.Equal(HttpStatusCode.Forbidden, practitioner.StatusCode);
        Assert.Equal("forbidden", await Resched.Code(practitioner));
        Assert.Equal(HttpStatusCode.NotFound, (await Resched.Move(h, id, "demo-stranger", Resched.Wednesday10)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Resched.Move(h, Guid.NewGuid(), Life.Client, Resched.Wednesday10)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Resched.Move(h, id, null, Resched.Wednesday10)).StatusCode);
        Assert.Equal(0, (await Life.Row(h, id)).RescheduleCount);
    }

    [Theory]
    [InlineData("2026-11-21T10:00:00Z")] // a Saturday: no rule
    [InlineData("2026-11-18T10:15:00Z")] // not a 30 minute step of the grid
    [InlineData("2026-11-18T17:00:00Z")] // 18:00 local, after the work hours
    public async Task A_time_the_calculator_does_not_offer_is_422_slot_unavailable(string startUtc)
    {
        var (h, _, id) = await Resched.Confirmed(H25);
        await using var _ = h;
        var r = await Resched.Move(h, id, Life.Client, startUtc);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("slot_unavailable", await Resched.Code(r));
        Assert.Equal(0, (await Life.Row(h, id)).RescheduleCount);
    }

    [Fact]
    public async Task The_slot_the_booking_already_holds_is_422_slot_unavailable()
    {
        var (h, _, id) = await Resched.Confirmed(H25);
        await using var _ = h;
        var held = (await Life.Row(h, id)).StartUtc;
        var r = await Resched.Move(h, id, Life.Client, held.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
        Assert.Equal("slot_unavailable", await Resched.Code(r));
        Assert.Equal(0, (await Life.Row(h, id)).RescheduleCount);
    }

    [Fact]
    public async Task A_slot_held_by_another_booking_is_409_slot_taken()
    {
        var (h, _, id) = await Resched.Confirmed(H25);
        await using var _ = h;
        var other = await Life.Seed(h, BookingStatus.Confirmed, DateTimeOffset.Parse(Resched.Wednesday10) - h.Clock.GetUtcNow());

        var r = await Resched.Move(h, id, Life.Client, Resched.Wednesday10);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("slot_taken", await Resched.Code(r));
        Assert.Equal(DateTimeOffset.Parse(Resched.Wednesday10), (await Life.Row(h, other)).StartUtc);
        Assert.Equal(0, (await Life.Row(h, id)).RescheduleCount);
    }

    [Fact]
    public async Task A_booking_may_move_by_half_an_hour_into_its_own_old_interval()
    {
        var (h, _, id) = await Resched.Confirmed(H25); // Tuesday 13:00 UTC
        await using var _ = h;
        var old = (await Life.Row(h, id)).StartUtc;
        var r = await Resched.Move(h, id, Life.Client, old.AddMinutes(30).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(old.AddMinutes(30), (await Life.Row(h, id)).StartUtc);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"startUtc\":null}")]
    [InlineData("{\"startUtc\":\"2026-11-18T11:00:00+01:00\"}")] // an offset other than Z
    public async Task A_missing_or_non_UTC_start_is_422_validation_failed(string json)
    {
        var (h, _, id) = await Resched.Confirmed(H25);
        await using var _ = h;
        var r = await h.ClientFor(Life.Client).PostAsync($"/api/v1/bookings/{id}/reschedule", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("validation_failed", await Resched.Code(r));
        Assert.Equal(0, (await Life.Row(h, id)).RescheduleCount);
    }

    [Fact]
    public async Task The_old_slot_can_be_booked_afterwards()
    {
        var (h, _, id) = await Resched.Confirmed(H25);
        await using var _ = h;
        var old = (await Life.Row(h, id)).StartUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        // before the move the slot is taken
        Assert.Equal(HttpStatusCode.Conflict, (await CreateHost.Post(h.ClientFor("demo-client-2"), h.Req(old), CreateHost.Key())).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Resched.Move(h, id, Life.Client, Resched.Wednesday10)).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await CreateHost.Post(h.ClientFor("demo-client-2"), h.Req(old), CreateHost.Key())).StatusCode);
    }

    [Fact]
    public async Task The_booking_view_offers_canReschedule_to_the_client_only_until_it_is_used()
    {
        var (h, _, id) = await Resched.Confirmed(H25);
        await using var _ = h;
        async Task<bool> Can(string user) => (await h.ClientFor(user).GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/v1/bookings/{id}")).GetProperty("canReschedule").GetBoolean();

        Assert.True(await Can(Life.Client));
        Assert.False(await Can(Life.Practitioner));
        await Resched.Move(h, id, Life.Client, Resched.Wednesday10);
        Assert.False(await Can(Life.Client));
    }
}
