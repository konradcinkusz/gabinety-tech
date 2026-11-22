using System.Net;
using System.Text.Json;
using Gabinety.Booking.Clients;
using Gabinety.Booking.Features.Availability;
using Gabinety.Contracts.Directory;
using Gabinety.Contracts.Booking;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Booking.Tests;

public class AvailabilityApiTests : IDisposable
{
    readonly BookingFactory f = new();
    readonly string user = "demo-user-" + Guid.NewGuid().ToString("N");
    readonly HttpClient c;

    public AvailabilityApiTests() { f.Directory.Add(user); c = f.ClientFor(user); }
    public void Dispose() => f.Dispose();

    static async Task<AvailabilityDto> Get(HttpClient c)
    {
        var r = await c.GetAsync("/api/v1/me/availability");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<AvailabilityDto>())!;
    }

    static async Task AssertInvalid(HttpResponseMessage r, string errorKeyPrefix)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var b = await r.Body();
        Assert.Equal("validation_failed", b.GetProperty("code").GetString());
        Assert.Equal("urn:gabinety:error:validation_failed", b.GetProperty("type").GetString());
        Assert.Contains(b.GetProperty("errors").EnumerateObject(), p => p.Name.StartsWith(errorKeyPrefix));
    }

    [Fact]
    public async Task Without_a_token_both_verbs_are_401()
    {
        var anon = f.ClientFor(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/me/availability")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PutAvailability([])).StatusCode);
    }

    [Fact]
    public async Task A_new_practitioner_has_no_availability()
    {
        var a = await Get(c);
        Assert.Empty(a.Rules);
        Assert.Empty(a.Exceptions);
    }

    [Fact]
    public async Task Put_replaces_the_whole_set_and_get_orders_by_weekday_then_start()
    {
        Assert.Equal(HttpStatusCode.OK, (await c.PutAvailability([Http.Rule(1, "09:00", "12:00")], [Http.Day("2026-12-24"), Http.Day("2026-12-25")])).StatusCode);
        var second = await c.PutAvailability(
            [Http.Rule(3, "14:00", "16:00"), Http.Rule(1, "13:00", "15:30"), Http.Rule(1, "08:00", "09:00"), Http.Rule(7, "10:00", "10:30")],
            [Http.Day("2027-01-01")]);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var a = await Get(c);
        Assert.Equal([(1, "08:00", "09:00"), (1, "13:00", "15:30"), (3, "14:00", "16:00"), (7, "10:00", "10:30")],
            a.Rules.Select(r => (r.IsoWeekday, r.StartLocal, r.EndLocal)));
        Assert.Equal([new DateOnly(2027, 1, 1)], a.Exceptions.Select(e => e.DateLocal));
    }

    [Fact]
    public async Task Put_with_empty_lists_clears_everything()
    {
        await c.PutAvailability([Http.Rule(2, "09:00", "10:00")], [Http.Day("2027-02-02")]);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAvailability([])).StatusCode);
        var a = await Get(c);
        Assert.Empty(a.Rules);
        Assert.Empty(a.Exceptions);
    }

    [Fact]
    public async Task Twenty_eight_rules_and_ninety_exceptions_are_accepted()
    {
        var rules = Enumerable.Range(1, 7).SelectMany(d => new[] { "08:00", "10:00", "12:00", "14:00" }
            .Select(s => Http.Rule(d, s, TimeOnly.Parse(s).AddHours(1).ToString("HH:mm")))).ToArray();
        var days = Enumerable.Range(0, 90).Select(i => Http.Day(new DateOnly(2027, 1, 1).AddDays(i).ToString("yyyy-MM-dd"))).ToArray();
        Assert.Equal(28, rules.Length);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAvailability(rules, days)).StatusCode);
        var a = await Get(c);
        Assert.Equal(28, a.Rules.Count);
        Assert.Equal(90, a.Exceptions.Count);
    }

    [Fact]
    public async Task Twenty_nine_rules_are_422_and_nothing_changes()
    {
        await c.PutAvailability([Http.Rule(1, "09:00", "10:00")]);
        var rules = Enumerable.Range(1, 29).Select(i => Http.Rule(i % 7 + 1, $"{i % 10:00}:00", $"{i % 10:00}:30")).ToArray();
        await AssertInvalid(await c.PutAvailability(rules), "rules");
        Assert.Single((await Get(c)).Rules);
    }

    [Fact]
    public async Task Ninety_one_exceptions_are_422()
    {
        var days = Enumerable.Range(0, 91).Select(i => Http.Day(new DateOnly(2027, 1, 1).AddDays(i).ToString("yyyy-MM-dd"))).ToArray();
        await AssertInvalid(await c.PutAvailability([], days), "exceptions");
        Assert.Empty((await Get(c)).Exceptions);
    }

    [Theory]
    [InlineData(1, "09:15", "10:00")]   // start off the half hour
    [InlineData(1, "09:00", "10:10")]   // end off the half hour
    [InlineData(1, "10:00", "10:00")]   // end not after start
    [InlineData(1, "11:00", "10:00")]   // end before start
    [InlineData(0, "09:00", "10:00")]   // weekday out of range
    [InlineData(8, "09:00", "10:00")]
    [InlineData(1, "9:00", "10:00")]    // not "HH:mm"
    [InlineData(1, "09:00", "24:00")]   // midnight end is not expressible
    [InlineData(1, "09:00", "nope")]
    public async Task An_invalid_rule_is_422_and_the_old_data_stays(int day, string start, string end)
    {
        await c.PutAvailability([Http.Rule(2, "09:00", "10:00")], [Http.Day("2027-03-03")]);
        await AssertInvalid(await c.PutAvailability([Http.Rule(day, start, end)], [Http.Day("2027-04-04")]), "rules[0]");
        var a = await Get(c);
        Assert.Equal([(2, "09:00", "10:00")], a.Rules.Select(r => (r.IsoWeekday, r.StartLocal, r.EndLocal)));
        Assert.Equal([new DateOnly(2027, 3, 3)], a.Exceptions.Select(e => e.DateLocal));
    }

    [Fact]
    public async Task Overlap_on_one_weekday_is_422()
    {
        await AssertInvalid(await c.PutAvailability([Http.Rule(1, "09:00", "11:00"), Http.Rule(1, "10:30", "12:00")]), "rules");
        await AssertInvalid(await c.PutAvailability([Http.Rule(1, "09:00", "12:00"), Http.Rule(1, "10:00", "11:00")]), "rules");
    }

    [Fact]
    public async Task Touching_rules_and_the_same_hours_on_another_weekday_are_fine()
    {
        var r = await c.PutAvailability([Http.Rule(1, "09:00", "10:00"), Http.Rule(1, "10:00", "11:00"), Http.Rule(2, "09:00", "11:00")]);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(3, (await Get(c)).Rules.Count);
    }

    [Fact]
    public async Task Duplicate_exception_dates_are_422()
    {
        await AssertInvalid(await c.PutAvailability([], [Http.Day("2027-05-05"), Http.Day("2027-05-05")]), "exceptions");
    }

    [Fact]
    public async Task Two_practitioners_do_not_see_each_others_availability()
    {
        var other = "demo-user-" + Guid.NewGuid().ToString("N");
        f.Directory.Add(other);
        var oc = f.ClientFor(other);
        await c.PutAvailability([Http.Rule(1, "09:00", "10:00")], [Http.Day("2027-06-06")]);
        await oc.PutAvailability([Http.Rule(5, "15:00", "16:00"), Http.Rule(5, "16:00", "17:00")]);

        Assert.Equal([(1, "09:00")], (await Get(c)).Rules.Select(r => (r.IsoWeekday, r.StartLocal)));
        Assert.Equal([(5, "15:00"), (5, "16:00")], (await Get(oc)).Rules.Select(r => (r.IsoWeekday, r.StartLocal)));
        Assert.Empty((await Get(oc)).Exceptions);
        await oc.PutAvailability([]); // replacing the other's set must not touch mine
        Assert.Single((await Get(c)).Rules);
        Assert.Single((await Get(c)).Exceptions);
    }

    [Fact]
    public async Task A_caller_who_is_not_a_practitioner_gets_403_forbidden()
    {
        var nobody = f.ClientFor("demo-user-not-a-practitioner");
        foreach (var r in new[] { await nobody.GetAsync("/api/v1/me/availability"), await nobody.PutAvailability([]) })
        {
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Equal("forbidden", (await r.Body()).GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task A_practitioner_who_is_not_approved_may_still_edit()
    {
        f.Directory.Override = (_, _) => FakeDirectory.Json(new { practitionerId = Guid.NewGuid(), status = "Submitted" });
        Assert.Equal(HttpStatusCode.OK, (await c.PutAvailability([Http.Rule(1, "09:00", "10:00")])).StatusCode);
    }

    [Fact]
    public async Task The_internal_key_is_presented_to_Directory()
    {
        await c.GetAsync("/api/v1/me/availability");
        Assert.Equal(FakeDirectory.Key, f.Directory.LastKeyHeader);
    }

    // ---- Directory failures ----

    static async Task AssertUnavailable(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        var text = await r.Content.ReadAsStringAsync();
        Assert.Equal("dependency_unavailable", JsonDocument.Parse(text).RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("directory.demo.invalid", text);
        Assert.DoesNotContain(FakeDirectory.Key, text);
    }

    [Fact]
    public async Task Directory_refusing_connections_is_503_dependency_unavailable_and_leaks_nothing()
    {
        f.Directory.Down = true;
        await AssertUnavailable(await c.GetAsync("/api/v1/me/availability"));
        await AssertUnavailable(await c.PutAvailability([Http.Rule(1, "09:00", "10:00")]));
        Assert.Equal(4, f.Directory.Calls); // two requests, one retry each
    }

    [Fact]
    public async Task Directory_answering_503_twice_is_503_dependency_unavailable()
    {
        f.Directory.Override = (_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        await AssertUnavailable(await c.GetAsync("/api/v1/me/availability"));
        Assert.Equal(2, f.Directory.Calls);
    }

    [Fact]
    public async Task One_failure_then_success_works_because_a_GET_is_retried_once()
    {
        f.Directory.Override = (r, n) => n == 1 ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : FakeDirectory.Json(new { practitionerId = Guid.NewGuid(), status = "Approved" });
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/me/availability")).StatusCode);
        Assert.Equal(2, f.Directory.Calls);
    }

    [Fact]
    public async Task The_by_user_answer_is_cached_so_a_second_request_does_not_call_Directory()
    {
        await c.GetAsync("/api/v1/me/availability");
        await c.GetAsync("/api/v1/me/availability");
        await c.PutAvailability([]);
        Assert.Equal(1, f.Directory.ByUserCalls);
    }
}

public class PractitionerResolverTests
{
    sealed class Counting(Func<string, Guid?> answer) : IDirectoryClient
    {
        public int Calls;
        public Task<DirectoryPractitioner?> GetPractitionerByUserAsync(string userId, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(answer(userId) is { } id ? new DirectoryPractitioner(id, PractitionerStatus.Approved) : null);
        }
        public Task<DirectoryOffering?> GetOfferingAsync(Guid offeringId, CancellationToken ct) => throw new NotSupportedException();
        public Task<DirectoryPractitionerName?> GetPractitionerNameAsync(Guid practitionerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<DirectoryContact?> GetPractitionerContactAsync(string userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsAliveAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public async Task A_cached_user_is_asked_again_only_after_60_seconds()
    {
        var clock = new ManualClock();
        var id = Guid.NewGuid();
        var dir = new Counting(_ => id);
        var resolver = new PractitionerResolver(dir, clock);

        Assert.Equal(id, await resolver.ResolveAsync("demo-user-1", default));
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(id, await resolver.ResolveAsync("demo-user-1", default));
        Assert.Equal(1, dir.Calls);
        clock.Advance(TimeSpan.FromSeconds(2)); // 61 s since the first answer
        Assert.Equal(id, await resolver.ResolveAsync("demo-user-1", default));
        Assert.Equal(2, dir.Calls);
        Assert.Equal(id, await resolver.ResolveAsync("demo-user-2", default));
        Assert.Equal(3, dir.Calls); // another user is a separate entry
    }

    [Fact]
    public async Task Clear_makes_the_next_resolve_ask_the_directory_again()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var current = first;
        var dir = new Counting(_ => current);
        var resolver = new PractitionerResolver(dir, new ManualClock());
        Assert.Equal(first, await resolver.ResolveAsync("demo-user-1", default));

        current = second; // the Directory was reset and reseeded: the same user has a new practitioner id
        Assert.Equal(first, await resolver.ResolveAsync("demo-user-1", default)); // still inside the 60 s
        resolver.Clear();
        Assert.Equal(second, await resolver.ResolveAsync("demo-user-1", default));
    }

    [Fact]
    public async Task A_non_practitioner_resolves_to_null_and_is_not_cached()
    {
        var dir = new Counting(_ => null);
        var resolver = new PractitionerResolver(dir, new ManualClock());
        Assert.Null(await resolver.ResolveAsync("demo-user-1", default));
        Assert.Null(await resolver.ResolveAsync("demo-user-1", default));
        Assert.Equal(2, dir.Calls);
    }
}
