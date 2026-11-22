extern alias BookingApp;

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gabinety.DemoSeeder;
using Microsoft.EntityFrameworkCore;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Integration.Tests.Seeder;

/// <summary>
/// T-08.01b: the Booking step against in-process Identity, Directory and Booking (InMemory). Expected values are written here, not read from the seeder's plan.
/// The first run is shared (class fixture) by the read-only tests of this class; the tests that run again or break something use a world of their own.
/// </summary>
public sealed class BookingSeedFixture : IAsyncLifetime
{
    public BookingWorld World { get; } = new();
    CompletionJobStandIn _job = null!;

    public async Task InitializeAsync()
    {
        await World.SeedDirectoryAsync();
        _job = new CompletionJobStandIn(World.Booking);
        await BookingWorld.Step().RunAsync(World.Context, CancellationToken.None);
    }

    public async Task DisposeAsync() { await _job.DisposeAsync(); await World.DisposeAsync(); }
}

public class BookingSeedStepTests(BookingSeedFixture fx) : IClassFixture<BookingSeedFixture>
{
    static readonly Dictionary<string, int> ExpectedPerStatus = new()
    {
        ["Confirmed"] = 2, ["PendingPayment"] = 1, ["Expired"] = 1, ["CancelledByClient"] = 1, ["LateCancelledByClient"] = 1,
        ["Completed"] = 3, ["ClientNoShow"] = 1, ["Disputed"] = 1, ["Resolved"] = 1,
    };

    BookingWorld _w => fx.World;

    [Fact]
    public async Task Bookings_exist_in_the_expected_count_per_status_read_through_the_API()
    {
        var counts = BookingWorld.StatusCounts(await _w.AllClientBookings());
        Assert.True(counts.Count > 0, _w.Log.Text);
        Assert.Equal(ExpectedPerStatus.OrderBy(p => p.Key), counts.OrderBy(p => p.Key));
    }

    [Fact]
    public async Task Every_approved_practitioner_has_weekday_availability_on_half_hours_and_the_others_have_none()
    {
        foreach (var (key, p) in _w.Context.Practitioners)
        {
            using var http = _w.Booking.CreateClient();
            http.DefaultRequestHeaders.Authorization = new("Bearer", await _w.TokenAsync(key));
            var rules = (await http.GetFromJsonAsync<JsonElement>("/api/v1/me/availability")).GetProperty("rules").EnumerateArray().ToList();
            if (p.Status != "Approved") { Assert.Empty(rules); continue; }
            Assert.NotEmpty(rules);
            Assert.All(rules, r =>
            {
                Assert.InRange(r.GetProperty("isoWeekday").GetInt32(), 1, 5);
                Assert.Matches(@"^\d\d:(00|30)$", r.GetProperty("startLocal").GetString()!);
                Assert.Matches(@"^\d\d:(00|30)$", r.GetProperty("endLocal").GetString()!);
            });
        }
    }

