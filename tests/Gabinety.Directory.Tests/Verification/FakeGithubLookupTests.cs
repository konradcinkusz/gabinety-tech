using System.Net;
using System.Net.Http.Json;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Verification;
using Gabinety.Directory.Tests.Internal;
using Gabinety.Directory.Tests.Offerings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Gabinety.Directory.Tests.Verification.Vk;

namespace Gabinety.Directory.Tests.Verification;

public class FakeGithubLookupTests
{
    const string Link = "/api/v1/me/practitioner/github-link", Verify = Link + "/verify";

    static string Handle(string prefix) => prefix + Guid.NewGuid().ToString("n")[..12];

    static async Task<(HttpClient C, string Sub, GithubLinkChallenge Ch)> Challenge(DemoResetHost h, string login)
    {
        var sub = Kit.Id();
        var c = await Kit.Practitioner(h, sub);
        var r = await c.PostAsJsonAsync(Link, new GithubLinkRequest(login));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (c, sub, (await r.Content.ReadFromJsonAsync<GithubLinkChallenge>(J))!);
    }

    [Theory]
    [InlineData("fake")]
    [InlineData("development")]
    public async Task A_demo_login_completes_the_github_verification_through_the_real_pipeline(string profile)
    {
        await using var h = profile == "fake" ? DemoResetHost.Fake() : DemoResetHost.Kind("Development");
        var login = Handle("demo-");
        var (c, sub, _) = await Challenge(h, login);

        var r = await c.PostAsync(Verify, null);
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);

        var p = await h.Db(db => db.Practitioners.AsNoTracking().SingleAsync(x => x.UserId == sub));
        Assert.True(p.GithubLinked);
        Assert.Equal(login, p.GithubLogin);
        Assert.Null(p.GithubChallengeCode);
        Assert.Equal(1, await h.Db(db => db.VerificationEvents.CountAsync(e => e.PractitionerId == p.Id && e.Kind == VerificationEventKind.GithubLinked)));
        Assert.Equal(0, h.NetworkAttempts); // the fake makes no network call
    }

    [Fact]
    public async Task A_non_demo_login_never_passes()
    {
        await using var h = DemoResetHost.Fake();
        var (c, sub, _) = await Challenge(h, Handle("real-"));
        var r = await c.PostAsync(Verify, null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("github_not_linked", (await Problem(r)).Code);
        Assert.False((await h.Db(db => db.Practitioners.AsNoTracking().SingleAsync(x => x.UserId == sub))).GithubLinked);
        Assert.Equal(0, h.NetworkAttempts);
    }

    [Fact]
    public async Task A_demo_login_does_not_pass_with_a_challenge_that_was_never_issued_for_it()
    {
        await using var h = DemoResetHost.Fake();
        using var scope = h.Services.CreateScope(); // the lookup uses the scoped DbContext: keep the scope alive
        var profile = await scope.ServiceProvider.GetRequiredService<IGithubProfileLookup>().GetBioAsync(Handle("demo-"), CancellationToken.None); // no pending challenge for this login
        Assert.True(profile.Exists);
        Assert.False(profile.Bio?.Contains("gabinety-verify-", StringComparison.Ordinal) ?? false);
    }

    [Fact]
    public async Task The_fake_profile_resolves_the_fake_lookup_and_a_production_host_the_real_one()
    {
        await using var fake = DemoResetHost.Fake();
        await using var dev = DemoResetHost.Kind("Development");
        await using var prod = DemoResetHost.Kind("Production");
        await using var bare = DemoResetHost.Bare();
        Assert.Equal(typeof(FakeGithubProfileLookup), (await fake.Scoped(sp => sp.GetRequiredService<IGithubProfileLookup>())).GetType());
        Assert.Equal(typeof(FakeGithubProfileLookup), (await dev.Scoped(sp => sp.GetRequiredService<IGithubProfileLookup>())).GetType());
        Assert.Equal(typeof(HttpGithubProfileLookup), (await prod.Scoped(sp => sp.GetRequiredService<IGithubProfileLookup>())).GetType());
        Assert.Equal(typeof(HttpGithubProfileLookup), (await bare.Scoped(sp => sp.GetRequiredService<IGithubProfileLookup>())).GetType());
    }

    [Fact]
    public async Task In_a_production_host_a_demo_login_goes_to_the_real_lookup_and_does_not_pass()
    {
        await using var h = DemoResetHost.Kind("Production");
        var (c, sub, _) = await Challenge(h, Handle("demo-"));
        var r = await c.PostAsync(Verify, null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode); // the real lookup tried the (refusing) network
        Assert.True(h.NetworkAttempts > 0);
        Assert.False((await h.Db(db => db.Practitioners.AsNoTracking().SingleAsync(x => x.UserId == sub))).GithubLinked);
    }

    [Fact]
    public void The_fake_lookup_has_no_http_dependency()
    {
        foreach (var ctor in typeof(FakeGithubProfileLookup).GetConstructors())
            Assert.DoesNotContain(ctor.GetParameters(), p => p.ParameterType == typeof(HttpClient) || p.ParameterType == typeof(IHttpClientFactory));
    }

    [Fact]
    public async Task No_log_line_carries_the_login_or_the_challenge_code()
    {
        await using var h = DemoResetHost.Fake();
        var login = Handle("demo-");
        var (c, _, ch) = await Challenge(h, login);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync(Verify, null)).StatusCode);
        var lines = h.Logs.Lines.ToArray();
        Assert.NotEmpty(lines); // the capture works
        Assert.DoesNotContain(lines, l => l.Contains(login, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, l => l.Contains(ch.Code, StringComparison.Ordinal));
    }
}
