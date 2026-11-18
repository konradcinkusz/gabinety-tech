using System.Net;
using System.Text;

namespace Gabinety.Booking.Tests;

/// <summary>F-03 (T-10.10): Booking is the one public app. From the public edge only the webhook, the public reads and the client API are served.</summary>
public class PublicEdgeTests
{
    const string Key = "demo-AAAAbbbbCCCCddddEEEEffffGGGGhhhh_12345";
    static readonly (string, string) Edge = ("Fly-Client-IP", "203.0.113.9");

    static CreateHost NewHost()
    {
        var h = new CreateHost().AsDeployed(); // the edge guard is a deployed-profile rule; Mock cannot run there (F-01), so the host gets the provider double "Acme"
        h.Settings["Internal:Key"] = Key;
        return h;
    }

    static Task<HttpResponseMessage> Send(CreateHost h, HttpMethod m, string path, bool edge, string? key = null, string? body = null)
    {
        var req = new HttpRequestMessage(m, path);
        if (body != null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (key != null) req.Headers.Add("X-Internal-Key", key);
        if (edge) req.Headers.Add(Edge.Item1, Edge.Item2);
        return h.CreateClient().SendAsync(req);
    }

    [Theory]
    [InlineData("GET", "/internal/v1/bookings/00000000-0000-0000-0000-000000000001/reviewability")]
    [InlineData("DELETE", "/internal/v1/subjects/demo-user")]
    [InlineData("POST", "/internal/v1/test/clock")]
    [InlineData("POST", "/internal/v1/demo/reset")]
    public async Task Internal_routes_are_404_from_the_edge_even_with_the_valid_key(string method, string path)
    {
        await using var h = NewHost();
        Assert.Equal(HttpStatusCode.NotFound, (await Send(h, new HttpMethod(method), path, edge: true, key: Key, body: "{}")).StatusCode);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/details")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/api/v1/admin/disputes")]
    [InlineData("/api/v1/admin/ledger.csv")]
    [InlineData("/")]
    public async Task Routes_that_the_public_does_not_need_are_404_from_the_edge(string path)
    {
        await using var h = NewHost();
        Assert.Equal(HttpStatusCode.NotFound, (await Send(h, HttpMethod.Get, path, edge: true)).StatusCode);
    }

    [Fact]
    public async Task The_webhook_the_alive_probe_the_public_reads_and_the_client_api_are_served_from_the_edge()
    {
        await using var h = NewHost();
        // 404 from the guard would be the failure; the handlers answer anything else (401 for no token, 4xx for a bad body, ...).
        Assert.NotEqual(HttpStatusCode.NotFound, (await Send(h, HttpMethod.Post, "/webhooks/payments/acme", edge: true, body: "{}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(h, HttpMethod.Get, "/alive", edge: true)).StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, (await Send(h, HttpMethod.Get, $"/api/v1/practitioners/{Guid.NewGuid()}/slots", edge: true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(h, HttpMethod.Get, "/api/v1/me/bookings", edge: true)).StatusCode);
    }

    [Fact]
    public async Task Without_edge_headers_everything_still_works_for_the_private_network()
    {
        await using var h = NewHost();
        Assert.Equal(HttpStatusCode.OK, (await Send(h, HttpMethod.Get, "/health", edge: false)).StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, (await Send(h, HttpMethod.Get, "/api/v1/admin/disputes", edge: false)).StatusCode);
        // an unknown booking answers from the handler (not found by id, with the key), not "Unauthorized" and not a blanket guard
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await Send(h, HttpMethod.Get, $"/internal/v1/bookings/{Guid.NewGuid()}/reviewability", edge: false, key: Key)).StatusCode);
    }

    [Fact]
    public async Task The_web_app_over_the_private_network_with_X_Edge_Key_reaches_admin_but_never_internal()
    {
        await using var h = NewHost();
        Task<HttpResponseMessage> Web(string path, string? key = null)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.Add("X-Edge-Key", "demo-QQQQppppOOOOnnnnMMMMllllKKKKjjjj_abcde");
            if (key != null) req.Headers.Add("X-Internal-Key", key);
            return h.CreateClient().SendAsync(req);
        }
        Assert.NotEqual(HttpStatusCode.NotFound, (await Web("/api/v1/admin/disputes")).StatusCode); // 401: the guard did not filter it
        Assert.Equal(HttpStatusCode.NotFound, (await Web($"/internal/v1/bookings/{Guid.NewGuid()}/reviewability", Key)).StatusCode);
    }
}
