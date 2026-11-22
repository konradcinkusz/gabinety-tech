using System.Net;
using Bunit;
using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Practitioners;
using Gabinety.Web.Components.Shared;
using Gabinety.Web.Hosting;
using Gabinety.Web.Tests.Public;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Web.Tests.Host;

/// <summary>T-11.19 (ADR-0013): <c>GET /avatar/{slug}</c> with a fake GitHub. The host never reaches the network; the fake is the primary handler of the one named client.</summary>
public class AvatarEndpointTests
{
    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4];

    static HttpResponseMessage Image(byte[]? bytes = null, string type = "image/png") => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes ?? Png) { Headers = { ContentType = new(type) } } };

    static HttpResponseMessage Redirect(string to) => new(HttpStatusCode.Found) { Headers = { Location = new Uri(to) } };

    /// <summary>GitHub answers <c>github.com/&lt;login&gt;.png</c> with a redirect to the avatars host, which answers with the image: the real shape, reproduced.</summary>
    static HttpResponseMessage Github(HttpRequestMessage r) =>
        r.RequestUri!.Host == "github.com" ? Redirect("https://avatars.githubusercontent.com/u/1?size=96") : Image();

    static WebFactory Host(FakeBackend github, string? login = "demo-anna", TestClock? clock = null)
    {
        var web = new WebFactory().Configure(s =>
        {
            s.AddHttpClient(AvatarEndpoint.ClientName).ConfigurePrimaryHttpMessageHandler(() => github);
            if (clock is not null) s.AddSingleton<TimeProvider>(clock);
        });
        web.Directory.Respond = r => r.RequestUri!.AbsolutePath == "/api/v1/practitioners/demo-anna-demowska" ? FakeBackend.Json(PublicSupport.Profile(github: login)) : new HttpResponseMessage(HttpStatusCode.NotFound);
        return web;
    }

    static async Task AssertMissAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync()); // no detail, and not the not-found page either
    }

    [Fact]
    public async Task The_picture_is_served_with_the_cache_headers_and_a_second_request_does_not_call_GitHub()
    {
        var github = new FakeBackend { Respond = Github };
        await using var web = Host(github);
        var client = web.CreateClient();

        var first = await client.GetAsync("/avatar/demo-anna-demowska");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("image/png", first.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await first.Content.ReadAsByteArrayAsync());
        Assert.Equal("public, max-age=86400", first.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", first.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(["/demo-anna.png?size=96", "/u/1?size=96"], github.Requests.Select(r => r.PathAndQuery)); // the one URL built from the login, then the redirect it was given

        var callsAfterFirst = github.Calls;
        var directoryCalls = web.Directory.Calls;
        var second = await client.GetAsync("/avatar/demo-anna-demowska");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(Png, await second.Content.ReadAsByteArrayAsync());
        Assert.Equal(callsAfterFirst, github.Calls);
        Assert.Equal(directoryCalls, web.Directory.Calls);
    }

    [Fact]
    public async Task A_login_that_fails_the_rule_is_a_404_and_GitHub_is_not_called()
    {
        var github = new FakeBackend { Respond = Github };
        await using var web = Host(github, login: "demo/../anna?x=1");
        await AssertMissAsync(await web.CreateClient().GetAsync("/avatar/demo-anna-demowska"));
        Assert.Equal(0, github.Calls);
    }

    [Fact]
    public async Task A_redirect_to_another_host_is_a_404_and_that_host_is_never_called()
    {
        var github = new FakeBackend { Respond = r => r.RequestUri!.Host == "github.com" ? Redirect("https://evil.example/u/1.png") : Image() };
        await using var web = Host(github);
        await AssertMissAsync(await web.CreateClient().GetAsync("/avatar/demo-anna-demowska"));
        Assert.DoesNotContain(github.Requests, r => r.PathAndQuery.StartsWith("/u/1.png", StringComparison.Ordinal));
        Assert.Equal(1, github.Calls);
    }

    [Fact]
    public async Task A_body_over_200_KB_is_a_404()
    {
        var github = new FakeBackend { Respond = _ => Image(new byte[AvatarEndpoint.MaxBytes + 1]) };
        await using var web = Host(github);
        await AssertMissAsync(await web.CreateClient().GetAsync("/avatar/demo-anna-demowska"));
    }

    [Fact]
    public async Task A_body_that_grows_past_the_limit_without_a_length_is_cut_off_and_is_a_404()
    {
        var github = new FakeBackend { Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new EndlessStream()) { Headers = { ContentType = new("image/png") } } } };
        await using var web = Host(github);
        await AssertMissAsync(await web.CreateClient().GetAsync("/avatar/demo-anna-demowska"));
    }

    [Theory]
    [InlineData("image/svg+xml")] [InlineData("text/html")] [InlineData("image/gif")]
    public async Task A_content_type_other_than_png_or_jpeg_is_a_404(string type)
    {
        var github = new FakeBackend { Respond = _ => Image(type: type) };
        await using var web = Host(github);
        await AssertMissAsync(await web.CreateClient().GetAsync("/avatar/demo-anna-demowska"));
    }

    [Fact]
    public async Task A_jpeg_is_accepted()
    {
        var github = new FakeBackend { Respond = _ => Image(type: "image/jpeg") };
        await using var web = Host(github);
        var response = await web.CreateClient().GetAsync("/avatar/demo-anna-demowska");
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData("Demo-Anna")] [InlineData("demo_anna")] [InlineData("a")]
    public async Task A_slug_that_is_not_a_slug_is_a_404_without_a_call_to_the_Directory_or_to_GitHub(string slug)
    {
        var github = new FakeBackend { Respond = Github };
        await using var web = Host(github);
        await AssertMissAsync(await web.CreateClient().GetAsync("/avatar/" + slug));
        Assert.Equal(0, github.Calls);
        Assert.Equal(0, web.Directory.CallsExceptShellCheck);
    }

    [Fact]
    public async Task A_practitioner_the_Directory_does_not_know_is_a_404()
    {
        var github = new FakeBackend { Respond = Github };
        await using var web = Host(github);
        await AssertMissAsync(await web.CreateClient().GetAsync("/avatar/demo-nobody"));
        Assert.Equal(0, github.Calls);
    }

    [Fact]
    public async Task A_failure_is_remembered_for_ten_minutes_and_then_asked_again()
    {
        var clock = TestClock.Default();
        var github = new FakeBackend { Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError) };
        await using var web = Host(github, clock: clock);
        var client = web.CreateClient();
        await AssertMissAsync(await client.GetAsync("/avatar/demo-anna-demowska"));
        var afterFirst = github.Calls; // the kernel's resilience handler may retry a 5xx once
        Assert.True(afterFirst > 0);
        await AssertMissAsync(await client.GetAsync("/avatar/demo-anna-demowska"));
        Assert.Equal(afterFirst, github.Calls);
        clock.Advance(TimeSpan.FromMinutes(11));
        github.Respond = Github;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/avatar/demo-anna-demowska")).StatusCode);
    }

    [Fact]
    public async Task The_route_is_public_and_a_slow_GitHub_is_a_404_within_the_timeout()
    {
        var github = new FakeBackend { Respond = Github, Delay = TimeSpan.FromSeconds(30) };
        await using var web = Host(github);
        var started = DateTime.UtcNow;
        await AssertMissAsync(await web.CreateClient().GetAsync("/avatar/demo-anna-demowska")); // no session, no redirect to sign-in
        Assert.InRange(DateTime.UtcNow - started, TimeSpan.Zero, TimeSpan.FromSeconds(10));
    }

    sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) { Array.Fill(buffer, (byte)1, offset, count); return count; }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>The markup half: the image exists only for a practitioner with a verified GitHub login, and lies over the initials.</summary>
