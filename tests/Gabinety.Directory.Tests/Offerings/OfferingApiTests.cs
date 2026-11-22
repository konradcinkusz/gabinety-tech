using System.Net;
using System.Net.Http.Json;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Gabinety.Directory.Tests.Offerings.Kit;

namespace Gabinety.Directory.Tests.Offerings;

public class OfferingApiTests(OfferingFactory f) : IClassFixture<OfferingFactory>, IAsyncLifetime
{
    public async Task InitializeAsync() => await WaitForSeed(f);

    public Task DisposeAsync() => Task.CompletedTask;

    async Task Rejected(OfferingRequest req, string field, string code = "validation_failed")
    {
        var c = await Practitioner(f);
        var r = await c.PostAsJsonAsync(Url, req);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var p = await ProblemOf(r);
        Assert.Equal(code, p.Code);
        if (field != "") Assert.Contains(field, p.Errors!.Keys);
        Assert.Empty(await List(c)); // nothing was stored
    }

    async Task Accepted(OfferingRequest req)
    {
        var c = await Practitioner(f);
        var r = await c.PostAsJsonAsync(Url, req);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    }

    [Theory]
    [InlineData(4999, false)] [InlineData(5000, true)] [InlineData(150000, true)] [InlineData(150001, false)]
    public async Task Price_boundaries(long price, bool ok)
    {
        if (ok) await Accepted(Valid(price: price)); else await Rejected(Valid(price: price), "priceMinor");
    }

    [Theory]
    [InlineData(29, false)] [InlineData(30, true)] [InlineData(45, false)] [InlineData(60, true)] [InlineData(61, false)]
    public async Task Duration_is_exactly_30_or_60(int minutes, bool ok)
    {
        if (ok) await Accepted(Valid(duration: minutes)); else await Rejected(Valid(duration: minutes), "durationMinutes");
    }

    [Theory]
    [InlineData(4, false)] [InlineData(5, true)] [InlineData(80, true)] [InlineData(81, false)]
    public async Task Title_length_boundaries(int len, bool ok)
    {
        var t = new string('t', len);
        if (ok) await Accepted(Valid(title: t)); else await Rejected(Valid(title: t), "title");
    }

    [Fact]
    public async Task Title_is_trimmed_before_counting_and_control_characters_are_rejected()
    {
        await Rejected(Valid(title: "  abcd  "), "title");
        await Rejected(Valid(title: "Tytuł\u0007z dzwonkiem"), "title");
    }

    [Theory]
    [InlineData(0, true)] [InlineData(600, true)] [InlineData(601, false)]
    public async Task Description_length_boundaries(int len, bool ok)
    {
        var d = new string('d', len);
        if (ok) await Accepted(Valid(description: d)); else await Rejected(Valid(description: d), "description");
    }

    [Fact]
    public async Task Four_tech_slugs_are_fine_five_are_not()
    {
        await Accepted(Valid(techs: ["dotnet", "csharp", "azure", "python"]));
        await Rejected(Valid(techs: ["dotnet", "csharp", "azure", "python", "react"]), "techSlugs");
    }

    [Fact] public async Task Duplicate_tech_slug_is_422() => await Rejected(Valid(techs: ["dotnet", "dotnet"]), "techSlugs");
    [Fact] public async Task Unknown_tech_slug_is_422() => await Rejected(Valid(techs: ["demo-nonexistent"]), "techSlugs");
    [Fact] public async Task Unknown_problem_slug_is_422() => await Rejected(Valid(problem: "demo-nonexistent"), "problemSlug");
    [Fact] public async Task Tech_slug_as_problem_is_422() => await Rejected(Valid(problem: "dotnet"), "problemSlug");
    [Fact] public async Task Problem_slug_as_tech_is_422() => await Rejected(Valid(techs: ["code-review"]), "techSlugs");

    [Fact]
    public async Task Inactive_slugs_are_422()
    {
        await f.AddInactiveCategoriesAsync();
        await Rejected(Valid(problem: "demo-inactive-problem"), "problemSlug");
        await Rejected(Valid(techs: ["demo-inactive-tech"]), "techSlugs");
    }

    [Theory]
    [InlineData("https://meet.google.com/abc-defg-hij", true)]
    [InlineData("https://eu.zoom.us/j/123", true)] // subdomain of an allowed host
    [InlineData("https://evilzoom.us/j/1", false)] // not a subdomain: only a suffix of the name
    [InlineData("https://example.test/room", false)]
    [InlineData("http://meet.google.com/abc", false)]
    [InlineData("https://127.0.0.1/room", false)]
    [InlineData("https://localhost/room", false)]
    public async Task Meeting_url_rules(string url, bool ok)
    {
        if (ok) await Accepted(Valid(meeting: url)); else await Rejected(Valid(meeting: url), "meetingUrl");
    }

    [Fact]
    public async Task Meeting_url_of_301_characters_is_422() =>
        await Rejected(Valid(meeting: "https://meet.google.com/" + new string('a', 301 - 24)), "meetingUrl");

    [Fact]
    public async Task Meeting_url_is_optional() => await Accepted(Valid(meeting: null));

