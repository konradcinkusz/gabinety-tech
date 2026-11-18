using System.Net;
using System.Text.Json;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Gabinety.Reputation.Tests.Analytics;

/// <summary>
/// PostgreSQL proof (CI `integration` job, needs Docker) of the SQL Reputation sends for the administrator's list (T-10.03): the review lookup is
/// <c>client_user_id = ANY(@clients) AND practitioner_id = ANY(@practitioners) AND status &lt;&gt; 'Rejected'</c>, with status stored as text. The InMemory tests cannot show that
/// the enum comparison and the two list parameters translate. Booking's side is a stub here; its own SQL is proved in Booking's <c>PairCountsPostgresTests</c>.
/// </summary>
[Trait("tier", "integration")]
public class SuspiciousPairsPostgresTests
{
    const string Url = "/api/v1/admin/suspicious-pairs";

    static async Task<AnalyticsHost> StartAsync(string cs)
    {
        var h = new AnalyticsHost();
        h.Settings["Database:Provider"] = "PostgreSQL";
        h.Settings["ConnectionStrings:reputationdb"] = cs;
        h.CreateClient();
        var state = h.Services.GetRequiredService<MigrationState>();
        await state.WaitForCompletionAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(MigrationStatus.Done, state.Status);
        return h;
    }

    [Fact]
    public async Task Lists_only_the_pair_whose_live_reviews_are_all_five_stars()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        using var h = await StartAsync(pg.GetConnectionString());
        var p = Guid.NewGuid();
        var longClient = "demo-pgp-long-" + new string('x', 36); // 50 characters (the column is varchar(64)): with the practitioner id the key is over 80, so Booking sends the hashed "h:" key; Reputation passes the key through untouched
        await h.Seed(
            AnalyticsHost.NewReview("demo-pgp-farmer", p, 5), AnalyticsHost.NewReview("demo-pgp-farmer", p, 5), AnalyticsHost.NewReview("demo-pgp-farmer", p, 5),
            AnalyticsHost.NewReview("demo-pgp-farmer", p, 4, ReviewStatus.Rejected), // a rejected review neither lists nor clears
            AnalyticsHost.NewReview("demo-pgp-held", p, 5, ReviewStatus.Held),
            AnalyticsHost.NewReview("demo-pgp-lone4", p, 4),
            AnalyticsHost.NewReview("demo-pgp-mixed", p, 5), AnalyticsHost.NewReview("demo-pgp-mixed", p, 4),
            AnalyticsHost.NewReview(longClient, p, 5));
        var hashed = "h:" + new string('a', 64);
        h.Booking.Add(
            AnalyticsHost.Pair("demo-pgp-farmer", p, 3), AnalyticsHost.Pair("demo-pgp-held", p, 4), AnalyticsHost.Pair("demo-pgp-lone4", p, 3),
            AnalyticsHost.Pair("demo-pgp-mixed", p, 3), AnalyticsHost.Pair("demo-pgp-silent", p, 3),
            new Gabinety.Contracts.Booking.PairCountDto(hashed, longClient, p, 3));

        var r = await h.As("demo-admin", "Admin").GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var items = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["demo-pgp-farmer", "demo-pgp-held", longClient], items.Select(i => i.GetProperty("clientUserId").GetString()!).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(3, items.Single(i => i.GetProperty("clientUserId").GetString() == "demo-pgp-farmer").GetProperty("reviews").GetInt32()); // the rejected 4 is not counted
        Assert.Equal(hashed, items.Single(i => i.GetProperty("clientUserId").GetString() == longClient).GetProperty("pairKey").GetString());
    }
}
