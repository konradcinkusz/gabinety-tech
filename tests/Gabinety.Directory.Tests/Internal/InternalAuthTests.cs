using System.Net;
using System.Net.Http.Json;
using Gabinety.Contracts.Directory;

namespace Gabinety.Directory.Tests.Internal;

public class InternalAuthTests(InternalFactory f) : IClassFixture<InternalFactory>
{
    public static TheoryData<string, string> Endpoints() => new()
    {
        { "GET", "/internal/v1/offerings/" },
        { "GET", "/internal/v1/practitioners/by-user/" },
        { "PUT", "/internal/v1/practitioners/{id}/reputation" },
        { "PUT", "/internal/v1/practitioners/{id}/booking-stats" },
        { "DELETE", "/internal/v1/subjects/" },
    };

    static HttpRequestMessage Req(string method, string path, string? key)
    {
        var url = path.Replace("{id}", Guid.NewGuid().ToString());
        if (url.EndsWith('/')) url += method == "GET" && url.Contains("offerings") ? Guid.NewGuid().ToString() : "demo-" + Guid.NewGuid().ToString("n");
        var r = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "PUT") r.Content = JsonContent.Create(new { ratingCount = 1, ratingAvg = 5.0, completedCount = 1, practitionerCancelCount = 0, noShowUpheldCount = 0, openSlots14d = 1, version = 1 });
        if (key is not null) r.Headers.Add("X-Internal-Key", key);
        return r;
    }

    [Theory, MemberData(nameof(Endpoints))]
    public async Task Missing_key_is_401(string method, string path) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Internal(null).SendAsync(Req(method, path, null))).StatusCode);

    [Theory, MemberData(nameof(Endpoints))]
    public async Task Wrong_key_is_401(string method, string path) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Internal("demo-wrong-key").SendAsync(Req(method, path, "demo-wrong-key"))).StatusCode);

    [Fact]
    public async Task A_user_bearer_token_is_not_an_internal_credential()
    {
        var r = await f.As("demo-user-1").GetAsync($"/internal/v1/practitioners/by-user/demo-x");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task The_right_key_reaches_the_endpoint()
    {
        var r = await f.Internal().GetAsync($"/internal/v1/practitioners/by-user/demo-{Guid.NewGuid():n}");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}
