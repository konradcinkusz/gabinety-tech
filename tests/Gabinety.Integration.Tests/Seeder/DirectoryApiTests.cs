using System.Net;
using System.Net.Http.Headers;
using Gabinety.DemoSeeder;

namespace Gabinety.Integration.Tests.Seeder;

public class DirectoryApiTests
{
    sealed class Scripted(params HttpResponseMessage[] replies) : HttpMessageHandler
    {
        readonly Queue<HttpResponseMessage> _replies = new(replies);
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return Task.FromResult(_replies.Dequeue()); }
    }

    static HttpResponseMessage Limited() => new(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero) } };

    static readonly SeedOptions Options = new("http://localhost", "demo-key", "http://localhost", "demo-password");

    [Fact]
    public async Task A_429_is_waited_out_and_retried()
    {
        var handler = new Scripted(Limited(), Limited(), new HttpResponseMessage(HttpStatusCode.NoContent));
        var api = new DirectoryApi(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }, Options, TimeProvider.System);
        await api.PushReputationAsync(Guid.NewGuid(), 1, 4.5m, 1, CancellationToken.None);
        Assert.Equal(3, handler.Calls);
    }

    /// <summary>A clock whose timers fire at once and remember how long they were asked to wait.</summary>
    sealed class InstantTime : TimeProvider
    {
        public TimeSpan Waited;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Waited += dueTime;
            return base.CreateTimer(callback, state, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
    }

    [Fact]
    public async Task A_429_without_Retry_After_is_outwaited_for_longer_than_the_services_one_minute_window()
    {
        // CONTRACTS 10.2: 60 per minute in six 10 s segments, and the service sends no Retry-After. A burst in one segment frees up only 60 to 70 s later.
        var replies = Enumerable.Range(0, 7).Select(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)).Append(new HttpResponseMessage(HttpStatusCode.NoContent)).ToArray();
        var handler = new Scripted(replies);
        var time = new InstantTime();
        var api = new DirectoryApi(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }, Options, time);

        await api.PushReputationAsync(Guid.NewGuid(), 1, 4.5m, 1, CancellationToken.None);

        Assert.Equal(8, handler.Calls);
        Assert.True(time.Waited >= TimeSpan.FromSeconds(70), $"waited only {time.Waited}");
    }

    [Fact]
    public async Task Booking_answering_503_because_the_Directory_was_rate_limited_is_waited_out_too()
    {
        // Booking asks the Directory from the same loopback address as the seeder, so a busy seeder can make Booking's own lookup a 429, which Booking reports as 503.
        var down = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("""{"code":"dependency_unavailable"}""", System.Text.Encoding.UTF8, "application/json") };
        var handler = new Scripted(down(), down(), new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"offsetMinutes":0,"now":"2026-11-17T10:00:00Z"}""", System.Text.Encoding.UTF8, "application/json") });
        var api = new BookingApi(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }, Options, new InstantTime());

        var (offset, _) = await api.GetClockAsync(CancellationToken.None);

        Assert.Equal(0, offset);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task An_error_names_the_status_and_code_but_never_the_key_or_the_body()
    {
        var body = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("""{"code":"forbidden","title":"secret demo-key detail"}""", System.Text.Encoding.UTF8, "application/problem+json") };
        var api = new DirectoryApi(new HttpClient(new Scripted(body)) { BaseAddress = new Uri("http://localhost/") }, Options, TimeProvider.System);
        var e = await Assert.ThrowsAsync<SeedException>(() => api.PushReputationAsync(Guid.NewGuid(), 1, 4.5m, 1, CancellationToken.None));
        Assert.Contains("403 forbidden", e.Message);
        Assert.DoesNotContain("demo-key", e.Message);
        Assert.DoesNotContain("secret", e.Message);
    }
}
