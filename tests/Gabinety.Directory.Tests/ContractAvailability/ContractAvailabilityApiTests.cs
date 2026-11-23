using System.Net;
using System.Net.Http.Json;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.ContractAvailability;
using Gabinety.Directory.Tests.Offerings;
using Gabinety.Directory.Tests.Profiles;

namespace Gabinety.Directory.Tests.ContractAvailability;

public class ContractAvailabilityApiTests(ProfileFactory f) : IClassFixture<ProfileFactory>
{
    const string Url = "/api/v1/me/practitioner/contract-availability";
    static DateOnly Today => ContractAvailabilityRules.TodayWarsaw(TimeProvider.System);

    [Fact]
    public async Task It_needs_a_signed_in_user()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().PutAsJsonAsync(Url, new ContractAvailabilitySettings(ContractStatus.NotAvailable, null, null, null), Kit.J)).StatusCode);
    }

    [Fact]
    public async Task Without_a_profile_it_is_404()
    {
        var c = f.ClientFor(Kit.Id());
        var r = await c.GetAsync(Url);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("not_found", (await Kit.ProblemOf(r)).Code);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync(Url, new ContractAvailabilitySettings(ContractStatus.AvailableNow, null, 10, null), Kit.J)).StatusCode);
    }

    [Fact]
    public async Task A_new_profile_is_not_available_for_contracts()
    {
        var c = await Kit.Practitioner(f);
        var s = (await c.GetFromJsonAsync<ContractAvailabilitySettings>(Url, Kit.J))!;
        Assert.Equal(new ContractAvailabilitySettings(ContractStatus.NotAvailable, null, null, null), s);
    }

    [Fact]
    public async Task The_wire_uses_snake_case_status_and_camelCase_fields()
    {
        var c = await Kit.Practitioner(f);
        var from = Today.AddMonths(3);
        var r = await c.PutAsJsonAsync(Url, new ContractAvailabilitySettings(ContractStatus.AvailableFrom, from, 20, 11_000), Kit.J);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = await r.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"available_from\"", json);
        Assert.Contains($"\"availableFrom\":\"{from:yyyy-MM-dd}\"", json);
        Assert.Contains("\"hoursPerWeek\":20", json);
        Assert.Contains("\"rateMinor\":11000", json);
    }

    [Fact]
    public async Task What_is_saved_is_what_is_read_and_switching_off_clears_it()
    {
        var c = await Kit.Practitioner(f);
        var saved = new ContractAvailabilitySettings(ContractStatus.AvailableNow, null, 32, 13_000);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync(Url, saved, Kit.J)).StatusCode);
        Assert.Equal(saved, await c.GetFromJsonAsync<ContractAvailabilitySettings>(Url, Kit.J));

        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync(Url, new ContractAvailabilitySettings(ContractStatus.NotAvailable, null, 32, 13_000), Kit.J)).StatusCode);
        Assert.Equal(new ContractAvailabilitySettings(ContractStatus.NotAvailable, null, null, null), await c.GetFromJsonAsync<ContractAvailabilitySettings>(Url, Kit.J));
    }

    [Theory]
    [InlineData("available_now", null, 0, "hoursPerWeek")]
    [InlineData("available_now", null, 61, "hoursPerWeek")]
    [InlineData("available_from", null, 10, "availableFrom")]
    public async Task Invalid_input_is_422_with_the_field_named(string status, string? from, int hours, string field)
    {
        var c = await Kit.Practitioner(f);
        var r = await c.PutAsJsonAsync(Url, new { status, availableFrom = from, hoursPerWeek = hours }, Kit.J);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var p = await Kit.ProblemOf(r);
        Assert.Equal("validation_failed", p.Code);
        Assert.Contains(field, p.Errors!.Keys);
    }

    [Fact]
    public async Task A_past_date_for_available_from_is_422()
    {
        var c = await Kit.Practitioner(f);
        var r = await c.PutAsJsonAsync(Url, new ContractAvailabilitySettings(ContractStatus.AvailableFrom, Today, 10, null), Kit.J);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Contains("availableFrom", (await Kit.ProblemOf(r)).Errors!.Keys);
    }

    [Fact]
    public async Task An_unknown_status_string_is_refused_and_nothing_changes()
    {
        var c = await Kit.Practitioner(f);
        var r = await c.PutAsJsonAsync(Url, new { status = "maybe", hoursPerWeek = 10 }, Kit.J);
        Assert.True(r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
        Assert.Equal(ContractStatus.NotAvailable, (await c.GetFromJsonAsync<ContractAvailabilitySettings>(Url, Kit.J))!.Status);
    }

    [Fact]
    public async Task The_profile_PUT_does_not_wipe_contract_availability()
    {
        var c = await Kit.Practitioner(f);
        var saved = new ContractAvailabilitySettings(ContractStatus.AvailableNow, null, 24, 12_000);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync(Url, saved, Kit.J)).StatusCode);

        var put = await c.PutAsJsonAsync("/api/v1/me/practitioner", new UpdatePractitionerRequest("Demo Name", "A headline of enough length", "short bio"), Kit.J);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(saved, await c.GetFromJsonAsync<ContractAvailabilitySettings>(Url, Kit.J));
    }

    [Fact]
    public async Task Each_user_edits_only_their_own_row()
    {
        var a = await Kit.Practitioner(f);
        var b = await Kit.Practitioner(f);
        Assert.Equal(HttpStatusCode.OK, (await a.PutAsJsonAsync(Url, new ContractAvailabilitySettings(ContractStatus.AvailableNow, null, 40, null), Kit.J)).StatusCode);
        Assert.Equal(ContractStatus.NotAvailable, (await b.GetFromJsonAsync<ContractAvailabilitySettings>(Url, Kit.J))!.Status);
    }
}
