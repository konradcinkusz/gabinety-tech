using System.Net;
using System.Text.Json;
using Gabinety.Reputation.Features.Internal.TestClock;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Reputation.Tests;

public class ClockApiTests
{
    static Task<HttpResponseMessage> SetOffset(InternalHost h, object offset, string? key = InternalHost.Key) =>
        h.PostRaw("/internal/v1/test/clock", JsonSerializer.Serialize(new { offsetMinutes = offset }), key);

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Outside_the_fake_profile_both_endpoints_are_not_mapped(string kind)
    {
        await using var h = new InternalHost { Wrap = true }; // even a leaked registration must not make the route appear
        h.Settings["Environment:Kind"] = kind;
        Assert.Equal(HttpStatusCode.NotFound, (await SetOffset(h, 60)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.PostRaw("/internal/v1/demo/reset", "{}")).StatusCode);
    }

    [Fact]
    public async Task With_no_profile_settings_and_with_the_flag_set_to_0_the_endpoints_are_not_mapped()
    {
        await using var none = new InternalHost();
        Assert.Equal(HttpStatusCode.NotFound, (await SetOffset(none, 60)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await none.PostRaw("/internal/v1/demo/reset", "{}")).StatusCode);
        await using var zero = new InternalHost();
        zero.Settings["GABINETY_FAKE"] = "0";
        Assert.Equal(HttpStatusCode.NotFound, (await SetOffset(zero, 60)).StatusCode);
    }

    [Fact]
    public async Task Environment_kind_Development_maps_the_endpoints()
    {
        await using var h = new InternalHost { Wrap = true };
        h.Settings["Environment:Kind"] = "Development";
        Assert.Equal(HttpStatusCode.OK, (await SetOffset(h, 60)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.PostRaw("/internal/v1/demo/reset", "{}")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task A_missing_or_wrong_internal_key_is_401(string? key)
    {
        await using var h = InternalHost.Fake();
        Assert.Equal(HttpStatusCode.Unauthorized, (await SetOffset(h, 60, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.PostRaw("/internal/v1/demo/reset", "{}", key)).StatusCode);
    }

    [Fact]
    public async Task The_offset_moves_now_forward_and_back_and_zero_resets()
    {
        await using var h = InternalHost.Fake();
        var baseNow = h.Clock.GetUtcNow();

        var fwd = await SetOffset(h, 1440);
        Assert.Equal(HttpStatusCode.OK, fwd.StatusCode);
        var j = await fwd.Body();
        Assert.Equal(1440, j.GetProperty("offsetMinutes").GetInt64());
        Assert.Equal(baseNow.AddDays(1), j.GetProperty("now").GetDateTimeOffset());

        var back = await (await SetOffset(h, -60)).Body();
        Assert.Equal(baseNow.AddHours(-1), back.GetProperty("now").GetDateTimeOffset());

        var zero = await (await SetOffset(h, 0)).Body();
        Assert.Equal(0, zero.GetProperty("offsetMinutes").GetInt64());
        Assert.Equal(baseNow, zero.GetProperty("now").GetDateTimeOffset());
    }

    [Fact]
    public async Task Every_reader_of_TimeProvider_sees_the_offset_time_and_a_replaced_clock_still_works_without_the_profile()
    {
        await using var h = InternalHost.Fake();
        await SetOffset(h, 2880);
        Assert.Equal(h.Clock.GetUtcNow().AddDays(2), h.Services.GetRequiredService<TimeProvider>().GetUtcNow());

        await using var plain = new InternalHost(); // a test host that replaces TimeProvider with a manual clock and no profile: no decorator
        plain.CreateClient();
        Assert.Same(plain.Clock, plain.Services.GetRequiredService<TimeProvider>());
    }

    [Theory]
    [InlineData(5_259_601)]
    [InlineData(-5_259_601)]
    [InlineData(long.MaxValue)]
    public async Task An_offset_beyond_ten_years_is_400_validation_failed_and_changes_nothing(long minutes)
    {
        await using var h = InternalHost.Fake();
        await SetOffset(h, 30);
        var r = await SetOffset(h, minutes);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("validation_failed", (await r.Body()).GetProperty("code").GetString());
        Assert.Equal(h.Clock.GetUtcNow().AddMinutes(30), h.Services.GetRequiredService<TimeProvider>().GetUtcNow());
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
        await using var h = InternalHost.Fake();
        var r = await h.PostRaw("/internal/v1/test/clock", body);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("validation_failed", (await r.Body()).GetProperty("code").GetString());
    }
}
