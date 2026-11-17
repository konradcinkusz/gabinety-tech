using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Tests.Offerings;
using Microsoft.Net.Http.Headers;

namespace Gabinety.Directory.Tests.Search;

[Collection("search")]
public class SearchProfileTests(SearchFactory f)
{
    static string Url(string slug) => "/api/v1/practitioners/" + slug;

    record Extras(string Bio, string? PortfolioUrl, IReadOnlyList<Offering> Offerings, int CompletedCount, DateTimeOffset MemberSince);

    /// <summary>Reads the flat wire body (the card's fields and the extras side by side) back into the contract record.</summary>
    static PractitionerProfile Flat(string json)
    {
        var card = JsonSerializer.Deserialize<PractitionerCard>(json, Sk.J)!;
        var x = JsonSerializer.Deserialize<Extras>(json, Sk.J)!;
        return new PractitionerProfile(card, x.Bio, x.PortfolioUrl, x.Offerings, x.CompletedCount, x.MemberSince);
    }

    [Fact]
    public async Task The_profile_JSON_is_flat_camelCase_with_exactly_the_contract_properties_and_never_a_meetingUrl()
    {
        await f.Reset();
        var p = await f.Approved(x => { x.Bio = "Demo bio"; x.PortfolioUrl = "https://portfolio.example.test/me"; x.RatingCount = 3; x.RatingAvg = 4.5m; x.GithubLinked = true; x.GithubLogin = "demo-gh"; },
            new Spec("code-review", ["dotnet"], 30, 12000));
        await f.Db(async db => { db.Offerings.Where(o => o.PractitionerId == p.Id).ToList().ForEach(o => o.MeetingUrl = "https://meet.example.test/x"); await db.SaveChangesAsync(); return 0; });
        using var doc = JsonDocument.Parse(await f.Anon().GetStringAsync(Url(p.Slug)));
        var root = doc.RootElement;
        Assert.Equal(
            ["id", "slug", "displayName", "headline", "githubLogin", "badges", "rating", "isNew", "fromPrice", "problemSlugs", "durations",
             "bio", "portfolioUrl", "offerings", "completedCount", "memberSince"],
            root.EnumerateObject().Select(o => o.Name).ToArray());
        Assert.Equal(p.Slug, root.GetProperty("slug").GetString());
        Assert.False(root.TryGetProperty("card", out _));
        Assert.Equal(["id", "title", "description", "durationMinutes", "price", "problemSlug", "techSlugs"],
            root.GetProperty("offerings")[0].EnumerateObject().Select(o => o.Name).ToArray());
        Assert.DoesNotContain("meetingUrl", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_Approved_profile_is_public_and_carries_the_card_the_bio_and_only_active_offerings()
    {
        await f.Reset();
        var p = await f.Approved(x => { x.Bio = "Demo bio text"; x.PortfolioUrl = "https://portfolio.example.test/me"; x.CompletedCount = 7; x.RatingCount = 3; x.RatingAvg = 4.5m; },
            new Spec("code-review", ["dotnet"], 30, 12000), new Spec("audyt-kodu", ["csharp", "azure"], 60, 30000), new Spec("code-review", ["dotnet"], 30, 500, Active: false));
        var r = await f.Anon().GetAsync(Url(p.Slug));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = Flat(await r.Content.ReadAsStringAsync());
        Assert.Equal(p.Id, body.Card.Id);
        Assert.Equal("Demo bio text", body.Bio);
        Assert.Equal("https://portfolio.example.test/me", body.PortfolioUrl);
        Assert.Equal(7, body.CompletedCount);
        Assert.Equal(p.ApprovedAt, body.MemberSince);
        Assert.Equal(new RatingDto(4.5, 3), body.Card.Rating);
        Assert.Equal(2, body.Offerings.Count);                                   // the 5 zl offering is inactive
        Assert.Equal(new MoneyDto(12000, "PLN"), body.Offerings[0].Price);
        Assert.Equal(["csharp", "azure"], body.Offerings[1].TechSlugs);
        Assert.Equal(12000, body.Card.FromPrice.AmountMinor);
        Assert.Equal("public, max-age=300, stale-while-revalidate=3600", r.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData(PractitionerStatus.Draft)]
    [InlineData(PractitionerStatus.Submitted)]
    [InlineData(PractitionerStatus.Rejected)]
    [InlineData(PractitionerStatus.Suspended)]
    public async Task Any_status_other_than_Approved_is_404_not_found_and_not_cached(PractitionerStatus status)
    {
        await f.Reset();
        var p = await f.Seed(status, null, new Spec());
        var r = await f.Anon().GetAsync(Url(p.Slug));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal(ErrorCodes.NotFound, (await Kit.ProblemOf(r)).Code);
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task An_unknown_slug_is_404()
    {
        await f.Reset();
        var r = await f.Anon().GetAsync(Url("demo-nobody"));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task An_Approved_profile_without_an_active_offering_answers_200_with_no_offerings_and_a_zero_price()
    {
        await f.Reset();
        var p = await f.Seed(PractitionerStatus.Approved, null, new Spec(Active: false));
        var body = Flat(await f.Anon().GetStringAsync(Url(p.Slug)));
        Assert.Empty(body.Offerings);
        Assert.Equal(new MoneyDto(0, "PLN"), body.Card.FromPrice);
        Assert.Empty(body.Card.ProblemSlugs);
        Assert.Empty(await Sk.Ids(f.Anon()));                                  // ...and the list never shows it
    }

    [Fact]
    public async Task The_profile_has_an_ETag_and_If_None_Match_gets_304_until_the_content_changes()
    {
        await f.Reset();
        var p = await f.Approved();
        var c = f.Anon();
        var first = await c.GetAsync(Url(p.Slug));
        var tag = first.Headers.ETag!.Tag;
        Assert.StartsWith("\"", tag);

        var req = new HttpRequestMessage(HttpMethod.Get, Url(p.Slug));
        req.Headers.TryAddWithoutValidation(HeaderNames.IfNoneMatch, tag);
        var second = await c.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Equal(tag, second.Headers.ETag!.Tag);
        Assert.Equal("public, max-age=300, stale-while-revalidate=3600", second.Headers.CacheControl?.ToString());
        Assert.Empty(await second.Content.ReadAsStringAsync());

        var weak = new HttpRequestMessage(HttpMethod.Get, Url(p.Slug));
        weak.Headers.TryAddWithoutValidation(HeaderNames.IfNoneMatch, "\"other\", W/" + tag);
        Assert.Equal(HttpStatusCode.NotModified, (await c.SendAsync(weak)).StatusCode);

        await f.Db(async db => { var row = await db.Practitioners.FindAsync(p.Id); row!.Bio = "A changed bio"; await db.SaveChangesAsync(); return 0; });
        var stale = new HttpRequestMessage(HttpMethod.Get, Url(p.Slug));
        stale.Headers.TryAddWithoutValidation(HeaderNames.IfNoneMatch, tag);
        var third = await c.SendAsync(stale);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        Assert.NotEqual(tag, third.Headers.ETag!.Tag);
    }
}

[Collection("search")]
public class SearchLeakTests(SearchFactory f)
{
    [Fact]
    public async Task No_public_body_leaks_the_meeting_url_nip_user_id_status_reason_challenge_or_an_email()
    {
        await f.Reset();
        const string Nip = "5260250995", User = "demo-user-LEAKUSER-7f3a", Meet = "https://meet.example.test/LEAKMEET-abc", Reason = "LEAKREASON suspended earlier";
        var p = await f.Seed(PractitionerStatus.Approved, x =>
        {
            x.UserId = User; x.Nip = Nip; x.NipValid = true; x.MeetingUrlDefault = Meet; x.StatusReason = Reason; x.GithubLinked = true; x.GithubLogin = "demo-dev";
            x.GithubChallengeCode = "gabinety-verify-LEAKCODE"; x.GithubChallengeLogin = "demo-dev"; x.Bio = "Contact-free bio";
        }, new Spec());
        await f.Db(async db => { db.Offerings.Where(o => o.PractitionerId == p.Id).ToList().ForEach(o => o.MeetingUrl = Meet); await db.SaveChangesAsync(); return 0; });

        var c = f.Anon();
        var bodies = new Dictionary<string, string>
        {
            ["list"] = await c.GetStringAsync("/api/v1/practitioners"),
            ["profile"] = await c.GetStringAsync("/api/v1/practitioners/" + p.Slug),
            ["sitemap"] = await c.GetStringAsync("/api/v1/sitemap"),
            ["rules"] = await c.GetStringAsync("/api/v1/ranking-rules"),
        };
        Assert.Contains(p.Slug, bodies["list"]); // the bodies are real, not empty
        Assert.Contains(p.Slug, bodies["profile"]);

        var forbiddenNames = new[] { "meetingUrl", "meeting_url", "meetingUrlDefault", "nip", "nipValid", "userId", "user_id", "statusReason", "status_reason", "githubChallenge",
            "githubChallengeCode", "github_challenge", "githubChallengeLogin", "status" };
        var forbiddenValues = new[] { Nip, User, "LEAKUSER", "LEAKMEET", "meet.example.test", "LEAKREASON", "LEAKCODE", "gabinety-verify" };
        foreach (var (name, body) in bodies)
        {
            foreach (var v in forbiddenValues) Assert.False(body.Contains(v, StringComparison.OrdinalIgnoreCase), $"{name} leaks the value {v}");
            Assert.DoesNotMatch(@"[\w.+-]+@[\w-]+\.[\w.-]+", body);
            using var doc = JsonDocument.Parse(body);
            var names = new List<string>(); Collect(doc.RootElement, names);
            foreach (var n in forbiddenNames) Assert.DoesNotContain(n, names);
            foreach (var n in names) Assert.DoesNotContain("meeting", n, StringComparison.OrdinalIgnoreCase);
        }
    }

    static void Collect(JsonElement e, List<string> names)
    {
        if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) { names.Add(p.Name); Collect(p.Value, names); }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) Collect(x, names);
    }
}
