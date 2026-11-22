using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.DemoSeeder;

namespace Gabinety.Integration.Tests.Seeder;

/// <summary>
/// ADR proposal 0013: the contract availability of four demo programmers (Directory step) and the three negotiations of the proposals step, against in-process Identity, Directory and Booking
/// (InMemory). Expected values are written here, not read from the plan. Everything is read through the public API, as a person would.
/// </summary>
public sealed class ProposalSeedFixture : IAsyncLifetime
{
    public BookingWorld World { get; } = new();

    public async Task InitializeAsync()
    {
        await World.SeedDirectoryAsync();
        await new ProposalSeedStep().RunAsync(World.Context, CancellationToken.None);
    }

    public async Task DisposeAsync() => await World.DisposeAsync();
}

public class ProposalSeedStepTests(ProposalSeedFixture fx) : IClassFixture<ProposalSeedFixture>
{
    BookingWorld W => fx.World;

    async Task<HttpClient> As(string user)
    {
        var http = W.Booking.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", await W.TokenAsync(user));
        return http;
    }

    async Task<List<JsonElement>> Sent(string client)
    {
        using var http = await As(client);
        return (await http.GetFromJsonAsync<JsonElement>("/api/v1/me/proposals?role=client&limit=24")).GetProperty("items").EnumerateArray().Select(i => i.Clone()).ToList();
    }

    async Task<JsonElement> View(string user, Guid id)
    {
        using var http = await As(user);
        return await http.GetFromJsonAsync<JsonElement>($"/api/v1/proposals/{id}");
    }

    [Fact]
    public async Task Four_programmers_advertise_contract_availability_and_the_others_do_not()
    {
        using var http = W.Directory.CreateClient();
        var cards = (await http.GetFromJsonAsync<JsonElement>("/api/v1/practitioners?limit=24")).GetProperty("items").EnumerateArray().ToDictionary(c => c.GetProperty("slug").GetString()!, c => c.Clone());
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var anna = cards["demo-anna-demowska"].GetProperty("contract");
        Assert.False(anna.TryGetProperty("availableFrom", out _)); // available now: no date
        Assert.Equal((20, 15000), (anna.GetProperty("hoursPerWeek").GetInt32(), anna.GetProperty("rate").GetProperty("amountMinor").GetInt32()));

        var bartosz = cards["demo-bartosz-testowy"].GetProperty("contract");
        Assert.True(DateOnly.Parse(bartosz.GetProperty("availableFrom").GetString()!) >= today.AddDays(88)); // about three months ahead
        Assert.Equal((12, 13000), (bartosz.GetProperty("hoursPerWeek").GetInt32(), bartosz.GetProperty("rate").GetProperty("amountMinor").GetInt32()));

        var celina = cards["demo-celina-przykladowska"].GetProperty("contract");
        Assert.Equal(8, celina.GetProperty("hoursPerWeek").GetInt32());
        Assert.False(celina.TryGetProperty("rate", out _)); // no indicative rate

        var ewa = cards["demo-ewa-wzorcowa"].GetProperty("contract");
        Assert.True(DateOnly.Parse(ewa.GetProperty("availableFrom").GetString()!) >= today.AddDays(28));

        Assert.All(cards.Where(c => c.Key is not ("demo-anna-demowska" or "demo-bartosz-testowy" or "demo-celina-przykladowska" or "demo-ewa-wzorcowa")),
            c => Assert.False(c.Value.TryGetProperty("contract", out _), c.Key));
    }