    [Theory]
    [InlineData("Pisz na jan@example.test")]
    [InlineData("Dzwoń 600 700 800")]
    [InlineData("Znajdź mnie @jankowalski")]
    public async Task Contact_details_in_title_or_description_are_422(string text)
    {
        await Rejected(Valid(title: text), "", "contact_details_not_allowed");
        await Rejected(Valid(description: text), "", "contact_details_not_allowed");
    }

    [Fact]
    public async Task Sixth_active_offering_is_too_many_and_deactivating_frees_a_place()
    {
        var c = await Practitioner(f);
        var made = new List<OfferingPrivate>();
        for (var i = 0; i < 5; i++) made.Add(await Created(c, Valid(title: $"Oferta numer {i}")));
        var r = await c.PostAsJsonAsync(Url, Valid(title: "Oferta szósta"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("too_many_offerings", (await ProblemOf(r)).Code);
        Assert.Equal(5, (await List(c)).Count);

        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"{Url}/{made[0].Id}/deactivate", null)).StatusCode);
        await Created(c, Valid(title: "Oferta szósta")); // the inactive one does not count
        Assert.Equal(6, (await List(c)).Count);
    }

    [Fact]
    public async Task Create_returns_the_private_view_and_get_lists_inactive_ones_with_the_meeting_url_in_creation_order()
    {
        var c = await Practitioner(f);
        var a = await Created(c, Valid(title: "Pierwsza oferta", price: 7000, meeting: "https://meet.google.com/abc"));
        var b = await Created(c, Valid(title: "Druga oferta", duration: 60));
        Assert.Equal(new MoneyDto(7000, "PLN"), a.Price);
        Assert.True(a.IsActive);
        Assert.Equal("https://meet.google.com/abc", a.MeetingUrl);
        await c.PostAsync($"{Url}/{a.Id}/deactivate", null);

        var items = await List(c);
        Assert.Equal([a.Id, b.Id], items.Select(x => x.Id));
        Assert.False(items[0].IsActive);
        Assert.Equal("https://meet.google.com/abc", items[0].MeetingUrl);
        Assert.True(items[1].IsActive);
        Assert.Equal(60, items[1].DurationMinutes);
    }

    [Fact]
    public async Task Deactivate_is_204_and_idempotent()
    {
        var c = await Practitioner(f);
        var o = await Created(c, Valid());
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"{Url}/{o.Id}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"{Url}/{o.Id}/deactivate", null)).StatusCode);
        Assert.False((await List(c)).Single().IsActive);
    }

    [Fact]
    public async Task Activate_turns_a_deactivated_offering_back_on_and_is_idempotent()
    {
        var c = await Practitioner(f);
        var o = await Created(c, Valid());
        await c.PostAsync($"{Url}/{o.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"{Url}/{o.Id}/activate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"{Url}/{o.Id}/activate", null)).StatusCode);
        Assert.True((await List(c)).Single().IsActive);
    }

    [Fact]
    public async Task A_suspended_practitioner_activating_an_offer_stays_unlisted_and_has_no_public_profile()
    {
        var uid = Id();
        var c = await Practitioner(f, uid);
        var o = await Created(c, Valid());
        await c.PostAsync($"{Url}/{o.Id}/deactivate", null);
        string slug;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Gabinety.Directory.Persistence.DirectoryDbContext>();
            var p = await db.Practitioners.SingleAsync(x => x.UserId == uid);
            p.Status = PractitionerStatus.Suspended;
            slug = p.Slug;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"{Url}/{o.Id}/activate", null)).StatusCode);
        var anonymous = f.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/practitioners/{slug}")).StatusCode); // only Approved profiles are public
        var list = await anonymous.GetStringAsync("/api/v1/practitioners?limit=100");
        Assert.DoesNotContain(slug, list);
    }

    [Fact]
    public async Task Activate_refuses_a_sixth_active_offering_and_another_practitioners_offering_is_404()
    {
        var c = await Practitioner(f);
        var first = await Created(c, Valid(title: "Pierwsza oferta"));
        await c.PostAsync($"{Url}/{first.Id}/deactivate", null);
        for (var i = 0; i < 5; i++) await Created(c, Valid(title: $"Oferta numer {i}"));
        var r = await c.PostAsync($"{Url}/{first.Id}/activate", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("too_many_offerings", (await ProblemOf(r)).Code);
        Assert.False((await List(c)).Single(x => x.Id == first.Id).IsActive);

        var other = await Practitioner(f);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"{Url}/{first.Id}/activate", null)).StatusCode);
    }

    [Fact]
    public async Task Put_changes_the_offering_and_leaves_the_others_untouched_and_does_not_reactivate()
    {
        var c = await Practitioner(f);
        var a = await Created(c, Valid(title: "Oferta do zmiany", price: 10000));
        var b = await Created(c, Valid(title: "Oferta sąsiednia", price: 12000));
        var r = await c.PutAsJsonAsync($"{Url}/{a.Id}", Valid(title: "Oferta zmieniona", price: 20000, duration: 60, techs: ["csharp"]));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var updated = (await r.Content.ReadFromJsonAsync<OfferingPrivate>(J))!;
        Assert.Equal((a.Id, "Oferta zmieniona", 20000L, 60), (updated.Id, updated.Title, updated.Price.AmountMinor, updated.DurationMinutes));
        Assert.Equal(["csharp"], updated.TechSlugs);

        var items = await List(c);
        Assert.Equal(12000, items.Single(x => x.Id == b.Id).Price.AmountMinor);
        Assert.Equal("Oferta sąsiednia", items.Single(x => x.Id == b.Id).Title);

        await c.PostAsync($"{Url}/{a.Id}/deactivate", null);
        await c.PutAsJsonAsync($"{Url}/{a.Id}", Valid(title: "Nadal nieaktywna"));
        Assert.False((await List(c)).Single(x => x.Id == a.Id).IsActive);
    }

    [Fact]
    public async Task Put_validates_like_create()
    {
        var c = await Practitioner(f);
        var o = await Created(c, Valid());
        var r = await c.PutAsJsonAsync($"{Url}/{o.Id}", Valid(price: 4999));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal(10000, (await List(c)).Single().Price.AmountMinor);
        r = await c.PutAsJsonAsync($"{Url}/{o.Id}", Valid(title: "Pisz jan@example.test"));
        Assert.Equal("contact_details_not_allowed", (await ProblemOf(r)).Code);
    }

    [Fact]
    public async Task Another_practitioners_offering_is_404_on_put_and_deactivate_and_absent_from_get()
    {
        var owner = await Practitioner(f);
        var other = await Practitioner(f);
        var o = await Created(owner, Valid());
        var put = await other.PutAsJsonAsync($"{Url}/{o.Id}", Valid(title: "Przejęta oferta"));
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal("not_found", (await ProblemOf(put)).Code);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"{Url}/{o.Id}/deactivate", null)).StatusCode);
        Assert.Empty(await List(other));
        Assert.True((await List(owner)).Single().IsActive);
        Assert.Equal("Przegląd kodu .NET", (await List(owner)).Single().Title);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PutAsJsonAsync($"{Url}/{Guid.NewGuid()}", Valid())).StatusCode); // unknown id
    }

    [Fact]
    public async Task User_without_a_practitioner_profile_gets_404_on_all_four_verbs()
    {
        var c = f.ClientFor(Id());
        foreach (var r in new[]
        {
            await c.GetAsync(Url), await c.PostAsJsonAsync(Url, Valid()), await c.PutAsJsonAsync($"{Url}/{Guid.NewGuid()}", Valid()),
            await c.PostAsync($"{Url}/{Guid.NewGuid()}/deactivate", null),
        })
        {
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Equal("not_found", (await ProblemOf(r)).Code);
        }
    }

    [Fact]
    public async Task Unauthenticated_is_401()
    {
        var c = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync(Url, Valid())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PutAsJsonAsync($"{Url}/{Guid.NewGuid()}", Valid())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsync($"{Url}/{Guid.NewGuid()}/deactivate", null)).StatusCode);
    }

    [Fact]
    public async Task Completeness_rises_by_a_quarter_with_the_first_active_offering_and_falls_back_after_deactivation()
    {
        var sub = Id();
        var c = await Practitioner(f, sub);
        var before = await Completeness(f, sub);
        var a = await Created(c, Valid(title: "Pierwsza oferta"));
        Assert.Equal(before + 0.25m, await Completeness(f, sub));
        var b = await Created(c, Valid(title: "Druga oferta"));
        Assert.Equal(before + 0.25m, await Completeness(f, sub)); // the component is "at least one"
        await c.PostAsync($"{Url}/{a.Id}/deactivate", null);
        Assert.Equal(before + 0.25m, await Completeness(f, sub)); // b is still active
        await c.PostAsync($"{Url}/{b.Id}/deactivate", null);
        Assert.Equal(before, await Completeness(f, sub));
    }

    [Fact]
    public async Task Submit_no_longer_lists_offering_once_an_active_one_exists()
    {
        var c = await Practitioner(f);
        var before = await c.PostAsync("/api/v1/me/practitioner/submit", null);
        Assert.Contains("offering", await before.Content.ReadAsStringAsync());
        await Created(c, Valid());
        var after = await c.PostAsync("/api/v1/me/practitioner/submit", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, after.StatusCode); // the other items stay unmet
        var body = await after.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"offering\"", body);
        Assert.Contains("github_linked", body);
    }
}

public class OfferingPriceConfigTests(PriceConfigFactory f) : IClassFixture<PriceConfigFactory>
{
    [Theory]
    [InlineData(5999, false)] [InlineData(6000, true)] [InlineData(9000, true)] [InlineData(9001, false)]
    public async Task Price_limits_come_from_configuration(long price, bool ok)
    {
        await WaitForSeed(f);
        var c = await Practitioner(f);
        var r = await c.PostAsJsonAsync(Url, Valid(price: price));
        Assert.Equal(ok ? HttpStatusCode.Created : HttpStatusCode.UnprocessableEntity, r.StatusCode);
    }
}
