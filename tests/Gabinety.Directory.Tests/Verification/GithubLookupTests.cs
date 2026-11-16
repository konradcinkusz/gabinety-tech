using System.Net;
using Gabinety.Directory.Features.Verification;

namespace Gabinety.Directory.Tests.Verification;

public class GithubLookupTests
{
    sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Seen { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    static HttpResponseMessage Json(HttpStatusCode s, string body = "{}") => new(s) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    static (HttpGithubProfileLookup L, Stub H) Make(Func<HttpRequestMessage, HttpResponseMessage> r) { var h = new Stub(r); return (new(new HttpClient(h)), h); }

    [Fact]
    public async Task Ok_with_a_bio_is_Found_and_the_request_is_exact()
    {
        var (l, h) = Make(_ => Json(HttpStatusCode.OK, """{"login":"demo-dev","bio":"hello gabinety-verify-abc"}"""));
        var r = await l.GetBioAsync("demo-dev", CancellationToken.None);
        Assert.True(r.Exists); Assert.Equal("hello gabinety-verify-abc", r.Bio);
        var req = Assert.Single(h.Seen);
        Assert.Equal("https://api.github.com/users/demo-dev", req.RequestUri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.Null(req.Headers.Authorization);
        Assert.False(req.Headers.Contains("Authorization"));
        Assert.Equal("gabinety-tech", req.Headers.UserAgent.ToString());
        Assert.Contains("application/vnd.github+json", req.Headers.Accept.ToString());
    }

    [Fact]
    public async Task Ok_with_a_null_bio_is_Found_without_a_bio()
    {
        var (l, _) = Make(_ => Json(HttpStatusCode.OK, """{"login":"demo-dev","bio":null}"""));
        var r = await l.GetBioAsync("demo-dev", CancellationToken.None);
        Assert.True(r.Exists); Assert.Null(r.Bio);
    }

    [Fact]
    public async Task Not_found_is_NotFound()
    {
        var (l, _) = Make(_ => Json(HttpStatusCode.NotFound));
        Assert.False((await l.GetBioAsync("demo-dev", CancellationToken.None)).Exists);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Server_error_and_rate_limit_throw_unavailable(HttpStatusCode s)
    {
        var (l, _) = Make(_ => Json(s));
        await Assert.ThrowsAsync<GithubUnavailableException>(() => l.GetBioAsync("demo-dev", CancellationToken.None));
    }

    [Fact]
    public async Task Timeout_network_error_and_garbage_body_throw_unavailable()
    {
        var timeout = new HttpClient(new Stub(_ => throw new TaskCanceledException("timeout")));
        await Assert.ThrowsAsync<GithubUnavailableException>(() => new HttpGithubProfileLookup(timeout).GetBioAsync("demo-dev", CancellationToken.None));
        var net = new HttpClient(new Stub(_ => throw new HttpRequestException("down")));
        await Assert.ThrowsAsync<GithubUnavailableException>(() => new HttpGithubProfileLookup(net).GetBioAsync("demo-dev", CancellationToken.None));
        var (l, _) = Make(_ => Json(HttpStatusCode.OK, "<html>"));
        await Assert.ThrowsAsync<GithubUnavailableException>(() => l.GetBioAsync("demo-dev", CancellationToken.None));
    }

    [Theory]
    [InlineData("../orgs")]
    [InlineData("a/b")]
    [InlineData("a?b")]
    [InlineData("")]
    public async Task A_handle_that_is_not_valid_never_reaches_the_URL(string login)
    {
        var (l, h) = Make(_ => Json(HttpStatusCode.OK));
        await Assert.ThrowsAsync<ArgumentException>(() => l.GetBioAsync(login, CancellationToken.None));
        Assert.Empty(h.Seen);
    }
}
