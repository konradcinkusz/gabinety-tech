using System.Net;
using Gabinety.Booking.Features.Internal.TestClock;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Booking.Tests;

public class InternalKeyRotationTests
{
    const string Previous = "k-previous-DO-NOT-LEAK-9012";

    static Task<HttpResponseMessage> Get(CreateHost h, string? key)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/internal/v1/test/clock");
        if (key != null) req.Headers.Add("X-Internal-Key", key);
        return h.CreateClient().SendAsync(req);
    }

    static CreateHost Host(string? previous)
    {
        var h = new CreateHost();
        h.Settings["Internal:Key"] = ClockApiTests.InternalKey;
        h.Settings["Environment:Kind"] = "Development"; // maps the clock endpoint, the one authenticated GET this service has
        h.Configure = s => s.AddTestClock();
        if (previous != null) h.Settings["Internal:PreviousKey"] = previous;
        return h;
    }

    [Fact]
    public async Task Current_and_previous_keys_both_authenticate_and_anything_else_is_401()
    {
        await using var h = Host(Previous);
        Assert.Equal(HttpStatusCode.OK, (await Get(h, ClockApiTests.InternalKey)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get(h, Previous)).StatusCode);
        foreach (var k in new[] { "k-wrong", Previous + "x", Previous[..^1], "", null })
            Assert.Equal(HttpStatusCode.Unauthorized, (await Get(h, k)).StatusCode);
    }

    [Fact]
    public async Task With_no_previous_key_a_previous_value_is_401_and_an_empty_header_never_matches()
    {
        await using var h = Host(null);
        Assert.Equal(HttpStatusCode.OK, (await Get(h, ClockApiTests.InternalKey)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(h, Previous)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(h, "")).StatusCode);
        await using var blank = Host("");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(blank, "")).StatusCode);
    }
}
