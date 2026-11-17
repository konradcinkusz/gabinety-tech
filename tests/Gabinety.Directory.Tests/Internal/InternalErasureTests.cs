using System.Net;
using Gabinety.Contracts.Directory;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Directory.Tests.Internal;

public class InternalErasureTests(InternalFactory f) : IClassFixture<InternalFactory>
{
    [Fact]
    public async Task Erasure_anonymises_the_row_and_leaves_no_text_of_the_user()
    {
        var p = await f.Seed(PractitionerStatus.Approved, x =>
        {
            x.DisplayName = "Demo Jan Kowalski"; x.Headline = "demo headline"; x.Bio = "demo bio text"; x.GithubLogin = "demo-gh"; x.GithubLinked = true;
            x.Nip = "1234563218"; x.NipValid = true; x.PortfolioUrl = "https://portfolio.example.test/jan"; x.MeetingUrlDefault = "https://meet.example.test/jan";
            x.StatusReason = "demo reason"; x.GithubChallengeCode = "demo-code"; x.GithubChallengeLogin = "demo-gh"; x.GithubChallengeExpiresAt = DateTimeOffset.UtcNow;
        });
        var o = await f.SeedOffering(p, meetingUrl: "https://meet.example.test/own");
        var oldUser = p.UserId; var oldSlug = p.Slug;

        var r = await f.Internal().DeleteAsync($"/internal/v1/subjects/{oldUser}");
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);

        var a = await f.Load(p.Id);
        Assert.Equal("Usunięty użytkownik", a.DisplayName);
        Assert.Equal(PractitionerStatus.Suspended, a.Status);
        Assert.NotEqual(oldUser, a.UserId);
        Assert.NotEqual(oldSlug, a.Slug);
        Assert.DoesNotContain("demo", a.Slug);
        Assert.Equal("", a.Headline);
        Assert.Equal("", a.Bio);
        Assert.Null(a.GithubLogin); Assert.False(a.GithubLinked);
        Assert.Null(a.Nip); Assert.False(a.NipValid);
        Assert.Null(a.PortfolioUrl); Assert.Null(a.MeetingUrlDefault);
        Assert.Null(a.StatusReason);
        Assert.Null(a.GithubChallengeCode); Assert.Null(a.GithubChallengeLogin); Assert.Null(a.GithubChallengeExpiresAt);

        var offerings = await f.OfferingsOf(p.Id);
        var off = Assert.Single(offerings);
        Assert.False(off.IsActive);
        Assert.Equal("", off.Title); Assert.Equal("", off.Description); Assert.Null(off.MeetingUrl);

        // nothing left that finds the person again
        Assert.Equal(HttpStatusCode.NotFound, (await f.Internal().GetAsync($"/internal/v1/practitioners/by-user/{oldUser}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Anon().GetAsync($"/api/v1/practitioners/{oldSlug}")).StatusCode);
    }

    [Fact]
    public async Task Erasure_twice_equals_once_and_an_unknown_subject_is_204()
    {
        var p = await f.Seed(PractitionerStatus.Draft);
        var c = f.Internal();
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/internal/v1/subjects/{p.UserId}")).StatusCode);
        var once = await f.Load(p.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/internal/v1/subjects/{p.UserId}")).StatusCode);
        var twice = await f.Load(p.Id);
        Assert.Equal(once.UserId, twice.UserId);
        Assert.Equal(once.Slug, twice.Slug);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/internal/v1/subjects/demo-{Guid.NewGuid():n}")).StatusCode);
    }

    [Fact]
    public async Task Erasure_replaces_the_user_id_with_a_different_random_value_per_row()
    {
        var a = await f.Seed(PractitionerStatus.Draft); var b = await f.Seed(PractitionerStatus.Draft);
        await f.Internal().DeleteAsync($"/internal/v1/subjects/{a.UserId}");
        await f.Internal().DeleteAsync($"/internal/v1/subjects/{b.UserId}");
        var (ra, rb) = (await f.Load(a.Id), await f.Load(b.Id));
        Assert.NotEqual(ra.UserId, rb.UserId);
        Assert.NotEqual(ra.Slug, rb.Slug);
    }

    [Fact]
    public async Task An_erased_practitioner_no_longer_appears_in_the_public_listing()
    {
        var p = await f.Seed(PractitionerStatus.Approved);
        await f.Internal().DeleteAsync($"/internal/v1/subjects/{p.UserId}");
        var list = await f.Anon().GetStringAsync("/api/v1/practitioners?limit=24");
        Assert.DoesNotContain(p.Id.ToString(), list);
    }
}
