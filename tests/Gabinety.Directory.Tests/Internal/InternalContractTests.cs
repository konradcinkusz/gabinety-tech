using System.Net;
using System.Net.Http.Json;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Profiles;

namespace Gabinety.Directory.Tests.Internal;

/// <summary><c>GET /internal/v1/practitioners/{id}/contract</c> (ADR proposal 0013): the target of a collaboration proposal, and erasure of contract availability.</summary>
public class InternalContractTests(InternalFactory f) : IClassFixture<InternalFactory>
{
    static void Open(Practitioner p) { p.ContractStatus = ContractStatus.AvailableNow; p.ContractHoursPerWeek = 20; p.ContractRateMinor = 12_000; }

    [Fact]
    public async Task An_approved_advertising_practitioner_accepts_proposals_and_the_lookup_names_them()
    {
        var p = await f.Seed(PractitionerStatus.Approved, Open);
        var r = await f.Internal().GetAsync($"/internal/v1/practitioners/{p.Id}/contract");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = (await r.Content.ReadFromJsonAsync<InternalContractTargetDto>(Ik.J))!;
        Assert.Equal(p.Id, body.PractitionerId);
        Assert.Equal(p.UserId, body.PractitionerUserId);
        Assert.Equal(PractitionerStatus.Approved, body.Status);
        Assert.Equal(p.DisplayName, body.DisplayName);
        Assert.Equal(p.Slug, body.Slug);
        Assert.True(body.AcceptsProposals);
    }

    [Fact]
    public async Task Not_advertising_or_not_approved_means_no_proposals()
    {
        var closed = await f.Seed(PractitionerStatus.Approved);
        var draft = await f.Seed(PractitionerStatus.Draft, Open);
        var suspended = await f.Seed(PractitionerStatus.Suspended, Open);
        foreach (var p in new[] { closed, draft, suspended })
        {
            var body = (await f.Internal().GetFromJsonAsync<InternalContractTargetDto>($"/internal/v1/practitioners/{p.Id}/contract", Ik.J))!;
            Assert.False(body.AcceptsProposals);
        }
    }

    [Fact]
    public async Task An_unknown_practitioner_is_404()
    {
        var r = await f.Internal().GetAsync($"/internal/v1/practitioners/{Guid.NewGuid()}/contract");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task The_lookup_needs_the_internal_key()
    {
        var p = await f.Seed(PractitionerStatus.Approved, Open);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anon().GetAsync($"/internal/v1/practitioners/{p.Id}/contract")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Internal("wrong-key").GetAsync($"/internal/v1/practitioners/{p.Id}/contract")).StatusCode);
    }

    [Fact]
    public async Task Erasure_clears_contract_availability()
    {
        var p = await f.Seed(PractitionerStatus.Approved, x => { Open(x); x.ContractStatus = ContractStatus.AvailableFrom; x.ContractAvailableFrom = new DateOnly(2027, 3, 1); });
        Assert.Equal(HttpStatusCode.NoContent, (await f.Internal().DeleteAsync($"/internal/v1/subjects/{p.UserId}")).StatusCode);
        var a = await f.Load(p.Id);
        Assert.Equal(ContractStatus.NotAvailable, a.ContractStatus);
        Assert.Null(a.ContractAvailableFrom); Assert.Null(a.ContractHoursPerWeek); Assert.Null(a.ContractRateMinor);
        var body = (await f.Internal().GetFromJsonAsync<InternalContractTargetDto>($"/internal/v1/practitioners/{p.Id}/contract", Ik.J))!;
        Assert.False(body.AcceptsProposals);
    }
}
