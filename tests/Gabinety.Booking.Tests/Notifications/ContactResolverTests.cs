using System.Net;
using Gabinety.Booking.Clients;
using Gabinety.Booking.Notifications;
using Microsoft.Extensions.Logging;

namespace Gabinety.Booking.Tests;

/// <summary>
/// <see cref="ContactResolver"/> and <c>DirectoryClient.GetPractitionerContactAsync</c> (T-04.09c): the client's address is the booking's snapshot, the
/// practitioner's is a Directory fact, cached briefly; every failure is a null address (a retried notification), never an exception.
/// </summary>
public class ContactResolverTests
{
    const string Practitioner = "demo-practitioner-user", Address = "demo-jan@demo.invalid";

    sealed class Rig
    {
        public readonly FakeDirectory Directory = new();
        public readonly ManualClock Clock = new();
        public readonly LogSink Logs = new();
        public readonly ContactResolver Resolver;
        public int ContactCalls;
        public string? AddressOf = Address;

        public Rig()
        {
            Directory.Override = (r, _) =>
            {
                if (r.RequestUri!.AbsolutePath != $"/internal/v1/practitioners/{Practitioner}/contact") return new HttpResponseMessage(HttpStatusCode.NotFound);
                Interlocked.Increment(ref ContactCalls);
                return AddressOf is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : FakeDirectory.Json(new { email = AddressOf });
            };
            var client = new DirectoryClient(new HttpClient(Directory) { BaseAddress = new Uri(FakeDirectory.BaseUrl) }, FakeDirectory.Key, retryDelay: TimeSpan.Zero);
            Resolver = new ContactResolver(client, Clock, new LoggerFactory([Logs]).CreateLogger<ContactResolver>());
        }

        public Task<string?> Practitioner_(string user = Practitioner) => Resolver.ResolveAddressAsync(new ContactLookup(user, true, null), CancellationToken.None);
    }

    [Fact]
    public async Task The_client_address_is_the_booking_snapshot_and_Directory_is_not_asked()
    {
        var r = new Rig();
        Assert.Equal("demo-client@demo.invalid", await r.Resolver.ResolveAddressAsync(new ContactLookup("demo-client", false, " demo-client@demo.invalid "), default));
        Assert.Equal(0, r.Directory.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("a@b.invalid, c@d.invalid")]
    [InlineData("Jan <jan@demo.invalid>")]
    public async Task A_client_without_a_usable_snapshot_has_no_address(string? snapshot)
    {
        var r = new Rig();
        Assert.Null(await r.Resolver.ResolveAddressAsync(new ContactLookup("demo-client", false, snapshot), default));
        Assert.Equal(0, r.Directory.Calls);
    }

    [Fact]
    public async Task The_practitioner_address_comes_from_Directory_with_the_internal_key_and_the_user_id_escaped()
    {
        var r = new Rig();
        r.Directory.Override = (req, _) => FakeDirectory.Json(new { email = Address });
        Assert.Equal(Address, await r.Practitioner_("demo user/1"));
        Assert.Equal(FakeDirectory.Key, r.Directory.LastKeyHeader);
    }

    [Fact]
    public async Task A_practitioner_answer_is_cached_for_five_minutes_and_asked_again_after()
    {
        var r = new Rig();
        Assert.Equal(Address, await r.Practitioner_());
        r.Clock.Advance(ContactResolver.CacheFor - TimeSpan.FromSeconds(1));
        r.AddressOf = "demo-changed@demo.invalid";
        Assert.Equal(Address, await r.Practitioner_()); // still the cached one
        Assert.Equal(1, r.ContactCalls);
        r.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("demo-changed@demo.invalid", await r.Practitioner_());
        Assert.Equal(2, r.ContactCalls);
    }

    [Fact]
    public async Task A_missing_address_is_not_cached_so_one_given_later_is_found_on_the_next_attempt()
    {
        var r = new Rig { AddressOf = null };
        Assert.Null(await r.Practitioner_());
        r.AddressOf = Address;
        Assert.Equal(Address, await r.Practitioner_());
        Assert.Equal(2, r.ContactCalls);
    }

    [Fact]
    public async Task A_cached_address_is_per_user()
    {
        var r = new Rig();
        Assert.Equal(Address, await r.Practitioner_());
        Assert.Null(await r.Practitioner_("demo-other-user")); // 404 for him: must not get the first one's address
    }

    [Fact]
    public async Task Directory_down_is_a_null_address_not_an_exception_and_is_not_cached()
    {
        var r = new Rig();
        r.Directory.Down = true;
        Assert.Null(await r.Practitioner_());
        r.Directory.Down = false;
        Assert.Equal(Address, await r.Practitioner_());
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_Directory_failure_status_is_a_null_address(HttpStatusCode status)
    {
        var r = new Rig();
        r.Directory.Override = (_, _) => new HttpResponseMessage(status);
        Assert.Null(await r.Practitioner_());
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("")]
    public async Task An_unusable_address_from_Directory_is_a_null_address(string email)
    {
        var r = new Rig { AddressOf = email };
        Assert.Null(await r.Practitioner_());
    }

    [Fact]
    public async Task Cancellation_is_the_only_exception_that_escapes()
    {
        var r = new Rig();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => r.Resolver.ResolveAddressAsync(new ContactLookup(Practitioner, true, null), cts.Token));
    }

    [Fact]
    public async Task Nothing_the_resolver_logs_carries_an_address_a_user_id_or_the_key()
    {
        var r = new Rig();
        await r.Practitioner_();
        r.Directory.Down = true;
        r.Clock.Advance(TimeSpan.FromHours(1));
        await r.Practitioner_();
        await r.Resolver.ResolveAddressAsync(new ContactLookup("demo-client", false, "demo-client@demo.invalid"), default);
        Assert.NotEmpty(r.Logs.Lines); // the outage was logged...
        foreach (var line in r.Logs.Lines)
        {
            Assert.DoesNotContain("demo-", line);
            Assert.DoesNotContain("@", line);
            Assert.DoesNotContain(FakeDirectory.Key, line);
        }
    }
}
