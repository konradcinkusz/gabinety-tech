using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Verification;
using static Gabinety.Directory.Tests.Verification.Vk;

namespace Gabinety.Directory.Tests.Verification;

public class GithubLinkTests(VerificationFactory f) : IClassFixture<VerificationFactory>
{
    const string Link = "/api/v1/me/practitioner/github-link", Verify = Link + "/verify";

    async Task<(HttpClient C, Guid Id)> Draft()
    {
        var p = await f.Seed(PractitionerStatus.Draft);
        return (f.As(p.UserId), p.Id);
    }

    static async Task<GithubLinkChallenge> Challenge(HttpClient c, string? login = null)
    {
        login ??= "demo-" + Guid.NewGuid().ToString("n")[..12]; // each test its own handle: a linked one is taken
        var r = await c.PostAsJsonAsync(Link, new GithubLinkRequest(login));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<GithubLinkChallenge>(J))!;
    }

    [Theory]
    [InlineData("")]
    [InlineData("-abc")]
    [InlineData("abc-")]
    [InlineData("has space")]
    [InlineData("zażółć")]
    [InlineData("a_b")]
    [InlineData("abc\n")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 40
    public async Task Bad_handle_is_422_validation_failed(string login)
    {
        var (c, _) = await Draft();
        var r = await c.PostAsJsonAsync(Link, new GithubLinkRequest(login));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var p = await Problem(r);
        Assert.Equal("validation_failed", p.Code);
        Assert.True(p.Errors!.ContainsKey("githubLogin"));
    }

    [Fact]
    public async Task Missing_handle_is_422()
    {
        var (c, _) = await Draft();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync(Link, new { })).StatusCode);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("Demo-Dev-1")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 39
    public async Task Valid_handle_gets_a_code_of_the_contract_shape(string login)
    {
        var (c, _) = await Draft();
        var ch = await Challenge(c, login);
        Assert.Matches(new Regex(@"^gabinety-verify-[a-z0-9]{10}\z"), ch.Code);
        Assert.False(string.IsNullOrWhiteSpace(ch.Instruction));
    }

    [Fact]
    public async Task User_without_a_practitioner_gets_404_on_both_endpoints()
    {
        var c = f.As("demo-nobody-" + Guid.NewGuid().ToString("n"));
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync(Link, new GithubLinkRequest("demo-dev"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync(Verify, null)).StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_is_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.As(null).PostAsync(Verify, null)).StatusCode);

    [Fact]
    public async Task A_second_request_gives_a_different_code_and_replaces_the_first()
    {
        var (c, _) = await Draft();
        var first = await Challenge(c);
        var second = await Challenge(c);
        Assert.NotEqual(first.Code, second.Code);

        f.Github.Exists = true; f.Github.Bio = first.Code;
        var old = await c.PostAsync(Verify, null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, old.StatusCode);
        f.Github.Bio = second.Code;
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync(Verify, null)).StatusCode);
    }

    [Fact]
    public async Task Verify_204_when_the_bio_contains_the_code_among_other_text_and_links_the_profile()
    {
        var (c, id) = await Draft();
        var ch = await Challenge(c, "Demo-Dev-" + Guid.NewGuid().ToString("n")[..8]);
        f.Github.Exists = true; f.Github.Bio = $"I review .NET code. {ch.Code} Warsaw";
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync(Verify, null)).StatusCode);

        var me = (await c.GetFromJsonAsync<PractitionerPrivate>("/api/v1/me/practitioner", J))!;
        Assert.True(me.GithubLinked);
        Assert.StartsWith("Demo-Dev-", me.GithubLogin);
        var row = await f.Load(id);
        Assert.Null(row.GithubChallengeCode); Assert.Null(row.GithubChallengeLogin); Assert.Null(row.GithubChallengeExpiresAt);
        var ev = Assert.Single(await f.Events(id));
        Assert.Equal(VerificationEventKind.GithubLinked, ev.Kind);
        Assert.Equal(row.UserId, ev.ActorUserId);

        // the challenge is spent
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsync(Verify, null)).StatusCode);
        Assert.Single(await f.Events(id));
    }

    [Fact]
    public async Task Verify_422_github_not_linked_when_the_bio_lacks_the_code_or_is_empty()
    {
        var (c, id) = await Draft();
        await Challenge(c);
        foreach (var bio in new string?[] { "no code here", null, "gabinety-verify-0000000000" })
        {
            f.Github.Exists = true; f.Github.Bio = bio;
            var r = await c.PostAsync(Verify, null);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            Assert.Equal("github_not_linked", (await Problem(r)).Code);
        }
        Assert.False((await f.Load(id)).GithubLinked);
        Assert.Empty(await f.Events(id));
    }

    [Fact]
    public async Task Verify_422_when_no_challenge_is_pending_and_GitHub_is_not_called()
    {
        var (c, _) = await Draft();
        var before = f.Github.Calls;
        var r = await c.PostAsync(Verify, null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("github_not_linked", (await Problem(r)).Code);
        Assert.Equal(before, f.Github.Calls);
    }

    [Fact]
    public async Task Verify_422_when_the_GitHub_profile_does_not_exist()
    {
        var (c, _) = await Draft();
        await Challenge(c);
        f.Github.Exists = false;
        try
        {
            var r = await c.PostAsync(Verify, null);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            Assert.Equal("github_not_linked", (await Problem(r)).Code);
        }
        finally { f.Github.Exists = true; }
    }

    [Fact]
    public async Task The_challenge_is_valid_at_exactly_30_minutes_and_expired_after_it()
    {
        var (c, _) = await Draft();
        var ch = await Challenge(c);
        f.Github.Exists = true; f.Github.Bio = ch.Code;
        f.Clock.Advance(TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(1));
        var r = await c.PostAsync(Verify, null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("github_not_linked", (await Problem(r)).Code);

        var (c2, _) = await Draft();
        var ch2 = await Challenge(c2);
        f.Github.Bio = ch2.Code;
        f.Clock.Advance(TimeSpan.FromMinutes(30)); // the boundary: still valid
        Assert.Equal(HttpStatusCode.NoContent, (await c2.PostAsync(Verify, null)).StatusCode);

        var (c3, _) = await Draft();
        var ch3 = await Challenge(c3);
        f.Github.Bio = ch3.Code;
        f.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c3.PostAsync(Verify, null)).StatusCode);
    }

    [Fact]
    public async Task Verify_503_dependency_unavailable_when_GitHub_does_not_answer_and_the_challenge_survives()
    {
        var (c, id) = await Draft();
        var ch = await Challenge(c);
        f.Github.Unavailable = true;
        try
        {
            var r = await c.PostAsync(Verify, null);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
            Assert.Equal("dependency_unavailable", (await Problem(r)).Code);
        }
        finally { f.Github.Unavailable = false; }
        Assert.Equal(ch.Code, (await f.Load(id)).GithubChallengeCode);
    }

    [Fact]
    public async Task A_handle_already_linked_to_another_practitioner_is_refused_case_insensitively()
    {
        await f.Seed(PractitionerStatus.Draft, p => { p.GithubLinked = true; p.GithubLogin = "Demo-Taken"; });
        var (c, id) = await Draft();
        var ch = await Challenge(c, "demo-taken");
        f.Github.Exists = true; f.Github.Bio = ch.Code;
        var r = await c.PostAsync(Verify, null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var p = await Problem(r);
        Assert.Equal("github_not_linked", p.Code);
        Assert.True(p.Errors!.ContainsKey("githubLogin"));
        Assert.False((await f.Load(id)).GithubLinked);
        Assert.Empty(await f.Events(id));
    }
}
