using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Gabinety.Booking.Features.Internal.TestClock;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Booking.Tests;

public class ClockApiTests
{
    internal const string InternalKey = "k-internal-DO-NOT-LEAK-5678";

    /// <summary>A host in the fake profile (GABINETY_FAKE=1) whose manual clock is wrapped by the offset clock, as in a real fake-profile start.</summary>
    internal static CreateHost FakeHost(Action<CreateHost>? more = null)
    {
        var h = new CreateHost();
        h.Settings["Internal:Key"] = InternalKey;
        h.Settings["GABINETY_FAKE"] = "1";
        h.Configure = s => s.AddTestClock();
        more?.Invoke(h);
        return h;
    }

    internal static Task<HttpResponseMessage> PostRaw(CreateHost h, string path, string json, string? key = InternalKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (key != null) req.Headers.Add("X-Internal-Key", key);
        return h.CreateClient().SendAsync(req);
    }

    static Task<HttpResponseMessage> SetOffset(CreateHost h, object offset, string? key = InternalKey) =>
        PostRaw(h, "/internal/v1/test/clock", JsonSerializer.Serialize(new { offsetMinutes = offset }), key);

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Outside_the_fake_profile_both_endpoints_are_not_mapped(string kind)
    {
        await using var h = new CreateHost().AsDeployed(kind);
        h.Settings["Internal:Key"] = InternalKey;
        h.Configure += s => s.AddTestClock(); // even a leaked registration must not make the route appear
        Assert.Equal(HttpStatusCode.NotFound, (await SetOffset(h, 60)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostRaw(h, "/internal/v1/demo/reset", "{}")).StatusCode);
    }

    [Fact]
    public async Task With_no_profile_settings_at_all_both_endpoints_are_not_mapped()
    {
        await using var h = new CreateHost().AsDeployed();
        h.Settings["Internal:Key"] = InternalKey;
        Assert.Equal(HttpStatusCode.NotFound, (await SetOffset(h, 60)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostRaw(h, "/internal/v1/demo/reset", "{}")).StatusCode);
    }

    [Fact]
    public async Task Fake_flag_other_than_1_does_not_map_the_endpoints()
    {
        await using var h = new CreateHost().AsDeployed();
        h.Settings["Internal:Key"] = InternalKey;
        h.Settings["GABINETY_FAKE"] = "0";
        Assert.Equal(HttpStatusCode.NotFound, (await SetOffset(h, 60)).StatusCode);
    }

    [Fact]
    public async Task Environment_kind_Development_maps_the_endpoints()
    {
        await using var h = new CreateHost().AsDeployed("Development"); // Environment:Kind alone, with GABINETY_FAKE=0
        h.Settings["Internal:Key"] = InternalKey;
        h.Configure += s => s.AddTestClock();
        Assert.Equal(HttpStatusCode.OK, (await SetOffset(h, 60)).StatusCode);
    }

    [Fact]
    public async Task The_offset_moves_now_forward_and_back_and_zero_resets()
    {
        await using var h = FakeHost();
        var baseNow = h.Clock.GetUtcNow();

        var fwd = await SetOffset(h, 1440);
        Assert.Equal(HttpStatusCode.OK, fwd.StatusCode);
        var j = await fwd.Body();
        Assert.Equal(1440, j.GetProperty("offsetMinutes").GetInt64());
        Assert.Equal(baseNow.AddDays(1), j.GetProperty("now").GetDateTimeOffset());

        var back = await (await SetOffset(h, -60)).Body();
        Assert.Equal(-60, back.GetProperty("offsetMinutes").GetInt64());
        Assert.Equal(baseNow.AddHours(-1), back.GetProperty("now").GetDateTimeOffset());

        var zero = await (await SetOffset(h, 0)).Body();
        Assert.Equal(0, zero.GetProperty("offsetMinutes").GetInt64());
        Assert.Equal(baseNow, zero.GetProperty("now").GetDateTimeOffset());
    }

