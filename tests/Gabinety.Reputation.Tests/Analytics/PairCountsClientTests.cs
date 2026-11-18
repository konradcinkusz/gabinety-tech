using System.Net;
using System.Net.Http.Json;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.Reputation.Features.Analytics;

namespace Gabinety.Reputation.Tests.Analytics;

public class PairCountsClientTests
{
    sealed class Stub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public Stub(Func<HttpRequestMessage, Task<HttpResponseMessage>> answer) : this((r, _) => answer(r)) { }
        public List<HttpRequestMessage> Seen { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Seen.Add(request); return answer(request, ct); }
    }

    static PairCountsClient Client(Stub s, string? key = "k-demo-pairs", TimeSpan? timeout = null) =>
        new(new HttpClient(s) { BaseAddress = new Uri("http://booking.demo.invalid/") }, key, timeout);

    [Fact]
    public async Task Sends_the_internal_key_and_the_query_and_reads_the_page()
    {
        var p = Guid.NewGuid();
        var s = new Stub(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new Page<PairCountDto>([new PairCountDto($"c:{p}", "c", p, 3)], "next-1")) }));
        var page = await Client(s).GetAsync(3, "a.b-c_d", 20, default);
        var req = Assert.Single(s.Seen);
        Assert.Equal("/internal/v1/analytics/pair-counts", req.RequestUri!.AbsolutePath);
        Assert.Equal("?minBookings=3&limit=20&cursor=a.b-c_d", req.RequestUri.Query);
        Assert.Equal("k-demo-pairs", req.Headers.GetValues("X-Internal-Key").Single());
        Assert.Equal("next-1", page.NextCursor);
        Assert.Equal(3, Assert.Single(page.Items).Bookings);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_non_success_answer_is_one_exception_without_the_key_or_url(HttpStatusCode code)
    {
        var ex = await Assert.ThrowsAsync<BookingUnavailableForAnalyticsException>(() =>
            Client(new Stub(_ => Task.FromResult(new HttpResponseMessage(code)))).GetAsync(3, null, 20, default));
        Assert.DoesNotContain("k-demo-pairs", ex.ToString());
        Assert.DoesNotContain("booking.demo.invalid", ex.Message);
    }

    [Fact]
    public async Task A_connection_failure_a_timeout_and_a_garbage_body_are_the_same_exception()
    {
        await Assert.ThrowsAsync<BookingUnavailableForAnalyticsException>(() => Client(new Stub(_ => throw new HttpRequestException("boom"))).GetAsync(3, null, 20, default));
        await Assert.ThrowsAsync<BookingUnavailableForAnalyticsException>(() =>
            Client(new Stub(async (r, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); }),
                timeout: TimeSpan.FromMilliseconds(50)).GetAsync(3, null, 20, default));
        await Assert.ThrowsAsync<BookingUnavailableForAnalyticsException>(() =>
            Client(new Stub(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json") }))).GetAsync(3, null, 20, default));
    }
}