    [Fact]
    public async Task Every_booking_carries_a_demo_key_and_an_idempotency_key_derived_from_it()
    {
        var rows = await _w.Booking.Bookings();
        Assert.Equal(ExpectedPerStatus.Values.Sum(), rows.Count);
        foreach (var b in rows)
        {
            var key = Regex(b.BriefText);
            Assert.StartsWith("demo-booking-", key);
            // the expected derivation is written out here: the first 16 bytes of SHA-256 over a fixed prefix and the key
            var expected = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("gabinety-demo-booking:" + key))[..16]);
            Assert.Equal(expected, b.IdempotencyKey);
        }
        Assert.Equal(rows.Count, rows.Select(b => Regex(b.BriefText)).Distinct().Count());
    }

    static string Regex(string brief) => System.Text.RegularExpressions.Regex.Match(brief, @"^\[(demo-[a-z0-9-]+)\]").Groups[1].Value;

    [Fact]
    public async Task Completed_bookings_are_reviewable_by_Bookings_internal_endpoint_and_the_others_in_the_review_path_are_not()
    {
        using var http = _w.Booking.CreateClient();
        http.DefaultRequestHeaders.Add("X-Internal-Key", BookingHost.Key);
        var rows = await _w.Booking.Bookings();
        var completed = rows.Where(b => b.Status == BookingStatus.Completed).ToList();
        Assert.Equal(3, completed.Count);
        foreach (var b in completed)
        {
            var r = await http.GetFromJsonAsync<JsonElement>($"/internal/v1/bookings/{b.Id}/reviewability");
            Assert.True(r.GetProperty("reviewable").GetBoolean(), $"{Regex(b.BriefText)} is not reviewable");
        }
        // three different practitioners, so Reputation can seed reviews for more than one of them
        Assert.True(completed.Select(b => b.PractitionerId).Distinct().Count() >= 3);
        var notDone = rows.First(b => b.Status == BookingStatus.Confirmed);
        Assert.False((await http.GetFromJsonAsync<JsonElement>($"/internal/v1/bookings/{notDone.Id}/reviewability")).GetProperty("reviewable").GetBoolean());
    }

    [Fact]
    public async Task Demo_client_has_a_Completed_booking_inside_its_dispute_window_and_the_API_says_so()
    {
        var mine = await _w.AllClientBookings();   // demo-client is the first client
        var disputable = mine.Where(b => b.GetProperty("status").GetString() == "Completed" && b.GetProperty("canDispute").GetBoolean()).ToList();
        Assert.NotEmpty(disputable);
        foreach (var b in disputable) // "inside the window" is measured against the service clock, not this machine's
            Assert.True(_w.Booking.Now < b.GetProperty("startUtc").GetDateTimeOffset() + TimeSpan.FromHours(72));
    }

    [Fact]
    public void The_clock_is_back_at_its_baseline()
    {
        Assert.Equal(0, _w.Booking.ClockOffsetMinutes);
    }

    [Fact]
    public async Task The_pending_booking_has_a_live_hold_and_the_expired_one_does_not()
    {
        var all = await _w.AllClientBookings();
        var pending = all.Single(b => b.GetProperty("status").GetString() == "PendingPayment");
        Assert.True(pending.GetProperty("holdExpiresAt").GetDateTimeOffset() > _w.Booking.Now);
    }

    [Fact]
    public async Task Dispute_and_resolution_leave_a_Disputed_and_a_Resolved_booking_with_a_release_decision()
    {
        var rows = await _w.Booking.Bookings();
        Assert.Equal("release", rows.Single(b => b.Status == BookingStatus.Resolved).DisputeResolution);
        Assert.NotNull(rows.Single(b => b.Status == BookingStatus.Disputed).DisputeOpenedAt);
    }

    [Fact]
    public void No_token_internal_key_or_password_reaches_the_log()
    {
        var text = _w.Log.Text;
        Assert.Contains("booking: demo-booking-", text); // the step did log, with demo keys
        Assert.DoesNotContain(BookingHost.Key, text);
        Assert.DoesNotContain(DirectoryHost.Key, text);
        Assert.DoesNotContain(SeedOptions.DevIdentityPassword, text);
        Assert.DoesNotContain("eyJ", text);
        Assert.DoesNotContain("Bearer", text);
        Assert.DoesNotContain("demo-brief", text);
    }

    [Fact]
    public async Task A_second_run_changes_nothing_and_writes_nothing_but_the_clock_baseline()
    {
        var before = await Snapshot();
        _w.Calls.Requests.Clear();
        var fresh = _w.NewContext(); // a new run as the app does it: the directory step fills the context again, then the booking step must find everything through the API
        await new DirectorySeedStep().RunAsync(fresh, CancellationToken.None);
        await BookingWorld.Step().RunAsync(fresh, CancellationToken.None);
        Assert.Equal(before, await Snapshot());
        var writes = _w.Calls.Requests.Where(r => r.Method != HttpMethod.Get && r.Path != "/internal/v1/test/clock").ToList();
        Assert.True(writes.Count == 0, "a second run wrote: " + string.Join(", ", writes.Select(w => $"{w.Method} {w.Path}")));
        Assert.Equal(0, _w.Booking.ClockOffsetMinutes);
    }

    async Task<string> Snapshot() => await _w.Booking.Db(async db => JsonSerializer.Serialize(new
    {
        B = await db.Bookings.AsNoTracking().OrderBy(b => b.Id).ToListAsync(),
        P = await db.Payments.AsNoTracking().OrderBy(p => p.Id).ToListAsync(),
        L = await db.LedgerEntries.AsNoTracking().OrderBy(l => l.Id).ToListAsync(),
        R = await db.Rules.AsNoTracking().OrderBy(r => r.Id).ToListAsync(),
        I = await db.IdempotencyRecords.AsNoTracking().CountAsync(),
    }));

    [Fact]
    public void The_step_is_registered_after_the_directory_step()
    {
        var names = SeedSteps.All.Select(s => (s.Name, s.Order)).ToList();
        Assert.Contains(("booking", 200), names);
        Assert.True(names.IndexOf(("directory", 100)) < names.IndexOf(("booking", 200)));
    }
}

/// <summary>Runs that break something or lack something get a world of their own.</summary>
public class BookingSeedStepFailureTests
{
    [Fact]
    public async Task The_clock_is_restored_when_a_step_fails_midway()
    {
        await using var w = new BookingWorld();
        await w.SeedDirectoryAsync();
        w.Calls.FailWhen = r => r.RequestUri!.AbsolutePath.EndsWith("/dispute"); // after the clock has been moved forward for the late actions
        await using var job = new CompletionJobStandIn(w.Booking);

        await Assert.ThrowsAnyAsync<Exception>(() => BookingWorld.Step().RunAsync(w.Context, CancellationToken.None));

        Assert.Equal(0, w.Booking.ClockOffsetMinutes);
        Assert.Contains(w.Calls.Requests, r => r.Path == "/internal/v1/test/clock"); // it did move it
    }

