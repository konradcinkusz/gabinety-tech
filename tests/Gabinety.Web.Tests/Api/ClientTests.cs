using System.Net;
using Gabinety.Web.Api;
using Gabinety.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Web.Tests.Backends;

/// <summary>Each client method against a canned handler (BLAZOR-WEB-APP section 13): the URL it builds, what it reads, how it fails. The handler chain is the real one, built by the host.</summary>
public class ClientTests : IClassFixture<WebFactory>
{
    readonly WebFactory _web;
    public ClientTests(WebFactory web) => _web = web;

    T Client<T>(out IServiceScope scope) where T : notnull { scope = _web.Services.CreateScope(); return scope.ServiceProvider.GetRequiredService<T>(); }

    [Fact]
    public async Task Directory_reads_the_taxonomy_and_the_list_with_an_escaped_query()
    {
        _web.Directory.Respond = r => r.RequestUri!.AbsolutePath == "/api/v1/taxonomy" ? FakeBackend.Json(Sample.Taxonomy) : FakeBackend.Json(Sample.Cards(2, "NEXT"));
        var client = Client<DirectoryClient>(out var scope);
        using var _ = scope;

        var taxonomy = await client.TaxonomyAsync();
        Assert.True(taxonomy.IsOk);
        Assert.Equal("Audyt kodu", taxonomy.Value!.Problems[0].NamePl);

        var before = _web.Directory.Calls;
        var page = await client.PractitionersAsync(new PractitionerQuery("audyt-kodu", "dotnet", "a&b c", 30, 15000, "CUR+/="));
        Assert.True(page.IsOk);
        Assert.Equal(2, page.Value!.Items.Count);
        Assert.Equal("NEXT", page.Value.NextCursor);
        Assert.Equal("/api/v1/practitioners?problem=audyt-kodu&tech=dotnet&q=a%26b%20c&duration=30&maxPriceMinor=15000&cursor=CUR%2B%2F%3D", _web.Directory.Requests[before].PathAndQuery);
    }

    [Fact]
    public async Task A_failure_is_state_with_a_status_and_a_code_and_never_the_servers_text()
    {
        var client = Client<DirectoryClient>(out var scope);
        using var _ = scope;

        _web.Directory.Respond = _ => FakeBackend.Problem(HttpStatusCode.UnprocessableEntity, "invalid_input");
        var refused = await client.PractitionersAsync(new PractitionerQuery(Cursor: "forged"));
        Assert.Equal(new ApiFailure(422, "invalid_input"), refused.Failure);

        _web.Directory.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("<html>not json</html>") };
        Assert.Equal(new ApiFailure(404, "http_404"), (await client.TaxonomyAsync()).Failure);

        _web.Directory.Respond = _ => FakeBackend.Raw("{\"items\": 12}"); // a body that is not the contract
        Assert.Equal(new ApiFailure(502, ApiFailure.InvalidResponse), (await client.PractitionersAsync(new PractitionerQuery())).Failure);

        _web.Directory.Respond = FakeBackend.Throw(new HttpRequestException("connect ECONNREFUSED 10.0.0.1:5101")); // the cause is dropped
        Assert.Equal(ApiFailure.NoAnswer, (await client.TaxonomyAsync()).Failure);

