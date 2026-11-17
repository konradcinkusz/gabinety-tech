using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Profiles;
using Gabinety.Directory.Features.Verification;
using static Gabinety.Directory.Tests.Verification.Vk;

namespace Gabinety.Directory.Tests.Verification;

public class AdminDetailTests(VerificationFactory f) : IClassFixture<VerificationFactory>
{
    const string Base = "/api/v1/admin/practitioners";

    [Theory]
    [InlineData("")] [InlineData("/verification-events")]
    public async Task Unauthenticated_is_401_and_a_user_without_the_role_is_403(string tail)
    {
        var p = await f.Seed(PractitionerStatus.Submitted);
        var url = $"{Base}/{p.Id}{tail}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.As(null).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.As("demo-plain").GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.As("demo-plain", "Practitioner").GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.As("demo-admin", "Admin").GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.As("demo-root", "SuperAdmin").GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData("")] [InlineData("/verification-events")]
    public async Task Unknown_id_is_404_problem(string tail)
    {
        var r = await f.As("demo-admin", "Admin").GetAsync($"{Base}/{Guid.NewGuid()}{tail}");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("not_found", (await Problem(r)).Code);
    }

    [Fact]
    public async Task Detail_returns_the_review_fields_of_any_status()
    {
        foreach (var status in Enum.GetValues<PractitionerStatus>())
        {
            var p = await f.Seed(status, x =>
            {
                x.Bio = "demo bio text"; x.PortfolioUrl = "https://demo.invalid/p"; x.GithubLinked = true; x.GithubLogin = "demo-gh";
                x.Nip = "5260250995"; x.NipValid = true; x.StatusReason = "demo reason of review"; x.ApprovedAt = f.Clock.GetUtcNow();
            });
            var r = await f.As("demo-admin", "Admin").GetAsync($"{Base}/{p.Id}");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var j = (await r.Content.ReadFromJsonAsync<JsonElement>(J));
            Assert.Equal(p.Id, j.GetProperty("id").GetGuid());
            Assert.Equal(status.ToString(), j.GetProperty("status").GetString());
            Assert.Equal(p.Slug, j.GetProperty("slug").GetString());
            Assert.Equal(p.DisplayName, j.GetProperty("displayName").GetString());
            Assert.Equal("demo bio text", j.GetProperty("bio").GetString());
            Assert.Equal("https://demo.invalid/p", j.GetProperty("portfolioUrl").GetString());
            Assert.True(j.GetProperty("githubLinked").GetBoolean());
            Assert.Equal("demo-gh", j.GetProperty("githubLogin").GetString());
            Assert.Equal("5260250995", j.GetProperty("nip").GetString());
            Assert.True(j.GetProperty("nipValid").GetBoolean());
            Assert.Equal("demo reason of review", j.GetProperty("statusReason").GetString());
            Assert.Equal(f.Clock.GetUtcNow(), j.GetProperty("approvedAt").GetDateTimeOffset());
        }
    }

    [Fact]
    public async Task Detail_never_exposes_the_github_challenge_or_the_owner_user_id()
    {
        var p = await f.Seed(PractitionerStatus.Submitted, x =>
        {
            x.GithubChallengeCode = "gabinety-verify-abcdefghij"; x.GithubChallengeLogin = "demo-gh"; x.GithubChallengeExpiresAt = f.Clock.GetUtcNow().AddMinutes(5);
            x.UserId = "demo-owner-secret-id";
        });
        var body = await (await f.As("demo-admin", "Admin").GetAsync($"{Base}/{p.Id}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("gabinety-verify-", body);
        Assert.DoesNotContain("challenge", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("demo-owner-secret-id", body);
    }

    [Fact]
    public async Task Detail_submittedAt_is_the_latest_Submitted_event_else_null()
    {
        var p = await f.Seed(PractitionerStatus.Submitted);
        var c = f.As("demo-admin", "Admin");
        Assert.Equal(JsonValueKind.Null, (await c.GetFromJsonAsync<JsonElement>($"{Base}/{p.Id}", J)).GetProperty("submittedAt").ValueKind);
        var first = f.Clock.GetUtcNow();
        await f.Db(async db => { VerificationEvents.Record(db, p.Id, VerificationEventKind.Submitted, "u", null, f.Clock); await db.SaveChangesAsync(); return 0; });
        f.Clock.Advance(TimeSpan.FromHours(1));
        await f.Db(async db => { VerificationEvents.Record(db, p.Id, VerificationEventKind.Submitted, "u", null, f.Clock); await db.SaveChangesAsync(); return 0; });
        var j = await c.GetFromJsonAsync<JsonElement>($"{Base}/{p.Id}", J);
        Assert.Equal(first.AddHours(1), j.GetProperty("submittedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Events_are_oldest_first_for_that_practitioner_only_with_actor_note_and_kind()
    {
        var p = await f.Seed(PractitionerStatus.Submitted);
        var other = await f.Seed(PractitionerStatus.Submitted);
        var start = f.Clock.GetUtcNow();
        await f.Db(async db =>
        {
            // written newest-first on purpose: the order must come from `at`, not from insertion
            f.Clock.Advance(TimeSpan.FromMinutes(2)); VerificationEvents.Record(db, p.Id, VerificationEventKind.Rejected, "demo-a2", "demo note two", f.Clock);
            f.Clock.Advance(TimeSpan.FromMinutes(-2)); VerificationEvents.Record(db, p.Id, VerificationEventKind.Submitted, "demo-a1", null, f.Clock);
            VerificationEvents.Record(db, other.Id, VerificationEventKind.Approved, "demo-x", null, f.Clock);
            await db.SaveChangesAsync(); return 0;
        });
        var r = await f.As("demo-admin", "Admin").GetAsync($"{Base}/{p.Id}/verification-events");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var items = (await r.Content.ReadFromJsonAsync<JsonElement>(J)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal("Submitted", items[0].GetProperty("kind").GetString());
        Assert.Equal("demo-a1", items[0].GetProperty("actorUserId").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("note").ValueKind);
        Assert.Equal(start, items[0].GetProperty("at").GetDateTimeOffset());
        Assert.Equal("Rejected", items[1].GetProperty("kind").GetString());
        Assert.Equal("demo note two", items[1].GetProperty("note").GetString());
        Assert.NotEqual(Guid.Empty, items[1].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Events_of_a_practitioner_without_history_is_an_empty_list_not_404()
    {
        var p = await f.Seed(PractitionerStatus.Draft);
        var r = await f.As("demo-admin", "Admin").GetAsync($"{Base}/{p.Id}/verification-events");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Empty((await r.Content.ReadFromJsonAsync<JsonElement>(J)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Events_equal_timestamps_are_ordered_by_id_for_a_stable_answer()
    {
        var p = await f.Seed(PractitionerStatus.Submitted);
        var ids = new[] { Guid.Parse("00000000-0000-0000-0000-000000000002"), Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000003") };
        await f.Db(async db => { foreach (var id in ids) VerificationEvents.Record(db, p.Id, VerificationEventKind.NipChanged, "demo-a", null, f.Clock, id); await db.SaveChangesAsync(); return 0; });
        var items = (await f.As("demo-admin", "Admin").GetFromJsonAsync<JsonElement>($"{Base}/{p.Id}/verification-events", J)).GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid());
        Assert.Equal(ids.Order(), items);
    }
}
