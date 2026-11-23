using System.Net;
using System.Text.Json;
using Gabinety.Booking.Features.Proposals;
using Gabinety.Contracts.Booking;

namespace Gabinety.Booking.Tests;

public class ReadProposalsApiTests
{
    static async Task<JsonElement> ViewOf(HttpClient c, Guid id)
    {
        var r = await ProposalHost.View(c, id);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Body();
    }

    [Fact]
    public async Task Both_parties_see_terms_and_every_round_a_stranger_sees_404()
    {
        await using var h = new ProposalHost();
        var id = await h.Create(body: h.Req(text: "Pierwsza wiadomość klienta o współpracy.", company: "Demo Sp. z o.o."));
        await ProposalHost.Counter(h.AsPractitioner(), id, 1, ProposalHost.LongTerms(rate: 17000), "Odpowiedź programisty na propozycję.");

        foreach (var (client, role) in new[] { (h.AsClient(), "Client"), (h.AsPractitioner(), "Practitioner") })
        {
            var v = await ViewOf(client, id);
            Assert.Equal(role, v.GetProperty("viewerRole").GetString());
            Assert.Equal(17000, v.GetProperty("terms").GetProperty("rate").GetProperty("amountMinor").GetInt64());
            var rounds = v.GetProperty("rounds").EnumerateArray().ToList();
            Assert.Equal([1, 2], rounds.Select(x => x.GetProperty("number").GetInt32()));
            Assert.Equal(["Proposed", "Countered"], rounds.Select(x => x.GetProperty("action").GetString()));
            Assert.Equal(["Client", "Practitioner"], rounds.Select(x => x.GetProperty("by").GetString()));
            Assert.Equal("Pierwsza wiadomość klienta o współpracy.", rounds[0].GetProperty("text").GetString());
            Assert.Equal("Odpowiedź programisty na propozycję.", rounds[1].GetProperty("text").GetString());
            Assert.Equal("Demo Sp. z o.o.", v.GetProperty("companyName").GetString());
        }
        Assert.Equal(HttpStatusCode.NotFound, (await ProposalHost.View(h.AsClient(ProposalHost.Stranger), id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ProposalHost.View(h.AsClient(ProposalHost.OtherClient), id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ProposalHost.View(h.AsClient(), Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ProposalHost.View(h.Host.ClientFor(null), id)).StatusCode);
    }

    [Fact]
    public async Task The_flags_are_the_viewers()
    {
        await using var h = new ProposalHost();
        var id = await h.Create();
        var asPractitioner = await ViewOf(h.AsPractitioner(), id);
        Assert.Equal([true, true, true, false], new[] { "canAccept", "canCounter", "canReject", "canWithdraw" }.Select(f => asPractitioner.GetProperty(f).GetBoolean()));
        var asClient = await ViewOf(h.AsClient(), id);
        Assert.Equal([false, false, false, true], new[] { "canAccept", "canCounter", "canReject", "canWithdraw" }.Select(f => asClient.GetProperty(f).GetBoolean()));
    }

    [Fact]
    public async Task The_practitioner_sees_the_client_as_an_initial_and_the_client_sees_the_practitioners_name()
    {
        await using var h = new ProposalHost(new CreateHost { NameClaim = "anna@firma.pl" });
        var id = await h.Create();
        var asPractitioner = await ViewOf(h.AsPractitioner(), id);
        Assert.Equal("Klient A.", asPractitioner.GetProperty("client").GetProperty("displayName").GetString());
        Assert.DoesNotContain("anna", asPractitioner.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var asClient = await ViewOf(h.AsClient(), id);
        Assert.Equal("Demo Practitioner", asClient.GetProperty("practitioner").GetProperty("displayName").GetString());
        Assert.Equal("demo-practitioner", asClient.GetProperty("practitioner").GetProperty("slug").GetString());
    }

    [Fact]
    public async Task The_company_name_is_absent_when_none_was_given()
    {
        await using var h = new ProposalHost();
        var id = await h.Create();
        Assert.False((await ViewOf(h.AsPractitioner(), id)).TryGetProperty("companyName", out var c) && c.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task No_address_is_shown_before_acceptance_in_any_state()
    {
        await using var h = new ProposalHost();
        var open = await h.Create();
        var rejected = await h.Create(h.AsClient(ProposalHost.OtherClient));
        await ProposalHost.Act(h.AsPractitioner(), rejected, "reject", new { round = 1 });
        foreach (var (client, id) in new[] { (h.AsClient(), open), (h.AsPractitioner(), open), (h.AsPractitioner(), rejected), (h.AsClient(ProposalHost.OtherClient), rejected) })
        {
            var raw = (await ViewOf(client, id)).GetRawText();
            Assert.DoesNotContain("@demo.invalid", raw);
            Assert.False(JsonDocument.Parse(raw).RootElement.TryGetProperty("contact", out var c) && c.ValueKind != JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task After_acceptance_each_party_sees_the_other_ones_address()
    {
        await using var h = new ProposalHost();
        var id = await h.Create();
        await ProposalHost.Act(h.AsPractitioner(), id, "accept", new { round = 1 });
        Assert.Equal("demo-practitioner@demo.invalid", (await ViewOf(h.AsClient(), id)).GetProperty("contact").GetProperty("email").GetString());
        Assert.Equal("demo-client-1@demo.invalid", (await ViewOf(h.AsPractitioner(), id)).GetProperty("contact").GetProperty("email").GetString());
    }

    [Fact]
    public async Task When_Directory_holds_no_address_the_contact_is_there_but_empty()
    {
        await using var h = new ProposalHost { PractitionerEmail = null };
        var id = await h.Create();
        await ProposalHost.Act(h.AsPractitioner(), id, "accept", new { round = 1 });
        var contact = (await ViewOf(h.AsClient(), id)).GetProperty("contact");
        Assert.True(!contact.TryGetProperty("email", out var e) || e.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task A_booking_style_stranger_never_learns_whether_a_proposal_exists()
    {
        await using var h = new ProposalHost();
        var id = await h.Create();
        var real = await ProposalHost.View(h.AsClient(ProposalHost.Stranger), id);
        var none = await ProposalHost.View(h.AsClient(ProposalHost.Stranger), Guid.NewGuid());
        Assert.Equal(real.StatusCode, none.StatusCode);
        Assert.Equal(await real.Content.ReadAsStringAsync(), await none.Content.ReadAsStringAsync());
    }

    // ---- the list

    static async Task<(List<JsonElement> Items, string? Next)> List(HttpClient c, string query)
    {
        var r = await c.GetAsync("/api/v1/me/proposals" + query);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var b = await r.Body();
        return (b.GetProperty("items").EnumerateArray().ToList(), b.TryGetProperty("nextCursor", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null);
    }

    [Fact]
    public async Task The_list_is_the_callers_own_rows_for_one_role()
    {
        await using var h = new ProposalHost();
        var mine = await h.Create();
        var theirs = await h.Create(h.AsClient(ProposalHost.OtherClient));
        var asClient = (await List(h.AsClient(), "?role=client")).Items.Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(mine, asClient); Assert.DoesNotContain(theirs, asClient);
        var asPractitioner = (await List(h.AsPractitioner(), "?role=practitioner")).Items.Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(mine, asPractitioner); Assert.Contains(theirs, asPractitioner);
        Assert.DoesNotContain((await List(h.AsPractitioner(), "?role=client")).Items, i => i.GetProperty("id").GetGuid() == mine);
        Assert.Empty((await List(h.AsClient(ProposalHost.Stranger), "?role=client")).Items);
    }

    [Fact]
    public async Task A_summary_says_whose_turn_it_is_and_carries_no_text_or_address()
    {
        await using var h = new ProposalHost();
        var id = await h.Create(body: h.Req(company: "Demo Sp. z o.o."));
        var forPractitioner = (await List(h.AsPractitioner(), "?role=practitioner")).Items.Single(i => i.GetProperty("id").GetGuid() == id);
        var forClient = (await List(h.AsClient(), "?role=client")).Items.Single(i => i.GetProperty("id").GetGuid() == id);
        Assert.True(forPractitioner.GetProperty("yourTurn").GetBoolean());
        Assert.False(forClient.GetProperty("yourTurn").GetBoolean());
        Assert.Equal("Demo Sp. z o.o.", forPractitioner.GetProperty("companyName").GetString());
        foreach (var item in new[] { forPractitioner, forClient })
        {
            Assert.False(item.TryGetProperty("rounds", out _));
            Assert.DoesNotContain("Szukamy", item.GetRawText());
            Assert.DoesNotContain("@demo.invalid", item.GetRawText());
        }
    }

    [Fact]
    public async Task The_state_filter_separates_open_from_closed()
    {
        await using var h = new ProposalHost();
        var open = await h.Create();
        var closed = await h.Create(h.AsClient(ProposalHost.OtherClient));
        await ProposalHost.Act(h.AsPractitioner(), closed, "reject", new { round = 1 });
        var openIds = (await List(h.AsPractitioner(), "?role=practitioner&state=open")).Items.Select(i => i.GetProperty("id").GetGuid()).ToList();
        var closedIds = (await List(h.AsPractitioner(), "?role=practitioner&state=closed")).Items.Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(open, openIds); Assert.DoesNotContain(closed, openIds);
        Assert.Contains(closed, closedIds); Assert.DoesNotContain(open, closedIds);
    }

    [Fact]
    public async Task The_most_recent_activity_comes_first_and_paging_walks_every_row_once()
    {
        // its own practitioner user: the in-memory database is shared by name, and the demo practitioner of the other tests has hundreds of proposals
        await using var h = new ProposalHost(new CreateHost { OwnerUserId = "demo-practitioner-" + Guid.NewGuid().ToString("n") });
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add(await h.Create(h.AsClient($"demo-paging-{i}")));
            h.Host.Clock.Advance(TimeSpan.FromMinutes(1));
        }
        await ProposalHost.Counter(h.AsPractitioner(), ids[0], 1); // the oldest proposal had the latest activity
        var seen = new List<Guid>(); string? cursor = null; var pages = 0;
        do
        {
            var (items, next) = await List(h.AsPractitioner(), "?role=practitioner&limit=2" + (cursor is null ? "" : "&cursor=" + cursor));
            seen.AddRange(items.Select(i => i.GetProperty("id").GetGuid()).Where(ids.Contains));
            cursor = next; pages++;
            Assert.InRange(pages, 1, 20);
        } while (cursor is not null);
        Assert.Equal(ids.Count, seen.Count);
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(ids[0], seen[0]);
        Assert.Equal(ids[4], seen[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?role=admin")]
    [InlineData("?role=client&state=maybe")]
    [InlineData("?role=client&limit=0")]
    [InlineData("?role=client&limit=25")]
    [InlineData("?role=client&limit=abc")]
    [InlineData("?role=client&cursor=garbage")]
    public async Task A_bad_query_is_400(string query)
    {
        await using var h = new ProposalHost();
        var r = await h.AsClient().GetAsync("/api/v1/me/proposals" + query);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("validation_failed", (await r.Body()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_cursor_belongs_to_the_listing_it_was_made_for()
    {
        await using var h = new ProposalHost();
        for (var i = 0; i < 3; i++) await h.Create(h.AsClient($"demo-cursor-{i}"));
        var (_, next) = await List(h.AsPractitioner(), "?role=practitioner&limit=1");
        Assert.NotNull(next);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.AsPractitioner().GetAsync($"/api/v1/me/proposals?role=client&limit=1&cursor={next}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.AsPractitioner().GetAsync($"/api/v1/me/proposals?role=practitioner&state=open&limit=1&cursor={next}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.AsPractitioner().GetAsync($"/api/v1/me/proposals?role=practitioner&limit=1&cursor={next}x")).StatusCode);
    }

    [Fact]
    public async Task The_list_needs_a_signed_in_user()
    {
        await using var h = new ProposalHost();
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Host.ClientFor(null).GetAsync("/api/v1/me/proposals?role=client")).StatusCode);
    }

    [Fact]
    public async Task When_Directory_is_down_the_view_and_the_list_still_render_with_the_fallback_name()
    {
        await using var h = new ProposalHost();
        var id = await h.Create();
        h.Host.DirectoryDown = true; // the names lookup answers 503
        var v = await ViewOf(h.AsClient(), id);
        Assert.Equal("Programista", v.GetProperty("practitioner").GetProperty("displayName").GetString());
        Assert.Contains(id, (await List(h.AsClient(), "?role=client")).Items.Select(i => i.GetProperty("id").GetGuid()));
    }
}