public class AvatarMarkupTests : BunitContext
{
    static PractitionerCardDto Card(string? login) => Sample.Card() with { GithubLogin = login };

    [Fact]
    public void A_card_without_githubLogin_renders_no_img_and_keeps_the_initials()
    {
        var cut = Render<PractitionerCard>(p => p.Add(c => c.Practitioner, Card(null)));
        Assert.Empty(cut.FindAll("img"));
        Assert.Equal("AD", cut.Find(".avatar").TextContent);
    }

    [Fact]
    public void A_card_with_githubLogin_renders_the_image_over_the_initials_with_no_script()
    {
        var cut = Render<PractitionerCard>(p => p.Add(c => c.Practitioner, Card("demo-anna")));
        var img = cut.Find(".avatar img");
        Assert.Equal("/avatar/demo-anna-demowska", img.GetAttribute("src"));
        Assert.Equal("", img.GetAttribute("alt"));
        Assert.Equal("lazy", img.GetAttribute("loading"));
        Assert.Null(img.GetAttribute("onerror"));
        Assert.StartsWith("AD", cut.Find(".avatar").TextContent); // the initials stay under the image
    }

    [Fact]
    public void An_avatar_without_a_slug_renders_no_img()
    {
        var cut = Render<Avatar>(p => p.Add(a => a.Name, "Anna Demowska"));
        Assert.Empty(cut.FindAll("img"));
    }

    [Fact]
    public async Task The_profile_page_renders_the_image_only_with_a_githubLogin()
    {
        await using var without = new WebFactory();
        without.Directory.Respond = _ => FakeBackend.Json(PublicSupport.Profile(github: null));
        var page = await without.NewBrowser().GetAsync("/gabinet/demo-anna-demowska");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Empty(page.All(".identity img"));

        await using var with = new WebFactory();
        with.Directory.Respond = _ => FakeBackend.Json(PublicSupport.Profile(github: "demo-anna"));
        var verified = await with.NewBrowser().GetAsync("/gabinet/demo-anna-demowska");
        Assert.Equal("/avatar/demo-anna-demowska", verified.One(".identity img")!.GetAttribute("src"));
    }
}
