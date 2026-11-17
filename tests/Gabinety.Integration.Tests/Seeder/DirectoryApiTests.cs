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
