using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Contracts.Directory;

namespace Gabinety.Directory.Tests.Internal;

/// <summary>`nextFreeSlotUtc` on `PUT /internal/v1/practitioners/{id}/booking-stats` (CONTRACTS 3.6, T-11.03).</summary>
public class InternalNextSlotTests(InternalFactory f) : IClassFixture<InternalFactory>
{
    static readonly DateTimeOffset Slot = new(2026, 12, 1, 9, 30, 0, TimeSpan.Zero);

    async Task<Gabinety.Directory.Features.Profiles.Practitioner> Listed()
    {
        var p = await f.Seed(PractitionerStatus.Approved, x => { x.Headline = "demo"; x.RankingScore = 1m; x.RankingVersion = "old"; x.ApprovedAt = f.Clock.GetUtcNow(); });
        await f.SeedOffering(p);
        return p;
    }

    static Task<HttpResponseMessage> Put(HttpClient c, Guid id, long version, object? nextFreeSlotUtc, bool include = true) =>
        c.PutAsJsonAsync($"/internal/v1/practitioners/{id}/booking-stats",
            include
                ? new Dictionary<string, object?> { ["completedCount"] = 2, ["practitionerCancelCount"] = 0, ["noShowUpheldCount"] = 0, ["openSlots14d"] = 5, ["version"] = version, ["nextFreeSlotUtc"] = nextFreeSlotUtc }
                : new Dictionary<string, object?> { ["completedCount"] = 2, ["practitionerCancelCount"] = 0, ["noShowUpheldCount"] = 0, ["openSlots14d"] = 5, ["version"] = version },
            Ik.J);

    /// <summary>The card of the practitioner as the public list serves it (found by its unique display name).</summary>
    async Task<JsonElement> Card(Gabinety.Directory.Features.Profiles.Practitioner p)
    {
        var r = await f.Anon().GetAsync("/api/v1/practitioners?q=" + Uri.EscapeDataString(p.DisplayName));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == p.Id).Clone();
    }

    [Fact]
    public async Task A_push_with_a_next_free_slot_stores_it_and_the_card_shows_it()
    {
        var p = await Listed();
        Assert.False((await Card(p)).TryGetProperty("nextFreeSlotUtc", out _));
        Assert.Equal(HttpStatusCode.NoContent, (await Put(f.Internal(), p.Id, 1, Slot)).StatusCode);
        Assert.Equal(Slot, (await f.Load(p.Id)).NextFreeSlotUtc);
        Assert.Equal("2026-12-01T09:30:00Z", (await Card(p)).GetProperty("nextFreeSlotUtc").GetString());
    }

    [Fact]
    public async Task An_offset_instant_is_stored_as_the_same_instant_in_utc()
    {
        var p = await Listed();
        Assert.Equal(HttpStatusCode.NoContent, (await Put(f.Internal(), p.Id, 1, "2026-12-01T10:30:00+01:00")).StatusCode);
        var stored = (await f.Load(p.Id)).NextFreeSlotUtc;
        Assert.Equal(Slot, stored);
        Assert.Equal(TimeSpan.Zero, stored!.Value.Offset);
    }

    [Theory]
    [InlineData(false)] // the field is omitted: what an old Booking sends
    [InlineData(true)]  // the field is an explicit null
    public async Task A_later_push_without_a_next_free_slot_clears_it(bool explicitNull)
    {
        var p = await Listed();
        await Put(f.Internal(), p.Id, 1, Slot);
        Assert.Equal(HttpStatusCode.NoContent, (await Put(f.Internal(), p.Id, 2, null, include: explicitNull)).StatusCode);
        var a = await f.Load(p.Id);
        Assert.Null(a.NextFreeSlotUtc);
        Assert.Equal(2, a.StatsVersion);
        Assert.False((await Card(p)).TryGetProperty("nextFreeSlotUtc", out _));
    }

    [Theory]
    [InlineData(3)] // equal
    [InlineData(2)] // lower
    public async Task A_stale_push_changes_nothing_including_the_next_free_slot(long version)
    {
        var p = await Listed();
        await Put(f.Internal(), p.Id, 3, Slot);
        Assert.Equal(HttpStatusCode.NoContent, (await Put(f.Internal(), p.Id, version, null)).StatusCode);       // would clear it if it applied
        Assert.Equal(HttpStatusCode.NoContent, (await Put(f.Internal(), p.Id, version, Slot.AddDays(3))).StatusCode);
        var a = await f.Load(p.Id);
        Assert.Equal(Slot, a.NextFreeSlotUtc);
        Assert.Equal(3, a.StatsVersion);
    }

    [Fact]
    public async Task An_old_Booking_pushing_the_old_body_still_gets_204_and_every_other_field_is_applied()
    {
        var p = await Listed();
        var r = await f.Internal().PutAsJsonAsync($"/internal/v1/practitioners/{p.Id}/booking-stats",
            new { completedCount = 20, practitionerCancelCount = 1, noShowUpheldCount = 0, openSlots14d = 10, version = 7 }, Ik.J);   // the body before T-11.03, byte for byte
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        var a = await f.Load(p.Id);
        Assert.Equal((20, 1, 0, 10, 7L), (a.CompletedCount, a.PractitionerCancelCount, a.NoShowUpheldCount, a.OpenSlots14d, a.StatsVersion));
        Assert.Equal(0.50m, a.ProfileCompleteness);   // headline 0.15 + an active offering 0.25 + open slots 0.10 (CONTRACTS 3.5)
        Assert.Null(a.NextFreeSlotUtc);
        Assert.Equal(HttpStatusCode.OK, (await f.Anon().GetAsync("/api/v1/practitioners?q=" + Uri.EscapeDataString(p.DisplayName))).StatusCode);
    }

    [Fact]
    public async Task A_next_free_slot_does_not_change_the_ranking_or_the_other_stats()
    {
        var p = await Listed();
        await Put(f.Internal(), p.Id, 1, null);
        var without = await f.Load(p.Id);
        await Put(f.Internal(), p.Id, 2, Slot);
        var with = await f.Load(p.Id);
        Assert.Equal((without.RankingScore, without.ProfileCompleteness, without.OpenSlots14d), (with.RankingScore, with.ProfileCompleteness, with.OpenSlots14d));
    }
}
