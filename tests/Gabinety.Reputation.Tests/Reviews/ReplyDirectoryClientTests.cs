using System.Net;
using System.Net.Http.Json;
using Gabinety.Reputation.Clients;

namespace Gabinety.Reputation.Tests.Reviews;

/// <summary>The Reputation -> Directory by-user client (SPEC 5.1: 3 s, once).</summary>
public class ReplyDirectoryClientTests
{
    static (DirectoryClient c, FakeDirectory h) Make()
    {
        var h = new FakeDirectory();
        return (new DirectoryClient(new HttpClient(h) { BaseAddress = new Uri(FakeDirectory.BaseUrl + "/") }, FakeDirectory.Key, retryDelay: TimeSpan.Zero), h);
    }

    [Fact]
    public async Task A_known_user_resolves_and_the_key_is_sent()
    {
        var (c, h) = Make();
        var id = h.Add("demo-user-a");

        var found = await c.GetPractitionerByUserAsync("demo-user-a", default);

        Assert.Equal(id, found!.PractitionerId);
        Assert.Equal(FakeDirectory.Key, h.LastKeyHeader);
        Assert.Equal("/internal/v1/practitioners/by-user/demo-user-a", h.LastPath);
    }

    [Fact]
    public async Task A_404_is_null_and_not_retried()
    {
        var (c, h) = Make();
        Assert.Null(await c.GetPractitionerByUserAsync("demo-nobody", default));
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task A_5xx_is_retried_once_and_a_second_one_is_unavailable()
    {
        var (c, h) = Make();
        h.Override = (_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway);
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => c.GetPractitionerByUserAsync("demo-user-a", default));
        Assert.Equal(2, h.Calls);
    }

    [Fact]
    public async Task A_5xx_followed_by_success_succeeds()
    {
        var (c, h) = Make();
        var id = Guid.NewGuid();
        h.Override = (_, n) => n == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { practitionerId = id, status = "Approved" }) };
        Assert.Equal(id, (await c.GetPractitionerByUserAsync("demo-user-a", default))!.PractitionerId);
    }

    [Fact]
    public async Task A_401_is_a_configuration_fault_and_is_not_retried()
    {
        var (c, h) = Make();
        h.Override = (_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => c.GetPractitionerByUserAsync("demo-user-a", default));
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task A_connection_failure_is_unavailable_and_its_message_leaks_no_url_or_key()
    {
        var (c, h) = Make();
        h.Down = true;
        var e = await Assert.ThrowsAsync<DirectoryUnavailableException>(() => c.GetPractitionerByUserAsync("demo-user-a", default));
        Assert.DoesNotContain(FakeDirectory.Key, e.Message);
        Assert.DoesNotContain("directory.demo.invalid", e.Message);
    }

    [Fact]
    public async Task A_slow_answer_is_cut_at_the_timeout_and_counts_as_unavailable()
    {
        var slow = new SlowHandler();
        var c = new DirectoryClient(new HttpClient(slow) { BaseAddress = new Uri(FakeDirectory.BaseUrl + "/") }, FakeDirectory.Key, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => c.GetPractitionerByUserAsync("demo-user-a", default));
        Assert.Equal(2, slow.Calls);
    }

    sealed class SlowHandler : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
