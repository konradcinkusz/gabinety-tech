using System.Net;
using System.Text.Json;
using Gabinety.Booking.Jobs.StatsPush;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gabinety.Booking.Tests;

public class StatsPushClientTests
{
    static readonly Guid Id = Guid.NewGuid();
    static readonly BookingStatsPush Body = new(3, 1, 2, 40, 1_700_000_000_000);
    readonly CountingHandler handler = new();
    readonly List<TimeSpan> delays = new();
    readonly LogSink sink = new();

    DirectoryStatsClient Client(string? key = FakeDirectory.Key)
    {
        var log = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(sink)).CreateLogger<DirectoryStatsClient>();
        return new DirectoryStatsClient(new HttpClient(handler) { BaseAddress = new Uri(FakeDirectory.BaseUrl + "/") }, key, log,
            delay: (d, _) => { delays.Add(d); return Task.CompletedTask; });
    }

    [Fact]
    public async Task Sends_a_put_with_the_internal_key_and_the_contract_body()
    {
        Assert.True(await Client().PushAsync(Id, Body, default));
        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, req.Method);
        Assert.Equal($"/internal/v1/practitioners/{Id}/booking-stats", req.RequestUri!.AbsolutePath);
        Assert.Equal(FakeDirectory.Key, req.Headers.GetValues("X-Internal-Key").Single());
        var json = JsonDocument.Parse(handler.Bodies[0]).RootElement;
        Assert.Equal(3, json.GetProperty("completedCount").GetInt32());
        Assert.Equal(1, json.GetProperty("practitionerCancelCount").GetInt32());
        Assert.Equal(2, json.GetProperty("noShowUpheldCount").GetInt32());
        Assert.Equal(40, json.GetProperty("openSlots14d").GetInt32());
        Assert.Equal(1_700_000_000_000, json.GetProperty("version").GetInt64());
        Assert.Empty(delays);
    }

    [Fact]
    public async Task A_failing_directory_is_tried_three_times_with_bounded_backoff_then_false_without_throwing()
    {
        handler.Answer = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        Assert.False(await Client().PushAsync(Id, Body, default));
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(2, delays.Count);
        Assert.All(delays, d => Assert.InRange(d, TimeSpan.Zero, TimeSpan.FromSeconds(5)));
        Assert.True(delays[1] >= delays[0]);
    }

    [Fact]
    public async Task A_connection_failure_is_retried_and_its_message_never_reaches_the_logs()
    {
        var calls = 0;
        handler.Answer = _ => { calls++; throw new HttpRequestException($"connection refused to {FakeDirectory.BaseUrl} with {FakeDirectory.Key}"); };
        Assert.False(await Client().PushAsync(Id, Body, default));
        Assert.Equal(3, calls);
        Assert.DoesNotContain(sink.Lines, l => l.Contains(FakeDirectory.Key) || l.Contains(FakeDirectory.BaseUrl) || l.Contains("directory.demo.invalid"));
    }

    [Fact]
    public async Task Stops_at_the_first_success()
    {
        handler.Answer = n => new HttpResponseMessage(n == 1 ? HttpStatusCode.BadGateway : HttpStatusCode.NoContent);
        Assert.True(await Client().PushAsync(Id, Body, default));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Single(delays);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_client_error_is_not_retried(HttpStatusCode code)
    {
        handler.Answer = _ => new HttpResponseMessage(code);
        Assert.False(await Client().PushAsync(Id, Body, default));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_timeout_counts_as_a_failed_attempt()
    {
        var c = new DirectoryStatsClient(new HttpClient(new HangingHandler()) { BaseAddress = new Uri(FakeDirectory.BaseUrl + "/") }, FakeDirectory.Key,
            NullLogger<DirectoryStatsClient>.Instance, timeout: TimeSpan.FromMilliseconds(30), delay: (_, _) => Task.CompletedTask);
        Assert.False(await c.PushAsync(Id, Body, default));
    }

    sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); }
    }
}
