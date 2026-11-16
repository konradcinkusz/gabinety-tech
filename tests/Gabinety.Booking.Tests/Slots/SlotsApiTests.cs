using System.Net;
using System.Text.Json;
using Gabinety.Billing;

namespace Gabinety.Booking.Tests;

public class SlotsApiTests : IDisposable
{
    // now = Mon 2026-11-16 12:00 UTC (13:00 Warsaw). Lead 12 h: earliest slot is Mon 17:00 UTC; use Tue 2026-11-17 for assertions.
    // Tue 2026-11-17, Warsaw is UTC+1: 09:00 local = 08:00 UTC.
    static readonly DateTimeOffset TueNineLocal = new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);
    const string Tue = "2026-11-17";
    readonly SlotsHost f = new();
    public void Dispose() => f.Dispose();

    async Task<(HttpStatusCode Status, JsonElement Body)> Get(string from, string to)
    {
        var r = await f.CreateClient().GetAsync(f.Url(from, to));
        return (r.StatusCode, await r.Content.ReadAsStringAsync() is { Length: > 0 } s ? JsonDocument.Parse(s).RootElement.Clone() : default);
    }

    async Task<List<DateTimeOffset>> Starts(string from = Tue, string to = Tue)
    {
        var (status, body) = await Get(from, to);
        Assert.Equal(HttpStatusCode.OK, status);
        return body.GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("startUtc").GetDateTimeOffset()).ToList();
    }

    [Fact]
    public async Task Public_endpoint_lists_every_half_hour_of_the_window_without_a_token_and_is_no_store()
    {
        await f.WorkWeek();
        var r = await f.CreateClient().GetAsync(f.Url(Tue, Tue));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
        var slots = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("slots");
        Assert.Equal(15, slots.GetArrayLength()); // 09:00..16:00 local start, 60 min
        Assert.Equal(TueNineLocal, slots[0].GetProperty("startUtc").GetDateTimeOffset());
        Assert.Equal(TueNineLocal.AddHours(1), slots[0].GetProperty("endUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task A_booked_slot_disappears_and_so_does_a_slot_inside_its_buffer_but_the_one_after_the_buffer_stays()
    {
        await f.WorkWeek();
        // Booked 10:00-11:00 UTC (the 60 minute slot, buffer to 11:15). A 60 minute slot carries its own buffer, so it conflicts from 09:00 to 11:00.
        await f.Book(BookingStatus.Confirmed, TueNineLocal.AddHours(2));
        var starts = await Starts();
        Assert.Contains(TueNineLocal.AddMinutes(30), starts);                   // 08:30-09:30 (+15) ends before 10:00
        foreach (var blocked in new[] { 60, 120, 150, 180 })                    // 09:00, 10:00 (booked), 10:30, 11:00 (inside the buffer)
            Assert.DoesNotContain(TueNineLocal.AddMinutes(blocked), starts);
        Assert.Contains(TueNineLocal.AddMinutes(210), starts);                  // 11:30 is the first start after the buffer
    }

    [Fact]
    public async Task An_expired_hold_frees_the_slot_and_a_live_hold_blocks_it()
    {
        await f.WorkWeek();
        var now = f.Clock.GetUtcNow();
        await f.Book(BookingStatus.PendingPayment, TueNineLocal.AddHours(2), hold: now.AddMinutes(-1));
        await f.Book(BookingStatus.PendingPayment, TueNineLocal.AddHours(5), hold: now.AddMinutes(10));
        var starts = await Starts();
        Assert.Contains(TueNineLocal.AddHours(2), starts);
        Assert.DoesNotContain(TueNineLocal.AddHours(5), starts);
    }

    [Fact]
    public async Task A_hold_that_expires_as_the_clock_moves_frees_the_slot()
    {
        await f.WorkWeek();
        await f.Book(BookingStatus.PendingPayment, TueNineLocal.AddHours(2), hold: f.Clock.GetUtcNow().AddMinutes(10));
        Assert.DoesNotContain(TueNineLocal.AddHours(2), await Starts());
        f.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Contains(TueNineLocal.AddHours(2), await Starts());
    }

    [Theory]
    [InlineData(BookingStatus.Confirmed, true)]
    [InlineData(BookingStatus.Disputed, true)]
    [InlineData(BookingStatus.CancelledByClient, false)]
    [InlineData(BookingStatus.LateCancelledByClient, false)]
    [InlineData(BookingStatus.CancelledByPractitioner, false)]
    [InlineData(BookingStatus.Expired, false)]
    [InlineData(BookingStatus.Completed, false)]
    [InlineData(BookingStatus.ClientNoShow, false)]
    [InlineData(BookingStatus.Resolved, false)]
    public async Task Only_active_statuses_block(BookingStatus status, bool blocks)
    {
        await f.WorkWeek();
        await f.Book(status, TueNineLocal.AddHours(2));
        Assert.Equal(!blocks, (await Starts()).Contains(TueNineLocal.AddHours(2)));
    }

    [Fact]
    public async Task A_booking_the_evening_before_blocks_through_its_buffer_across_the_window_start()
    {
        await f.WorkWeek();
        // Booked 06:10-08:10 UTC: the buffer runs to 08:25, so the 08:00 UTC slot is blocked.
        await f.Book(BookingStatus.Confirmed, TueNineLocal.AddMinutes(-110), 120);
        Assert.DoesNotContain(TueNineLocal, await Starts());
    }

    [Fact]
    public async Task A_day_with_an_exception_has_no_slots_and_a_day_without_a_rule_has_none()
    {
        await f.WorkWeek();
        await f.Seed(db => db.Exceptions.Add(new() { Id = Guid.NewGuid(), PractitionerId = f.Practitioner, DateLocal = new(2026, 11, 17) }));
        Assert.Empty(await Starts(Tue, Tue));
        Assert.Empty(await Starts("2026-11-21", "2026-11-22")); // Sat, Sun
        Assert.NotEmpty(await Starts("2026-11-18", "2026-11-18"));
    }

    [Fact]
    public async Task Lead_time_is_taken_from_the_time_provider()
    {
        await f.WorkWeek();
        // now 12:00 UTC Monday; Monday slots start at 08:00 UTC (09:00 local). Earliest allowed is 00:00 UTC Tuesday.
        Assert.Empty(await Starts("2026-11-16", "2026-11-16"));
        f.Clock.Advance(TimeSpan.FromHours(-24)); // now = Sunday 12:00 UTC: Monday 08:00 UTC is 20 h away, so allowed
        Assert.Equal(TueNineLocal.AddDays(-1), (await Starts("2026-11-16", "2026-11-16"))[0]);
    }

    [Fact]
    public async Task Other_practitioners_bookings_do_not_block()
    {
        await f.WorkWeek();
        await f.Seed(db => db.Bookings.Add(Slots.NewBooking(Guid.NewGuid(), TueNineLocal.AddHours(2), 60, BookingStatus.Confirmed)));
        Assert.Contains(TueNineLocal.AddHours(2), await Starts());
    }

    [Fact]
    public async Task A_range_of_exactly_14_days_is_accepted_and_15_days_is_400()
    {
        await f.WorkWeek();
        Assert.Equal(HttpStatusCode.OK, (await Get("2026-11-17", "2026-11-30")).Status);
        var (status, body) = await Get("2026-11-17", "2026-12-01");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("errors", out _));
    }

    [Theory]
    [InlineData("2026-11-17", "2026-11-16")]
    [InlineData("17-11-2026", "2026-11-18")]
    [InlineData("2026-11-17", "nonsense")]
    [InlineData("", "2026-11-18")]
    public async Task Missing_malformed_or_reversed_dates_are_400(string from, string to)
    {
        var (status, body) = await Get(from, to);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_missing_offering_id_is_400()
    {
        var r = await f.CreateClient().GetAsync($"/api/v1/practitioners/{f.Practitioner}/slots?from={Tue}&to={Tue}");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task An_unknown_offering_and_an_offering_of_another_practitioner_are_404()
    {
        var c = f.CreateClient();
        var unknown = await c.GetAsync($"/api/v1/practitioners/{f.Practitioner}/slots?offeringId={Guid.NewGuid()}&from={Tue}&to={Tue}");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        f.OfferingPractitioner = Guid.NewGuid();
        var (status, body) = await Get(Tue, Tue);
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_inactive_offering_is_422_offering_inactive()
    {
        f.Active = false;
        var (status, body) = await Get(Tue, Tue);
        Assert.Equal((HttpStatusCode)422, status);
        Assert.Equal("offering_inactive", body.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Submitted")]
    [InlineData("Rejected")]
    [InlineData("Suspended")]
    public async Task A_practitioner_who_is_not_Approved_is_422_practitioner_not_bookable(string practitionerStatus)
    {
        f.Status = practitionerStatus;
        var (status, body) = await Get(Tue, Tue);
        Assert.Equal((HttpStatusCode)422, status);
        Assert.Equal("practitioner_not_bookable", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Directory_down_is_503_dependency_unavailable()
    {
        f.Directory.Down = true;
        var r = await f.CreateClient().GetAsync(f.Url(Tue, Tue));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        var text = await r.Content.ReadAsStringAsync();
        Assert.Contains("dependency_unavailable", text);
        Assert.DoesNotContain(FakeDirectory.Key, text);
    }

    [Fact]
    public async Task The_offering_duration_sets_the_slot_length()
    {
        f.DurationMinutes = 30;
        await f.WorkWeek();
        var (_, body) = await Get(Tue, Tue);
        var slots = body.GetProperty("slots");
        Assert.Equal(16, slots.GetArrayLength()); // 09:00..16:30
        Assert.Equal(TueNineLocal.AddMinutes(30), slots[0].GetProperty("endUtc").GetDateTimeOffset());
    }
}
