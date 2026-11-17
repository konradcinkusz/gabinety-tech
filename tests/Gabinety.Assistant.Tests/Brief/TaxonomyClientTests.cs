using System.Net;
using System.Text.Json;
using Gabinety.Assistant.Clients;
using Gabinety.Assistant.Tests.Usage;

namespace Gabinety.Assistant.Tests.Brief;

public class TaxonomyClientTests
{
    sealed class Handler(Func<HttpResponseMessage> next) : HttpMessageHandler
    {
        public int Calls;
        public Func<HttpResponseMessage> Next = next;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        { Calls++; Assert.Equal("/api/v1/taxonomy", r.RequestUri!.AbsolutePath); return Task.FromResult(Next()); }
    }

    static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = JsonContent.Create(StubTaxonomy.Full, options: new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
    static HttpResponseMessage Down() => new(HttpStatusCode.ServiceUnavailable);

    static (DirectoryTaxonomyClient, Handler, FakeClock) Make(Func<HttpResponseMessage> next)
    {
        var h = new Handler(next);
        var clock = new FakeClock(new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero));
        return (new DirectoryTaxonomyClient(new HttpClient(h) { BaseAddress = new("http://directory.demo.invalid/") }, clock), h, clock);
    }

    [Fact]
    public async Task Cached_for_10_minutes_then_fetched_again()
    {
        var (c, h, clock) = Make(Ok);
        Assert.Equal(12, (await c.GetAsync(default)).Techs.Count);
        clock.Now += TimeSpan.FromMinutes(9);
        await c.GetAsync(default);
        Assert.Equal(1, h.Calls);
        clock.Now += TimeSpan.FromMinutes(2);
        await c.GetAsync(default);
        Assert.Equal(2, h.Calls);
    }

    [Fact]
    public async Task When_Directory_is_down_the_stale_value_serves_for_24_hours_then_it_is_unavailable()
    {
        var (c, h, clock) = Make(Ok);
        await c.GetAsync(default);
        h.Next = Down;
        clock.Now += TimeSpan.FromHours(23);
        Assert.Equal(8, (await c.GetAsync(default)).Problems.Count);
        clock.Now += TimeSpan.FromHours(2);
        await Assert.ThrowsAsync<TaxonomyUnavailableException>(() => c.GetAsync(default));
    }

    [Fact]
    public async Task Nothing_cached_and_Directory_down_or_unseeded_or_not_configured_is_unavailable()
    {
        await Assert.ThrowsAsync<TaxonomyUnavailableException>(() => Make(Down).Item1.GetAsync(default));
        await Assert.ThrowsAsync<TaxonomyUnavailableException>(() => Make(() => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { version = "", problems = new object[0], techs = new object[0] }) }).Item1.GetAsync(default));
        await Assert.ThrowsAsync<TaxonomyUnavailableException>(() => new DirectoryTaxonomyClient(new HttpClient(), new FakeClock(DateTimeOffset.UnixEpoch)).GetAsync(default));
    }
}