    [Fact]
    public async Task A_baseline_other_than_zero_is_restored_exactly()
    {
        await using var w = new BookingWorld();
        await w.SeedDirectoryAsync();
        await using var job = new CompletionJobStandIn(w.Booking);
        var step = BookingWorld.Step();
        step.BaselineOffsetMinutes = 90;

        await step.RunAsync(w.Context, CancellationToken.None);

        Assert.Equal(90, w.Booking.ClockOffsetMinutes);
        Assert.Equal(12, (await w.Booking.Bookings()).Count);
    }

    [Fact]
    public async Task Unless_told_otherwise_the_baseline_is_what_the_clock_reads_at_the_start_and_it_is_restored_exactly()
    {
        await using var w = new BookingWorld();
        await w.SeedDirectoryAsync();
        await using var job = new CompletionJobStandIn(w.Booking);
        w.Booking.SetClockOffset(180); // a running system whose clock was moved before the seeder started
        var step = BookingWorld.Step(); // BaselineOffsetMinutes not set

        await step.RunAsync(w.Context, CancellationToken.None);

        Assert.Equal(180, w.Booking.ClockOffsetMinutes);
        Assert.Contains(w.Calls.Requests, r => r.Method == HttpMethod.Get && r.Path == "/internal/v1/test/clock");
        Assert.Equal(12, (await w.Booking.Bookings()).Count);
    }

    [Fact]
    public async Task Without_the_completion_job_the_run_still_finishes_the_other_states_says_so_and_restores_the_clock()
    {
        await using var w = new BookingWorld(); // no CompletionJobStandIn: main has no auto-complete job yet
        await w.SeedDirectoryAsync();

        await BookingWorld.Step(TimeSpan.Zero).RunAsync(w.Context, CancellationToken.None);

        var counts = BookingWorld.StatusCounts(await w.AllClientBookings());
        Assert.False(counts.ContainsKey("Completed"));
        Assert.Equal(1, counts["Resolved"]);
        Assert.Equal(1, counts["ClientNoShow"]);
        Assert.Contains("completed", w.Log.Text);
        Assert.Contains("auto-complete", w.Log.Text);
        Assert.Equal(0, w.Booking.ClockOffsetMinutes);
    }

    [Fact]
    public async Task Reset_calls_Bookings_demo_reset_with_the_internal_key_and_removes_bookings_of_demo_users()
    {
        await using var w = new BookingWorld();
        await w.SeedDirectoryAsync();
        await w.Booking.Db(async db =>
        {
            db.Bookings.Add(new()
            {
                Id = Guid.NewGuid(), ClientUserId = "demo-reset-client", PractitionerId = Guid.NewGuid(), PractitionerUserId = "someone-else", OfferingId = Guid.NewGuid(),
                OfferingTitle = "demo", DurationMinutes = 60, PriceMinor = 10000, StartUtc = DateTimeOffset.UtcNow.AddDays(3), EndUtc = DateTimeOffset.UtcNow.AddDays(3).AddHours(1),
                Status = BookingStatus.Confirmed, BriefText = "demo", IdempotencyKey = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Bookings.Add(new()
            {
                Id = Guid.NewGuid(), ClientUserId = "real-client", PractitionerId = Guid.NewGuid(), PractitionerUserId = "real-practitioner", OfferingId = Guid.NewGuid(),
                OfferingTitle = "real", DurationMinutes = 60, PriceMinor = 10000, StartUtc = DateTimeOffset.UtcNow.AddDays(4), EndUtc = DateTimeOffset.UtcNow.AddDays(4).AddHours(1),
                Status = BookingStatus.Confirmed, BriefText = "real", IdempotencyKey = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            return 0;
        });

        var removed = await new BookingSeedStep().ResetAsync(w.Context, CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.Equal(["real-client"], (await w.Booking.Bookings()).Select(b => b.ClientUserId));
    }

    [Fact]
    public async Task Without_a_Booking_endpoint_the_step_says_so_and_does_nothing()
    {
        await using var w = new BookingWorld();
        var context = new SeedContext(new SeedOptions("http://localhost", DirectoryHost.Key, DirectoryHost.Authority, SeedOptions.DevIdentityPassword), w.Http, w.Log, TimeProvider.System);

        await new BookingSeedStep().RunAsync(context, CancellationToken.None);

        Assert.Contains("booking: skipped", w.Log.Text);
        Assert.Empty(w.Calls.Requests);
    }
}