    [Fact]
    public async Task Three_clients_have_one_proposal_each_in_three_different_states()
    {
        var open = Assert.Single(await Sent("demo-client"));
        var countered = Assert.Single(await Sent("demo-client-2"));
        var accepted = Assert.Single(await Sent("demo-client-3"));
        Assert.Empty(await Sent("demo-client-4"));
        Assert.Equal(("AwaitingPractitioner", "LongTerm", true), (open.GetProperty("status").GetString(), open.GetProperty("kind").GetString(), !open.GetProperty("yourTurn").GetBoolean()));
        Assert.Equal(("AwaitingClient", "LongTerm", true), (countered.GetProperty("status").GetString(), countered.GetProperty("kind").GetString(), countered.GetProperty("yourTurn").GetBoolean()));
        Assert.Equal(("Accepted", "ShortTerm"), (accepted.GetProperty("status").GetString(), accepted.GetProperty("kind").GetString()));
        Assert.Equal("Demo Sp. z o.o.", open.GetProperty("companyName").GetString());
        Assert.False(countered.TryGetProperty("companyName", out var c) && c.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task The_countered_negotiation_has_two_rounds_with_the_programmers_terms_and_the_accepted_one_shows_the_address()
    {
        var countered = (await Sent("demo-client-2")).Single();
        var view = await View("demo-client-2", countered.GetProperty("id").GetGuid());
        var rounds = view.GetProperty("rounds").EnumerateArray().ToList();
        Assert.Equal(["Proposed", "Countered"], rounds.Select(r => r.GetProperty("action").GetString()));
        Assert.Equal(["Client", "Practitioner"], rounds.Select(r => r.GetProperty("by").GetString()));
        Assert.Equal(13000, rounds[0].GetProperty("terms").GetProperty("rate").GetProperty("amountMinor").GetInt32());
        Assert.Equal((14000, 10), (view.GetProperty("terms").GetProperty("rate").GetProperty("amountMinor").GetInt32(), view.GetProperty("terms").GetProperty("hoursPerWeek").GetInt32()));
        Assert.True(view.GetProperty("canAccept").GetBoolean()); // the client's turn
        Assert.False(view.TryGetProperty("contact", out var contact) && contact.ValueKind != JsonValueKind.Null);

        var accepted = (await Sent("demo-client-3")).Single();
        var done = await View("demo-client-3", accepted.GetProperty("id").GetGuid());
        Assert.Equal("Accepted", done.GetProperty("status").GetString());
        Assert.Equal(2, done.GetProperty("rounds").GetArrayLength());
        Assert.False(done.GetProperty("canAccept").GetBoolean() || done.GetProperty("canWithdraw").GetBoolean());
        Assert.True(done.TryGetProperty("contact", out _)); // closed by acceptance: the contact section exists (the address itself depends on the practitioner's snapshot)
    }

    [Fact]
    public async Task The_programmers_see_their_inbox_with_whose_turn_it_is()
    {
        using var anna = await As("demo-practitioner-1");
        var inbox = (await anna.GetFromJsonAsync<JsonElement>("/api/v1/me/proposals?role=practitioner&limit=24")).GetProperty("items").EnumerateArray().ToList();
        var row = Assert.Single(inbox);
        Assert.True(row.GetProperty("yourTurn").GetBoolean());
        Assert.StartsWith("Klient", row.GetProperty("client").GetProperty("displayName").GetString());
        Assert.Equal("Demo Sp. z o.o.", row.GetProperty("companyName").GetString());
    }

    [Fact]
    public async Task A_second_run_creates_and_changes_nothing_and_the_reset_removes_everything()
    {
        await using var world = new BookingWorld();
        await world.SeedDirectoryAsync();
        var step = new ProposalSeedStep();
        await step.RunAsync(world.Context, CancellationToken.None);
        var posts = world.Calls.Requests.Count(r => r.Method == HttpMethod.Post && r.Path.StartsWith("/api/v1/proposals"));
        Assert.Equal(3 + 1 + 1, posts); // three creates, one counter, one accept

        await step.RunAsync(world.Context, CancellationToken.None);
        Assert.Equal(posts, world.Calls.Requests.Count(r => r.Method == HttpMethod.Post && r.Path.StartsWith("/api/v1/proposals"))); // nothing was written again
        Assert.Contains("proposals: created=0 unchanged=3", world.Log.Text);

        // Booking's reset covers the proposals (the proposals step has no reset of its own)
        Assert.Equal(-1, await step.ResetAsync(world.Context, CancellationToken.None));
        await new BookingSeedStep().ResetAsync(world.Context, CancellationToken.None);
        using var http = world.Booking.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", await world.TokenAsync("demo-client"));
        Assert.Empty((await http.GetFromJsonAsync<JsonElement>("/api/v1/me/proposals?role=client")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public void The_plan_uses_only_demo_names_and_text_the_service_accepts()
    {
        Assert.All(ProposalPlan.All, t =>
        {
            Assert.True(DemoNames.IsDemo(t.Client) && DemoNames.IsDemo(t.Practitioner));
            Assert.InRange(t.Text.Length, 20, 1000);
            Assert.DoesNotContain("@", t.Text + t.Company + t.CounterText);
            Assert.DoesNotMatch(@"\d{7,}", t.Text);
        });
        Assert.Equal([ProposalOutcome.Open, ProposalOutcome.Countered, ProposalOutcome.Accepted], ProposalPlan.All.Select(t => t.Outcome));
        Assert.Equal(ProposalPlan.All.Length, ProposalPlan.All.Select(t => t.Client).Distinct().Count()); // one proposal per client: the per-user limit is never near
    }
}