    static Task<HttpResponseMessage> GetClock(CreateHost h, string? key = InternalKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/internal/v1/test/clock");
        if (key != null) req.Headers.Add("X-Internal-Key", key);
        return h.CreateClient().SendAsync(req);
    }

    [Fact]
    public async Task GET_clock_reads_the_offset_and_now_without_changing_them()
    {
        await using var h = FakeHost();
        var baseNow = h.Clock.GetUtcNow();
        var fresh = await (await GetClock(h)).Body();
        Assert.Equal(0, fresh.GetProperty("offsetMinutes").GetInt64());
        Assert.Equal(baseNow, fresh.GetProperty("now").GetDateTimeOffset());

        await SetOffset(h, 90);
        for (var i = 0; i < 2; i++) // a read twice: it changes nothing
        {
            var j = await (await GetClock(h)).Body();
            Assert.Equal(90, j.GetProperty("offsetMinutes").GetInt64());
            Assert.Equal(baseNow.AddMinutes(90), j.GetProperty("now").GetDateTimeOffset());
        }
    }

    [Fact]
    public async Task GET_clock_needs_the_internal_key_and_exists_only_outside_a_deployed_environment()
    {
        await using var h = FakeHost();
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetClock(h, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetClock(h, "wrong-key")).StatusCode);

        await using var prod = new CreateHost().AsDeployed("Production");
        prod.Settings["Internal:Key"] = InternalKey;
        prod.Configure += s => s.AddTestClock();
        Assert.Equal(HttpStatusCode.NotFound, (await GetClock(prod)).StatusCode);
    }

    [Fact]
    public async Task Every_reader_of_TimeProvider_sees_the_offset_time()
    {
        await using var h = FakeHost();
        await SetOffset(h, 2880);
        var seen = h.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        Assert.Equal(h.Clock.GetUtcNow().AddDays(2), seen);
    }

    [Theory]
    [InlineData(5_259_601)]
    [InlineData(-5_259_601)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public async Task An_offset_beyond_ten_years_is_400_validation_failed_and_changes_nothing(long minutes)
    {
        await using var h = FakeHost();
        await SetOffset(h, 30);
        var r = await SetOffset(h, minutes);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("validation_failed", (await r.Body()).GetProperty("code").GetString());
        Assert.Equal(30, (await (await SetOffset(h, 30)).Body()).GetProperty("offsetMinutes").GetInt64());
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"offsetMinutes":null}""")]
    [InlineData("""{"offsetMinutes":"60"}""")]
    [InlineData("""{"offsetMinutes":1.5}""")]
    [InlineData("""not json""")]
    [InlineData("""[]""")]
    public async Task A_missing_or_non_integer_offset_is_400_validation_failed(string body)
    {
        await using var h = FakeHost();
        var r = await PostRaw(h, "/internal/v1/test/clock", body);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("validation_failed", (await r.Body()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_range_edges_are_accepted()
    {
        await using var h = FakeHost();
        Assert.Equal(HttpStatusCode.OK, (await SetOffset(h, 5_259_600)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetOffset(h, -5_259_600)).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task A_missing_or_wrong_internal_key_is_401(string? key)
    {
        await using var h = FakeHost();
        Assert.Equal(HttpStatusCode.Unauthorized, (await SetOffset(h, 60, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostRaw(h, "/internal/v1/demo/reset", "{}", key)).StatusCode);
    }

    [Fact]
    public async Task A_booking_created_after_moving_the_clock_uses_the_offset_time_for_created_at_and_the_hold()
    {
        await using var h = FakeHost();
        await h.WorkWeek();
        await SetOffset(h, 60); // now = 2026-11-16 13:00Z, the slot is Tuesday 10:00Z
        var r = await CreateHost.Post(h.ClientFor("demo-client-clock"), h.Req(), CreateHost.Key());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal("2026-11-16T13:10:00Z", (await r.Body()).GetProperty("holdExpiresAt").GetString());
        var b = (await h.Bookings()).Single();
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 13, 0, 0, TimeSpan.Zero), b.CreatedAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 13, 10, 0, TimeSpan.Zero), b.HoldExpiresAt);
    }
}
