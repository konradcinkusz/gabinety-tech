using System.Net;
using System.Text.Json;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.ContractAvailability;
using Gabinety.Directory.Features.Profiles;

namespace Gabinety.Directory.Tests.Search;

/// <summary>ADR proposal 0013: the public card and profile carry contract availability, and the list filters on it with <c>contract=1</c>.</summary>
[Collection("search")]
public class SearchContractTests(SearchFactory f)
{
    static DateOnly Today => ContractAvailabilityRules.TodayWarsaw(TimeProvider.System);

    static void Now(Practitioner p) { p.ContractStatus = ContractStatus.AvailableNow; p.ContractHoursPerWeek = 20; p.ContractRateMinor = 11_000; }
    static Action<Practitioner> From(DateOnly d) => p => { p.ContractStatus = ContractStatus.AvailableFrom; p.ContractAvailableFrom = d; p.ContractHoursPerWeek = 10; };

    [Fact]
    public async Task A_card_without_contract_availability_has_no_contract_property()
    {
        await f.Reset();
        await f.Approved();
        using var doc = JsonDocument.Parse(await f.Anon().GetStringAsync("/api/v1/practitioners"));
        var item = doc.RootElement.GetProperty("items")[0];
        Assert.False(item.TryGetProperty("contract", out _));
    }

    [Fact]
    public async Task Available_now_is_a_contract_without_a_date_and_the_rate_is_money()
    {
        await f.Reset();
        var p = await f.Approved(Now);
        var card = (await Sk.Get(f.Anon())).Items.Single(i => i.Id == p.Id);
        Assert.NotNull(card.Contract);
        Assert.Null(card.Contract!.AvailableFrom);
        Assert.Equal(20, card.Contract.HoursPerWeek);
        Assert.Equal(11_000, card.Contract.Rate!.AmountMinor);
    }

    [Fact]
    public async Task A_future_date_is_shown_and_a_past_date_reads_as_now()
    {
        await f.Reset();
        var later = await f.Approved(From(Today.AddMonths(3)));
        var past = await f.Approved(From(Today.AddDays(-5)));
        var items = (await Sk.Get(f.Anon())).Items;
        Assert.Equal(Today.AddMonths(3), items.Single(i => i.Id == later.Id).Contract!.AvailableFrom);
        Assert.Null(items.Single(i => i.Id == past.Id).Contract!.AvailableFrom);
    }

    [Fact]
    public async Task The_wire_omits_the_date_when_available_now_and_names_it_camelCase_otherwise()
    {
        await f.Reset();
        var now = await f.Approved(Now);
        var later = await f.Approved(From(Today.AddMonths(2)));
        using var doc = JsonDocument.Parse(await f.Anon().GetStringAsync("/api/v1/practitioners"));
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        var nowJson = items.Single(i => i.GetProperty("id").GetGuid() == now.Id).GetProperty("contract");
        var laterJson = items.Single(i => i.GetProperty("id").GetGuid() == later.Id).GetProperty("contract");
        Assert.False(nowJson.TryGetProperty("availableFrom", out _));
        Assert.Equal(Today.AddMonths(2).ToString("yyyy-MM-dd"), laterJson.GetProperty("availableFrom").GetString());
        Assert.Equal(10, laterJson.GetProperty("hoursPerWeek").GetInt32());
        Assert.False(laterJson.TryGetProperty("rate", out _));
    }

    [Fact]
    public async Task The_filter_keeps_only_those_who_advertise()
    {
        await f.Reset();
        var open = await f.Approved(Now);
        var later = await f.Approved(From(Today.AddMonths(1)));
        var closed = await f.Approved();
        var all = await Sk.Ids(f.Anon());
        Assert.Contains(closed.Id, all);
        var filtered = await Sk.Ids(f.Anon(), "?contract=1");
        Assert.Equal(2, filtered.Count);
        Assert.Contains(open.Id, filtered); Assert.Contains(later.Id, filtered); Assert.DoesNotContain(closed.Id, filtered);
    }

    [Fact]
    public async Task The_filter_combines_with_paging()
    {
        await f.Reset();
        for (var i = 0; i < 3; i++) await f.Approved(Now);
        for (var i = 0; i < 2; i++) await f.Approved();
        var (ids, pages, _) = await Sk.Walk(f.Anon(), 2, "&contract=1");
        Assert.Equal(3, ids.Count);
        Assert.Equal(2, pages);
    }

    [Theory]
    [InlineData("?contract=0")]
    [InlineData("?contract=yes")]
    [InlineData("?contract=1&contract=1")]
    public async Task A_bad_contract_parameter_is_400(string query)
    {
        var r = await f.Anon().GetAsync("/api/v1/practitioners" + query);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task The_profile_carries_contract_flat_next_to_the_card_fields()
    {
        await f.Reset();
        var p = await f.Approved(Now);
        using var doc = JsonDocument.Parse(await f.Anon().GetStringAsync("/api/v1/practitioners/" + p.Slug));
        var c = doc.RootElement.GetProperty("contract");
        Assert.Equal(20, c.GetProperty("hoursPerWeek").GetInt32());
        Assert.Equal(11_000, c.GetProperty("rate").GetProperty("amountMinor").GetInt64());
    }

    [Fact]
    public async Task A_practitioner_who_is_not_approved_is_not_listed_even_when_available()
    {
        await f.Reset();
        var draft = await f.Seed(PractitionerStatus.Draft, Now, new Spec());
        Assert.DoesNotContain(draft.Id, await Sk.Ids(f.Anon(), "?contract=1"));
    }
}
