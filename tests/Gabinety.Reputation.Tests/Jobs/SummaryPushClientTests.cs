using System.Net;
using System.Text.Json;
using Gabinety.Reputation.Jobs.SummaryPush;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gabinety.Reputation.Tests;

/// <summary>Counting handler: <see cref="Answer"/> maps the call number (from 1) to a response; nothing sleeps.</summary>
sealed class CountingHandler : HttpMessageHandler
{
    public readonly List<HttpRequestMessage> Requests = new();
    public readonly List<string> Bodies = new();
    public Func<int, HttpResponseMessage> Answer = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
    {
        Requests.Add(r);
        Bodies.Add(r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct));
        return Answer(Requests.Count);
    }
}

public class SummaryPushClientTests
{
    internal const string Key = "k-directory-DO-NOT-LEAK-1234";
    internal const string BaseUrl = "http://directory.demo.invalid";
    static readonly Guid Id = Guid.NewGuid();
    static readonly ReputationPush Body = new(3, 4.33m, 7);
    readonly CountingHandler handler = new();
    readonly List<TimeSpan> delays = new();
    readonly LogSink sink = new();

    DirectoryReputationClient Client(string? key = Key)
    {
        var log = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(sink)).CreateLogger<DirectoryReputationClient>();
        return new DirectoryReputationClient(new HttpClient(handler) { BaseAddress = new Uri(BaseUrl + "/") }, key, log, delay: (d, _) => { delays.Add(d); return Task.CompletedTask; });
    }

    [Fact]
    public async Task Sends_a_put_with_the_internal_key_and_the_contract_body()
    {
        Assert.Equal(PushResult.Accepted, await Client().PushAsync(Id, Body, default));
        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, req.Method);
        Assert.Equal($"/internal/v1/practitioners/{Id}/reputation", req.RequestUri!.AbsolutePath);
        Assert.Equal(Key, req.Headers.GetValues("X-Internal-Key").Single());
        var json = JsonDocument.Parse(handler.Bodies[0]).RootElement;
        Assert.Equal(3, json.GetProperty("ratingCount").GetInt32());
        Assert.Equal(4.33m, json.GetProperty("ratingAvg").GetDecimal());
        Assert.Equal(7, json.GetProperty("version").GetInt64());
        Assert.Empty(delays);
    }

    [Fact]
    public async Task A_failing_directory_is_tried_three_times_with_bounded_backoff_then_Failed_without_throwing()
    {
        handler.Answer = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        Assert.Equal(PushResult.Failed, await Client().PushAsync(Id, Body, default));
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(2, delays.Count);
        Assert.All(delays, d => Assert.InRange(d, TimeSpan.Zero, TimeSpan.FromSeconds(5)));
        Assert.True(delays[1] >= delays[0]);
    }

    [Fact]
    public async Task A_connection_failure_is_retried_and_neither_the_key_nor_the_url_reaches_the_logs()
    {
        var calls = 0;
        handler.Answer = _ => { calls++; throw new HttpRequestException($"connection refused to {BaseUrl} with {Key}"); };
        Assert.Equal(PushResult.Failed, await Client().PushAsync(Id, Body, default));
        Assert.Equal(3, calls);
        Assert.NotEmpty(sink.Lines);
        Assert.DoesNotContain(sink.Lines, l => l.Contains(Key) || l.Contains(BaseUrl) || l.Contains("directory.demo.invalid"));
    }

    [Fact]
    public async Task Stops_at_the_first_success()
    {
        handler.Answer = n => new HttpResponseMessage(n == 1 ? HttpStatusCode.BadGateway : HttpStatusCode.NoContent);
        Assert.Equal(PushResult.Accepted, await Client().PushAsync(Id, Body, default));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Single(delays);
    }

    [Fact]
    public async Task An_unknown_practitioner_is_NotFound_after_a_single_call()
    {
        handler.Answer = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        Assert.Equal(PushResult.NotFound, await Client().PushAsync(Id, Body, default));
        Assert.Single(handler.Requests);
        Assert.Empty(delays);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Other_client_errors_are_Failed_and_not_retried(HttpStatusCode code)
    {
        handler.Answer = _ => new HttpResponseMessage(code);
        Assert.Equal(PushResult.Failed, await Client().PushAsync(Id, Body, default));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_timeout_counts_as_a_failed_attempt()
    {
        var c = new DirectoryReputationClient(new HttpClient(new HangingHandler()) { BaseAddress = new Uri(BaseUrl + "/") }, Key,
            NullLogger<DirectoryReputationClient>.Instance, timeout: TimeSpan.FromMilliseconds(30), delay: (_, _) => Task.CompletedTask);
        Assert.Equal(PushResult.Failed, await c.PushAsync(Id, Body, default));
    }

    [Fact]
    public async Task A_client_without_a_base_address_fails_quietly_instead_of_throwing()
    {
        var c = new DirectoryReputationClient(new HttpClient(handler), Key, NullLogger<DirectoryReputationClient>.Instance, delay: (_, _) => Task.CompletedTask);
        Assert.Equal(PushResult.Failed, await c.PushAsync(Id, Body, default));
    }

    [Fact]
    public async Task Cancellation_is_the_only_thing_that_escapes()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client().PushAsync(Id, Body, cts.Token));
    }

    sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); }
    }
}
