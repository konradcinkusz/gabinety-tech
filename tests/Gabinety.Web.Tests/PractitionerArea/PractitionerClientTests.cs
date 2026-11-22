using System.Net;
using Gabinety.Web.Api;
using Gabinety.Web.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Web.Tests.PractitionerAreaTests;

/// <summary>The typed clients of the practitioner area, called with a signed-in scope: the bearer, the verb and path, the body, a 204, and what a failure keeps of the server's answer.</summary>
public class PractitionerClientTests
{
    sealed class Token(string? value) : IAccessTokenSource { public Task<string?> AccessTokenAsync(CancellationToken ct = default) => Task.FromResult(value); }

    /// <summary>The real host and handler chain (resilience included) with the token of a signed-in user replaced by a fixed one: a client is called outside a request, which the real token source refuses.</summary>
    static (WebFactory Web, IServiceScope Scope, PractitionerWorld World) Scoped(string? token = "tok")
    {
        var web = new WebFactory();
        var world = new PractitionerWorld();
        world.HaveProfile();
        world.Attach(web);
        var derived = web.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<IAccessTokenSource>(_ => new Token(token))));
        return (web, derived.Services.CreateScope(), world);
    }

    [Fact]
    public async Task Without_a_session_no_bearer_is_sent_and_the_service_says_401_as_a_failure_not_an_exception()
    {
        var (web, scope, _) = Scoped(token: null);
        await using var _w = web;
        using var _s = scope;
        web.Directory.Respond = _ => FakeBackend.Problem(HttpStatusCode.Unauthorized, "unauthorized");
        var r = await scope.ServiceProvider.GetRequiredService<PractitionerClient>().ProfileAsync();
        Assert.True(r.Failure!.IsUnauthenticated);
        Assert.Null(web.Directory.Requests.Single().Authorization);
    }

    [Fact]
    public async Task A_204_is_a_success_with_no_body_and_a_write_is_sent_once_with_its_json_body()
    {
        var (web, scope, world) = Scoped();
        await using var _w = web;
        using var _s = scope;
        var client = scope.ServiceProvider.GetRequiredService<PractitionerClient>();
        world.Offers.Add(new PractitionerWorld.Offer { Title = "Tytuł oferty", ProblemSlug = "code-review", Duration = 30, Price = 10000 });
        var id = world.Offers[0].Id;
        Assert.True((await client.DeactivateOfferingAsync(id)).IsOk);
        Assert.True((await client.ActivateOfferingAsync(id)).IsOk);
        Assert.Equal([$"/api/v1/me/practitioner/offerings/{id}/deactivate", $"/api/v1/me/practitioner/offerings/{id}/activate"], web.Directory.Requests.Where(r => r.Method == HttpMethod.Post).Select(r => r.PathAndQuery));
        Assert.All(web.Directory.Requests, r => Assert.StartsWith("Bearer ", r.Authorization));
    }

    [Fact]
    public async Task A_write_is_never_retried_even_when_the_service_answers_503()
    {
        var (web, scope, _) = Scoped();
        await using var _w = web;
        using var _s = scope;
        web.Directory.Respond = _ => FakeBackend.Problem(HttpStatusCode.ServiceUnavailable, "dependency_unavailable");
        var r = await scope.ServiceProvider.GetRequiredService<PractitionerClient>().SubmitAsync();
        Assert.Equal(503, r.Failure!.Status);
        Assert.Single(web.Directory.Requests, x => x.Method == HttpMethod.Post); // one attempt: SPEC 5 rule 8
    }

    [Fact]
    public async Task A_failure_keeps_the_status_the_code_and_only_the_names_of_the_fields_and_unmet_items()
    {
        var (web, scope, _) = Scoped();
        await using var _w = web;
        using var _s = scope;
        web.Directory.Respond = _ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("""{"code":"submit_incomplete","title":"SERVER-TITLE","detail":"SERVER-DETAIL","errors":{"headline":["SERVER-MSG"],"bad key <b>":["x"],"rules[2].endLocal":["y"]},"unmetItems":["bio","github_linked","<script>","portfolioUrl"]}""", System.Text.Encoding.UTF8, "application/problem+json"),
        };
        var f = (await scope.ServiceProvider.GetRequiredService<PractitionerClient>().SubmitAsync()).Failure!;
        Assert.Equal((422, "submit_incomplete"), (f.Status, f.Code));
        Assert.Equal(["headline", "rules[2].endLocal"], f.Fields); // the key with markup characters is dropped
        Assert.Equal(["bio", "github_linked", "portfolioUrl"], f.Unmet);
        Assert.DoesNotContain("SERVER", string.Join("|", (f.Fields ?? []).Concat(f.Unmet ?? []).Append(f.Code)));
    }

    [Theory]
    [InlineData("""{"code":"has space"}""")] [InlineData("""{"code":42}""")] [InlineData("""[1,2]""")] [InlineData("not json")] [InlineData("")]
    public async Task A_code_that_is_not_an_identifier_falls_back_to_the_http_status_and_a_body_that_is_not_an_object_is_ignored(string body)
    {
        var (web, scope, _) = Scoped();
        await using var _w = web;
        using var _s = scope;
        web.Directory.Respond = _ => new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/problem+json") };
        var f = (await scope.ServiceProvider.GetRequiredService<PractitionerClient>().CreateProfileAsync("Demo")).Failure!;
        Assert.Equal((409, "http_409"), (f.Status, f.Code));
        Assert.Empty(f.Fields ?? []);
    }

    [Fact]
    public async Task The_scheduling_client_reads_and_writes_availability_and_the_payments_account_with_the_users_bearer()
    {
        var (web, scope, world) = Scoped();
        await using var _w = web;
        using var _s = scope;
        var client = scope.ServiceProvider.GetRequiredService<PractitionerSchedulingClient>();
        var saved = await client.SaveAvailabilityAsync(new AvailabilityDto([new(1, "09:00", "10:00")], [new("2027-01-06")]));
        Assert.True(saved.IsOk);
        Assert.Equal([(1, "09:00", "10:00")], world.Rules);
        Assert.Equal("2027-01-06", (await client.AvailabilityAsync()).Value!.Exceptions[0].DateLocal);
        world.PaymentState = "pending";
        Assert.Equal("pending", (await client.PaymentAccountAsync()).Value!.State);
        Assert.Equal("https://connect.stripe.com/setup/e/acct_test/abc", (await client.StartPaymentOnboardingAsync()).Value!.OnboardingUrl);
        world.PaymentState = null;
        Assert.Equal(404, (await client.PaymentAccountAsync()).Failure!.Status);
        Assert.All(web.Booking.Requests, r => Assert.StartsWith("Bearer ", r.Authorization));
    }

    [Fact]
    public async Task A_backend_that_is_down_is_a_failure_with_no_answer_not_an_exception()
    {
        var (web, scope, world) = Scoped();
        await using var _w = web;
        using var _s = scope;
        world.DirectoryDown = true;
        var r = await scope.ServiceProvider.GetRequiredService<PractitionerClient>().OfferingsAsync();
        Assert.Equal(0, r.Failure!.Status);
    }
}
