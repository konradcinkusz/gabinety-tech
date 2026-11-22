using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Booking.Notifications;
using Gabinety.Booking.Notifications.Preferences;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Tests;

/// <summary>T-11.12, GET and PUT /api/v1/me/notification-preferences (CONTRACTS 4.2): three switchable kinds, defaulting to enabled, per user, transactional kinds refused.</summary>
public class NotificationPreferencesApiTests : IDisposable
{
    const string Path = "/api/v1/me/notification-preferences";
    readonly CreateHost h = new();
    // The in-memory database is shared by the hosts of a test run, so each test owns its users.
    readonly string me = "demo-user-" + Guid.NewGuid().ToString("N"), ua = "demo-user-" + Guid.NewGuid().ToString("N"), ub = "demo-user-" + Guid.NewGuid().ToString("N");

    public void Dispose() => h.Dispose();

    static object Put(params (string Kind, bool Enabled)[] items) => new { items = items.Select(i => new { kind = i.Kind, enabled = i.Enabled }) };

    static async Task<List<(string Kind, bool Enabled)>> Items(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Body()).GetProperty("items").EnumerateArray().Select(i => (i.GetProperty("kind").GetString()!, i.GetProperty("enabled").GetBoolean())).ToList();
    }

    Task<List<NotificationPreference>> Rows(string userId) =>
        h.Query(db => db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == userId).ToListAsync());

    [Fact]
    public async Task Without_a_token_both_verbs_are_401()
    {
        var anon = h.ClientFor(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PutAsJsonAsync(Path, Put(("Reminder24h", false)))).StatusCode);
    }

    [Fact]
    public async Task A_user_with_no_rows_gets_the_three_switchable_kinds_all_enabled()
    {
        var items = await Items(await h.ClientFor(me).GetAsync(Path));

        Assert.Equal([("Reminder24h", true), ("Reminder1h", true), ("ReviewRequest", true)], items);
    }

    [Fact]
    public async Task Switching_a_kind_off_is_returned_by_the_PUT_and_by_a_later_GET_and_leaves_the_other_kinds_alone()
    {
        var c = h.ClientFor(me);

        var put = await Items(await c.PutAsJsonAsync(Path, Put(("Reminder24h", false))));
        var get = await Items(await c.GetAsync(Path));

        var expected = new List<(string, bool)> { ("Reminder24h", false), ("Reminder1h", true), ("ReviewRequest", true) };
        Assert.Equal(expected, put);
        Assert.Equal(expected, get);
        var row = Assert.Single(await Rows(me));
        Assert.Equal((NotificationKind.Reminder24h, false), (row.Kind, row.Enabled));
    }

    [Fact]
    public async Task Switching_it_back_on_updates_the_same_row_and_a_kind_left_out_keeps_its_value()
    {
        var c = h.ClientFor(me);
        await c.PutAsJsonAsync(Path, Put(("Reminder24h", false), ("ReviewRequest", false)));

        var items = await Items(await c.PutAsJsonAsync(Path, Put(("Reminder24h", true))));

        Assert.Equal([("Reminder24h", true), ("Reminder1h", true), ("ReviewRequest", false)], items);
        Assert.Equal(2, (await Rows(me)).Count);
    }

    [Fact]
    public async Task One_users_choice_never_reaches_another_user()
    {
        var a = h.ClientFor(ua);
        var b = h.ClientFor(ub);
        await a.PutAsJsonAsync(Path, Put(("Reminder1h", false)));

        Assert.All(await Items(await b.GetAsync(Path)), i => Assert.True(i.Enabled));
        await b.PutAsJsonAsync(Path, Put(("Reminder24h", false)));

        Assert.Equal([("Reminder24h", true), ("Reminder1h", false), ("ReviewRequest", true)], await Items(await a.GetAsync(Path)));
        Assert.Equal(["Reminder24h"], (await Rows(ub)).Select(r => r.Kind.ToString()));
    }

    [Theory]
    [InlineData("BookingConfirmed")]
    [InlineData("Rescheduled")]
    [InlineData("Cancelled")]
    [InlineData("DisputeOpened")]
    [InlineData("DisputeResolved")]
    [InlineData("PayoutSent")]
    [InlineData("Unknown")]
    [InlineData("reminder24h")]
    [InlineData("1")]
    [InlineData("")]
    public async Task A_kind_that_is_not_switchable_is_400_validation_failed_and_nothing_is_written(string kind)
    {
        var c = h.ClientFor(me);

        // The valid first item must not be saved when the second one is refused.
        var r = await c.PutAsJsonAsync(Path, Put(("Reminder1h", false), (kind, false)));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var body = await r.Body();
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
        Assert.Contains("items[1].kind", body.GetProperty("errors").EnumerateObject().Select(p => p.Name));
        Assert.Empty(await Rows(me));
    }

    [Theory]
    [InlineData("""{ "items": [] }""")]
    [InlineData("""{ "items": null }""")]
    [InlineData("""{ "items": [null] }""")]
    [InlineData("""{ "items": [{ "kind": null, "enabled": false }] }""")]
    [InlineData("""{ "items": [{ "kind": "Reminder24h", "enabled": false }, { "kind": "Reminder24h", "enabled": true }] }""")]
    [InlineData("""{}""")]
    public async Task An_empty_missing_null_or_repeated_item_list_is_400_validation_failed_and_nothing_is_written(string json)
    {
        var c = h.ClientFor(me);

        var r = await c.PutAsync(Path, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("validation_failed", (await r.Body()).GetProperty("code").GetString());
        Assert.Empty(await Rows(me));
    }
}
