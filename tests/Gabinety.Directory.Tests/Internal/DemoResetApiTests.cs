using System.Net;
using System.Text.Json;
using PractitionerStatus = Gabinety.Contracts.Directory.PractitionerStatus;
using Gabinety.Directory.Features.Offerings;
using Gabinety.Directory.Features.Profiles;
using Gabinety.Directory.Features.Verification;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Directory.Tests.Internal;

public class DemoResetApiTests
{
    const string Url = "/internal/v1/demo/reset";
    static readonly string[] Tables = ["practitioners", "offerings", "verificationEvents"];

    sealed record Chain(Guid Practitioner, Guid Offering, Guid Event);

    static async Task<Chain> SeedChain(DemoResetHost h, string slug, string userId)
    {
        var now = DateTimeOffset.UtcNow;
        var p = new Practitioner
        {
            Id = Guid.NewGuid(), UserId = userId, Slug = slug, DisplayName = "Demo", Headline = "h", Status = PractitionerStatus.Approved,
            CreatedAt = now, UpdatedAt = now, RatingCount = 3, RatingAvg = 4.5m, CompletedCount = 2, OpenSlots14d = 4,
        };
        var o = new Offering
        {
            Id = Guid.NewGuid(), PractitionerId = p.Id, Title = "Demo review", Description = "demo", DurationMinutes = 60, PriceMinor = 15000, Currency = "PLN",
            ProblemSlug = "code-review", TechSlugs = ["dotnet"], IsActive = true, CreatedAt = now,
        };
        var e = new VerificationEvent { Id = Guid.NewGuid(), PractitionerId = p.Id, Kind = VerificationEventKind.Approved, ActorUserId = "demo-admin", At = now };
        await h.Db(async db => { db.Practitioners.Add(p); db.Offerings.Add(o); db.VerificationEvents.Add(e); await db.SaveChangesAsync(); return 0; });
        return new(p.Id, o.Id, e.Id);
    }

    static async Task<(int p, int o, int e)> Counts(DemoResetHost h) => await h.Db(async db =>
        (await db.Practitioners.CountAsync(), await db.Offerings.CountAsync(), await db.VerificationEvents.CountAsync()));

    static Task<HttpResponseMessage> Reset(DemoResetHost h, string? key = DemoResetHost.Key) => h.Internal(key).PostAsync(Url, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

    static async Task<JsonElement> Body(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Reset_deletes_demo_slug_practitioners_with_their_offerings_and_events_and_keeps_every_other_row()
    {
        await using var h = DemoResetHost.Fake();
        var tag = Guid.NewGuid().ToString("n")[..8];
        await SeedChain(h, "demo-a-" + tag, "opaque-user-a");
        await SeedChain(h, "demo-b-" + tag, "opaque-user-b");
        var other = await SeedChain(h, "real-" + tag, "demo-user-of-a-real-slug"); // a demo- USER id does not put a row in the namespace: the slug does

        var r = await Reset(h);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await Body(r);
        Assert.Equal(2, j.GetProperty("practitioners").GetInt32());
        Assert.Equal(2, j.GetProperty("offerings").GetInt32());
        Assert.Equal(2, j.GetProperty("verificationEvents").GetInt32());

        Assert.Equal((1, 1, 1), await Counts(h));
        Assert.Equal(other.Practitioner, await h.Db(db => db.Practitioners.Select(p => p.Id).SingleAsync()));
        Assert.Equal(other.Offering, await h.Db(db => db.Offerings.Select(p => p.Id).SingleAsync()));
        Assert.Equal(other.Event, await h.Db(db => db.VerificationEvents.Select(p => p.Id).SingleAsync()));
    }

    [Fact]
    public async Task A_non_demo_practitioner_is_never_touched_even_when_the_only_row_present()
    {
        await using var h = DemoResetHost.Fake();
        await SeedChain(h, "real-" + Guid.NewGuid().ToString("n")[..8], "real-user");
        var j = await Body(await Reset(h));
        foreach (var t in Tables) Assert.Equal(0, j.GetProperty(t).GetInt32());
        Assert.Equal((1, 1, 1), await Counts(h));
    }

    [Fact]
    public async Task A_lookalike_that_does_not_start_with_demo_dash_is_kept()
    {
        await using var h = DemoResetHost.Fake();
        var tag = Guid.NewGuid().ToString("n")[..8];
        await SeedChain(h, "xdemo-" + tag, "u1");
        await SeedChain(h, "Demo-" + tag, "u2"); // the namespace is lower case
        Assert.Equal(0, (await Body(await Reset(h))).GetProperty("practitioners").GetInt32());
        Assert.Equal((2, 2, 2), await Counts(h));
    }

    [Fact]
    public async Task Reset_is_idempotent_the_second_call_deletes_nothing()
    {
        await using var h = DemoResetHost.Fake();
        await SeedChain(h, "demo-" + Guid.NewGuid().ToString("n")[..8], "u1");
        Assert.Equal(1, (await Body(await Reset(h))).GetProperty("practitioners").GetInt32());
        var second = await Reset(h);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var j = await Body(second);
        foreach (var t in Tables) Assert.Equal(0, j.GetProperty(t).GetInt32());
    }

    [Fact]
    public async Task Development_kind_also_maps_the_endpoint()
    {
        await using var h = DemoResetHost.Kind("Development");
        Assert.Equal(HttpStatusCode.OK, (await Reset(h)).StatusCode);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Outside_the_fake_profile_the_endpoint_is_not_mapped_and_deletes_nothing(string kind)
    {
        await using var h = DemoResetHost.Kind(kind);
        await SeedChain(h, "demo-" + Guid.NewGuid().ToString("n")[..8], "u1");
        Assert.Equal(HttpStatusCode.NotFound, (await Reset(h)).StatusCode);
        Assert.Equal((1, 1, 1), await Counts(h));
    }

    [Fact]
    public async Task With_no_profile_settings_at_all_the_endpoint_is_not_mapped()
    {
        await using var h = DemoResetHost.Bare();
        Assert.Equal(HttpStatusCode.NotFound, (await Reset(h)).StatusCode);
    }

    [Fact]
    public async Task Fake_flag_other_than_1_does_not_map_the_endpoint()
    {
        await using var h = new DemoResetHost(new Dictionary<string, string> { ["GABINETY_FAKE"] = "0" });
        Assert.Equal(HttpStatusCode.NotFound, (await Reset(h)).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("demo-wrong-key")]
    public async Task A_missing_or_wrong_internal_key_is_401_and_deletes_nothing(string? key)
    {
        await using var h = DemoResetHost.Fake();
        await SeedChain(h, "demo-" + Guid.NewGuid().ToString("n")[..8], "u1");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Reset(h, key)).StatusCode);
        Assert.Equal((1, 1, 1), await Counts(h));
    }

    [Fact]
    public async Task A_user_bearer_token_is_not_an_internal_credential()
    {
        await using var h = DemoResetHost.Fake();
        var r = await h.ClientFor("demo-user-1").PostAsync(Url, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}
