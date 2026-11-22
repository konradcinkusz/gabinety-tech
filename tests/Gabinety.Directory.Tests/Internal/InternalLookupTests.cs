using System.Net;
using System.Net.Http.Json;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;

namespace Gabinety.Directory.Tests.Internal;

public class InternalLookupTests(InternalFactory f) : IClassFixture<InternalFactory>
{
    [Fact]
    public async Task Offering_lookup_returns_the_offerings_own_meeting_url()
    {
        var p = await f.Seed(PractitionerStatus.Approved, x => x.MeetingUrlDefault = "https://meet.example.test/default");
        var o = await f.SeedOffering(p, meetingUrl: "https://meet.example.test/own", price: 12000, minutes: 30);
        var r = await f.Internal().GetAsync($"/internal/v1/offerings/{o.Id}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = (await r.Content.ReadFromJsonAsync<OfferingLookup>(Ik.J))!;
        Assert.Equal(o.Id, body.OfferingId);
        Assert.Equal(p.Id, body.PractitionerId);
        Assert.Equal(p.UserId, body.PractitionerUserId);
        Assert.Equal(PractitionerStatus.Approved, body.PractitionerStatus);
        Assert.Equal("Demo review", body.Title);
        Assert.Equal(30, body.DurationMinutes);
        Assert.Equal(new MoneyDto(12000, "PLN"), body.Price);
        Assert.Equal("https://meet.example.test/own", body.MeetingUrl);
        Assert.True(body.IsActive);
    }

    [Fact]
    public async Task Offering_lookup_falls_back_to_the_practitioners_default_meeting_url()
    {
        var p = await f.Seed(PractitionerStatus.Approved, x => x.MeetingUrlDefault = "https://meet.example.test/default");
        var o = await f.SeedOffering(p, meetingUrl: null, active: false);
        var body = (await f.Internal().GetFromJsonAsync<OfferingLookup>($"/internal/v1/offerings/{o.Id}", Ik.J))!;
        Assert.Equal("https://meet.example.test/default", body.MeetingUrl);
        Assert.False(body.IsActive);
    }

    [Fact]
    public async Task Offering_lookup_has_no_meeting_url_when_neither_is_set_and_reports_a_suspended_status()
    {
        var p = await f.Seed(PractitionerStatus.Suspended);
        var o = await f.SeedOffering(p);
        var body = (await f.Internal().GetFromJsonAsync<OfferingLookup>($"/internal/v1/offerings/{o.Id}", Ik.J))!;
        Assert.Null(body.MeetingUrl);
        Assert.Equal(PractitionerStatus.Suspended, body.PractitionerStatus);
    }

    [Fact]
    public async Task Unknown_offering_is_404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await f.Internal().GetAsync($"/internal/v1/offerings/{Guid.NewGuid()}")).StatusCode);

    [Fact]
    public async Task By_user_returns_id_and_status_and_nothing_else()
    {
        var p = await f.Seed(PractitionerStatus.Submitted);
        var r = await f.Internal().GetAsync($"/internal/v1/practitioners/by-user/{p.UserId}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var text = await r.Content.ReadAsStringAsync();
        var body = System.Text.Json.JsonDocument.Parse(text).RootElement;
        Assert.Equal(["practitionerId", "status"], body.EnumerateObject().Select(x => x.Name).Order().ToArray());
        Assert.Equal(p.Id, body.GetProperty("practitionerId").GetGuid());
        Assert.Equal("Submitted", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task By_user_of_a_non_practitioner_is_404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await f.Internal().GetAsync($"/internal/v1/practitioners/by-user/demo-{Guid.NewGuid():n}")).StatusCode);

    [Fact]
    public async Task Names_returns_id_display_name_and_slug_and_nothing_else()
    {
        var p = await f.Seed(PractitionerStatus.Approved);
        var r = await f.Internal().GetAsync($"/internal/v1/practitioners/{p.Id}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(["displayName", "practitionerId", "slug"], body.EnumerateObject().Select(x => x.Name).Order().ToArray());
        Assert.Equal(p.Id, body.GetProperty("practitionerId").GetGuid());
        Assert.Equal(p.DisplayName, body.GetProperty("displayName").GetString());
        Assert.Equal(p.Slug, body.GetProperty("slug").GetString());
    }

    [Fact]
    public async Task Names_of_an_unknown_practitioner_is_404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await f.Internal().GetAsync($"/internal/v1/practitioners/{Guid.NewGuid()}")).StatusCode);

    [Fact]
    public async Task Names_without_the_internal_key_is_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync($"/internal/v1/practitioners/{Guid.NewGuid()}")).StatusCode);

    // The shapes Booking's DirectoryClient reads (src/Gabinety.Booking/Clients/DirectoryClient.cs).
    sealed record OfferingLookup(Guid OfferingId, Guid PractitionerId, string PractitionerUserId, PractitionerStatus PractitionerStatus,
        string Title, int DurationMinutes, MoneyDto Price, string? MeetingUrl, bool IsActive);
}