        _web.Directory.Respond = _ => FakeBackend.Problem(HttpStatusCode.BadRequest, "code with spaces and <script>");
        Assert.Equal(new ApiFailure(400, "http_400"), (await client.TaxonomyAsync()).Failure); // a code that is not a code is not used
    }

    [Fact]
    public async Task Cancellation_is_not_a_backend_failure()
    {
        var client = Client<DirectoryClient>(out var scope);
        using var _ = scope;
        _web.Directory.Respond = FakeBackend.Throw(new OperationCanceledException());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.TaxonomyAsync(cts.Token));
    }

    [Fact]
    public async Task Reputation_and_Assistant_read_their_public_endpoints()
    {
        var id = Guid.NewGuid();
        _web.Reputation.Respond = _ => FakeBackend.Json(new PageDto<ReviewDto>([new ReviewDto(Guid.NewGuid(), 5, "Opinia demo", DateTimeOffset.UtcNow, null)], null));
        _web.Assistant.Respond = _ => FakeBackend.Json(new AssistantStatusDto("Fake", ["brief", "review-triage"]));
        var rep = Client<ReputationClient>(out var s1); using var _1 = s1;
        var asst = Client<AssistantClient>(out var s2); using var _2 = s2;

        Assert.Equal(5, (await rep.ReviewsAsync(id, "c 1")).Value!.Items[0].Rating);
        Assert.Equal($"/api/v1/practitioners/{id}/reviews?cursor=c%201", _web.Reputation.Requests[^1].PathAndQuery);
        var status = await asst.StatusAsync();
        Assert.Equal("Fake", status.Value!.State);
        Assert.Equal("/api/v1/assistant/status", _web.Assistant.Requests[^1].PathAndQuery);
    }

    [Fact]
    public async Task The_visitor_address_and_the_proxy_key_go_to_the_service_so_the_per_visitor_limits_key_on_the_visitor()
    {
        // The Next.js BFF does the same (web/src/lib/api/proxy.ts, PR #110): X-Client-Address + X-Edge-Key, both set by the host, never copied from the request.
        using var scope = _web.Services.CreateScope();
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.5, 198.51.100.77";
        ctx.Request.Headers["X-Client-Address"] = "6.6.6.6"; // a spoof from the visitor: ignored
        ctx.Request.Headers["X-Edge-Key"] = "guess";
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = ctx;
        _web.Directory.Respond = _ => FakeBackend.Json(Sample.Taxonomy);

        await scope.ServiceProvider.GetRequiredService<DirectoryClient>().TaxonomyAsync();

        var seen = _web.Directory.Requests[^1];
        Assert.Equal("198.51.100.77", seen.ClientAddress);
        Assert.Equal(WebFactory.TrustedKey, seen.EdgeKey);
        Assert.Null(seen.Authorization); // a public read carries no token
    }

    [Fact]
    public async Task Without_a_configured_proxy_key_no_address_leaves_the_host()
    {
        await using var web = new WebFactory().With("RateLimit:TrustedProxyKey", null);
        using var scope = web.Services.CreateScope();
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Forwarded-For"] = "198.51.100.77";
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = ctx;
        web.Directory.Respond = _ => FakeBackend.Json(Sample.Taxonomy);
        await scope.ServiceProvider.GetRequiredService<DirectoryClient>().TaxonomyAsync();
        Assert.Null(web.Directory.Requests[^1].ClientAddress);
        Assert.Null(web.Directory.Requests[^1].EdgeKey);
    }

    [Fact]
    public async Task The_key_is_never_sent_without_an_address()
    {
        using var scope = _web.Services.CreateScope(); // no HttpContext: a circuit that captured nothing
        _web.Directory.Respond = _ => FakeBackend.Json(Sample.Taxonomy);
        await scope.ServiceProvider.GetRequiredService<DirectoryClient>().TaxonomyAsync();
        Assert.Null(_web.Directory.Requests[^1].EdgeKey);
    }

    [Fact]
    public async Task The_handler_removes_a_forged_address_and_sets_the_bearer_only_from_the_request_options()
    {
        var inner = new FakeBackend { Respond = _ => FakeBackend.Json(new { }) };
        var handler = new BackendHeadersHandler(new WebOptions { TrustedProxyKey = "k" }) { InnerHandler = inner };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://x.test") };

        using var forged = new HttpRequestMessage(HttpMethod.Get, "/a");
        forged.Headers.TryAddWithoutValidation("X-Client-Address", "6.6.6.6");
        forged.Headers.TryAddWithoutValidation("X-Edge-Key", "k");
        forged.Headers.TryAddWithoutValidation("Authorization", "Bearer stolen");
        await http.SendAsync(forged);
        Assert.Null(inner.Requests[0].ClientAddress);
        Assert.Null(inner.Requests[0].EdgeKey);

        using var proper = new HttpRequestMessage(HttpMethod.Get, "/b");
        proper.Options.Set(RequestContext.AccessToken, "tok");
        proper.Options.Set(RequestContext.VisitorAddress, "2001:db8::1");
        await http.SendAsync(proper);
        Assert.Equal(("Bearer tok", "2001:db8::1", "k"), (inner.Requests[1].Authorization, inner.Requests[1].ClientAddress, inner.Requests[1].EdgeKey));

        using var bad = new HttpRequestMessage(HttpMethod.Get, "/c");
        bad.Options.Set(RequestContext.VisitorAddress, "1.2.3.4, 5.6.7.8");
        await http.SendAsync(bad);
        Assert.Null(inner.Requests[2].ClientAddress);
    }

    // ---- the resilience wiring ----

    static int ResilienceHandlers(HttpMessageHandler handler)
    {
        var n = 0;
        for (var h = handler; h is not null; h = (h as DelegatingHandler)?.InnerHandler)
            if (h.GetType().Name.Contains("Resilience", StringComparison.Ordinal)) n++;
        return n;
    }

    [Theory]
    [InlineData(Backend.Directory)] [InlineData(Backend.Booking)] [InlineData(Backend.Reputation)] [InlineData(Backend.Assistant)] [InlineData(Backend.Auth)]
    public void Every_backend_client_has_exactly_one_resilience_handler(string name)
    {
        // ServiceDefaults adds the standard handler to every client; a second AddStandardResilienceHandler multiplies the attempts (BLAZOR-WEB-APP section 5).
        var chain = _web.Services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(name);
        Assert.Equal(1, ResilienceHandlers(chain));
    }

    [Fact]
    public async Task A_read_that_fails_is_tried_once_more_and_a_write_never()
    {
        await using var web = new WebFactory();
        web.Directory.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        web.Identity.Backend.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        using var scope = web.Services.CreateScope();

        var read = await scope.ServiceProvider.GetRequiredService<DirectoryClient>().TaxonomyAsync();
        Assert.Equal(503, read.Failure!.Status);
        Assert.Equal(2, web.Directory.Calls); // SPEC 5.1: once, GET only: one attempt and one retry, not 4 x 4

        var login = await scope.ServiceProvider.GetRequiredService<Gabinety.Web.Auth.IdentityClient>().LoginAsync("a@demo.example", "x");
        Assert.Equal(Gabinety.Web.Auth.IdentityOutcome.Unavailable, login.Outcome);
        Assert.Equal(1, web.Identity.Backend.Calls); // login is never retried (SPEC 5.1)
    }

    [Fact]
    public async Task A_429_is_retried_once_after_the_wait_the_service_asked_for_at_most_two_seconds()
    {
        await using var web = new WebFactory();
        var first = true;
        web.Directory.Respond = _ =>
        {
            if (!first) return FakeBackend.Json(Sample.Taxonomy);
            first = false;
            var r = new HttpResponseMessage((HttpStatusCode)429);
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return r;
        };
        using var scope = web.Services.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<DirectoryClient>().TaxonomyAsync()).IsOk);
        Assert.Equal(2, web.Directory.Calls);
    }

    [Fact]
    public async Task A_backend_without_an_address_fails_as_state_not_as_a_crash()
    {
        await using var web = new WebFactory().With("Backend:Directory", "");
        // Nothing resolves "directory": the request would go to a host of that name, which does not exist. The fake stands in for the connection that fails.
        web.Directory.Respond = r => r.RequestUri!.Host == "directory" ? throw new HttpRequestException("no such host") : FakeBackend.Json(Sample.Taxonomy);
        using var scope = web.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<DirectoryClient>().TaxonomyAsync();
        Assert.Equal(ApiFailure.NoAnswer, result.Failure);
    }
}
