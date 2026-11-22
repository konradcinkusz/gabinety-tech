using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Contracts.Directory;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Directory.Tests.Internal;

/// <summary>
/// T-03.09: Directory keeps the practitioner's e-mail as a snapshot of the token they use on their profile, and gives it to Booking
/// (<c>GET /internal/v1/practitioners/{userId}/contact</c>, internal key) for notifications. Nothing public or admin ever shows it; erasure removes it.
/// </summary>
public class InternalContactTests(InternalFactory f) : IClassFixture<InternalFactory>
{
    const string Url = "/api/v1/me/practitioner";
    static string Id() => "demo-" + Guid.NewGuid().ToString("n");
    static string Name() => "Demo " + Guid.NewGuid().ToString("n")[..12]; // the in-memory database is shared by the fixture: names (slugs) must not collide
    static string Mail(string tag = "a") => $"demo-{tag}-{Guid.NewGuid():n}@demo.invalid";

    Task<HttpResponseMessage> Contact(string userId, string? key = InternalFactory.Key) => f.Internal(key).GetAsync($"/internal/v1/practitioners/{userId}/contact");

    [Fact]
    public async Task Creating_the_profile_snapshots_the_email_claim_and_the_contact_endpoint_returns_exactly_it()
    {
        var uid = Id(); var mail = Mail();
        Assert.Equal(HttpStatusCode.Created, (await f.As(uid, email: mail).PostAsJsonAsync(Url, new CreatePractitionerRequest(Name()))).StatusCode);

        var r = await Contact(uid);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal([ "email" ], body.EnumerateObject().Select(x => x.Name));
        Assert.Equal(mail, body.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Updating_submitting_and_reading_the_profile_follow_a_changed_address_and_a_token_without_one_keeps_the_stored_one()
    {
        var uid = Id(); var first = Mail("first"); var second = Mail("second"); var third = Mail("third"); var fourth = Mail("fourth");
        await f.As(uid, email: first).PostAsJsonAsync(Url, new CreatePractitionerRequest(Name()));

        var put = new UpdatePractitionerRequest(Name(), "Senior .NET reviewer", new string('b', 220), null, null, null);
        Assert.Equal(HttpStatusCode.OK, (await f.As(uid, email: second).PutAsJsonAsync(Url, put)).StatusCode);
        Assert.Equal(second, await Stored(uid));

        Assert.Equal(HttpStatusCode.OK, (await f.As(uid, email: third).GetAsync(Url)).StatusCode); // the dashboard load after a sign-in
        Assert.Equal(third, await Stored(uid));

        Assert.Equal(HttpStatusCode.OK, (await f.As(uid).GetAsync(Url)).StatusCode); // a token with no claim never erases the address
        Assert.Equal(third, await Stored(uid));
        Assert.Equal(HttpStatusCode.OK, (await f.As(uid, email: "not an address").GetAsync(Url)).StatusCode);
        Assert.Equal(third, await Stored(uid));

        await f.As(uid, email: fourth).PostAsync(Url + "/submit", null); // 422 (incomplete) or 204: either way the request is the token's moment
        Assert.Equal(third, await Stored(uid)); // a refused submit changes nothing
    }

    async Task<string?> Stored(string uid)
    {
        var r = await Contact(uid);
        return r.StatusCode == HttpStatusCode.OK ? (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("email").GetString() : null;
    }

    [Fact]
    public async Task A_submitted_profile_snapshots_the_address_of_the_submitting_token()
    {
        var uid = Id(); var mail = Mail("submit");
        var p = await f.Seed(PractitionerStatus.Draft, userId: uid, configure: x =>
        {
            x.Headline = "Senior .NET reviewer"; x.Bio = new string('b', 220); x.GithubLinked = true; x.NipValid = true; x.PortfolioUrl = "https://portfolio.example.test/x";
        });
        await f.SeedOffering(p);
        Assert.Null(await Stored(uid));
        var r = await f.As(uid, email: mail).PostAsync(Url + "/submit", null);
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal(mail, await Stored(uid));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a@b.invalid, c@d.invalid")]
    [InlineData("Jan <jan@demo.invalid>")]
    public async Task A_token_with_no_usable_address_stores_none_and_the_endpoint_is_404(string claim)
    {
        var uid = Id();
        Assert.Equal(HttpStatusCode.Created, (await f.As(uid, email: claim.Length == 0 ? null : claim).PostAsJsonAsync(Url, new CreatePractitionerRequest(Name()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Contact(uid)).StatusCode);
    }

    [Fact]
    public async Task An_unknown_user_is_404()
    {
        var r = await Contact(Id());
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("not_found", (await Verification.Vk.Problem(r)).Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("demo-wrong-key-0000")]
    public async Task Without_the_internal_key_the_contact_is_401_and_a_user_token_does_not_replace_it(string? key)
    {
        var uid = Id();
        await f.As(uid, email: Mail()).PostAsJsonAsync(Url, new CreatePractitionerRequest(Name()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Contact(uid, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.As(uid, email: Mail()).GetAsync($"/internal/v1/practitioners/{uid}/contact")).StatusCode);
    }

    [Fact]
    public async Task The_address_is_in_no_public_private_or_admin_view_and_the_by_user_lookup_stays_as_it_was()
    {
        var uid = Id(); var mail = Mail("hidden");
        var p = await f.Seed(PractitionerStatus.Approved, userId: uid, configure: x => x.ContactEmail = mail);
        var seen = new List<string>
        {
            await (await f.Anon().GetAsync($"/api/v1/practitioners/{p.Slug}")).Content.ReadAsStringAsync(),
            await (await f.Anon().GetAsync("/api/v1/practitioners")).Content.ReadAsStringAsync(),
            await (await f.Anon().GetAsync("/api/v1/sitemap")).Content.ReadAsStringAsync(),
            await (await f.As(uid).GetAsync(Url)).Content.ReadAsStringAsync(),
            await (await f.As(Id(), "Admin").GetAsync($"/api/v1/admin/practitioners/{p.Id}")).Content.ReadAsStringAsync(),
            await (await f.As(Id(), "Admin").GetAsync("/api/v1/admin/practitioners")).Content.ReadAsStringAsync(),
            await (await f.Internal().GetAsync($"/internal/v1/practitioners/by-user/{uid}")).Content.ReadAsStringAsync(),
            await (await f.Internal().GetAsync($"/internal/v1/offerings/{(await f.SeedOffering(p)).Id}")).Content.ReadAsStringAsync(),
        };
        Assert.All(seen, body => { Assert.DoesNotContain(mail, body); Assert.DoesNotContain("contactEmail", body, StringComparison.OrdinalIgnoreCase); });
        Assert.Equal(mail, await Stored(uid));
    }

    [Fact]
    public async Task Erasure_removes_the_address_and_the_old_user_id_finds_nothing()
    {
        var uid = Id(); var mail = Mail("erase");
        var p = await f.Seed(PractitionerStatus.Approved, userId: uid, configure: x => x.ContactEmail = mail);
        Assert.Equal(mail, await Stored(uid));

        Assert.Equal(HttpStatusCode.NoContent, (await f.Internal().DeleteAsync($"/internal/v1/subjects/{uid}")).StatusCode);

        Assert.Null((await f.Load(p.Id)).ContactEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await Contact(uid)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Contact((await f.Load(p.Id)).UserId)).StatusCode);
    }

    [Fact]
    public async Task The_column_is_a_nullable_254_character_string_named_contact_email()
    {
        var p = await f.Db(db => Task.FromResult(db.Model.FindEntityType(typeof(Features.Profiles.Practitioner))!.FindProperty(nameof(Features.Profiles.Practitioner.ContactEmail))!));
        Assert.Equal(("contact_email", 254, true), (p.GetColumnName(), p.GetMaxLength(), p.IsNullable));
    }
}
