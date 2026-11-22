using System.Net;
using System.Net.Http.Json;
using BookingStatus = Gabinety.Billing.BookingStatus;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.Booking.Payments.Mock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Booking.Tests;

/// <summary>
/// Security review F-01: mock payments are fail-closed. Mock (provider, simulate endpoint, unsigned webhook) exists only in the fake profile
/// (<c>GABINETY_FAKE=1</c> or <c>Environment:Kind=Development</c>); anywhere else the host refuses to start, and a vendor value stops it too until its adapter exists (T-09.05).
/// </summary>
public class MockPaymentsFailClosedTests
{
    const string Owner = "demo-client-1", Stranger = "demo-client-2";

    // ---- startup guard ----

    [Theory]
    [InlineData("Mock", null)]                 // Mock outside the fake profile
    [InlineData("mock", "Production")]
    [InlineData("Mock", "Staging")]
    [InlineData(null, null)]                   // no value: the default must not be Mock when deployed
    [InlineData("", "Production")]
    [InlineData("  ", null)]
    public void Outside_the_fake_profile_a_Mock_or_missing_provider_stops_the_host_at_startup(string? provider, string? kind)
    {
        using var h = new CreateHost();
        h.Settings["GABINETY_FAKE"] = "0";
        if (kind != null) h.Settings["Environment:Kind"] = kind;
        if (provider != null) h.Settings["Payments:Provider"] = provider;
        Assert.ThrowsAny<Exception>(() => h.CreateClient()); // WebApplicationFactory can lose the inner message to a startup race; the message is asserted on Problem() below
        var msg = PaymentsFeature.Problem(provider, nonDeployed: false)!;
        Assert.Contains("Payments:Provider", msg);
        Assert.Contains("fake profile", msg);
    }

    [Theory]
    [InlineData("Acme", false)]
    [InlineData("Acme", true)]
    public void A_vendor_value_without_its_adapter_stops_the_host_in_every_profile_and_says_the_adapter_is_missing(string provider, bool fake)
    {
        using var h = new CreateHost();
        h.Settings["GABINETY_FAKE"] = fake ? "1" : "0";
        h.Settings["Payments:Provider"] = provider;
        Assert.ThrowsAny<Exception>(() => h.CreateClient());
        var msg = PaymentsFeature.Problem(provider, nonDeployed: fake)!;
        Assert.Contains("Payments:Provider", msg);
        Assert.Contains("adapter", msg);
    }

    [Theory]
    [InlineData("GABINETY_FAKE", "1", "Mock")]
    [InlineData("GABINETY_FAKE", "1", null)]
    [InlineData("Environment:Kind", "Development", "Mock")]
    [InlineData("Environment:Kind", "Development", null)]
    public async Task In_the_fake_profile_Mock_and_an_unset_provider_start(string key, string value, string? provider)
    {
        await using var h = new CreateHost();
        h.Settings.Remove("GABINETY_FAKE");
        h.Settings[key] = value;
        if (provider != null) h.Settings["Payments:Provider"] = provider;
        Assert.Equal("Mock", h.Services.GetRequiredService<IPaymentProvider>().Name);
        Assert.Equal(HttpStatusCode.OK, (await h.CreateClient().GetAsync("/health")).StatusCode);
    }

    [Fact]
    public void The_message_names_no_secret_and_tells_the_operator_what_to_do()
    {
        using var h = new CreateHost();
        h.Settings["GABINETY_FAKE"] = "0";
        h.Settings["Payments:WebhookSecret"] = "whsec-DO-NOT-LEAK-9999";
        Assert.ThrowsAny<Exception>(() => h.CreateClient());
        var msg = PaymentsFeature.Problem(null, nonDeployed: false)!;
        Assert.DoesNotContain("whsec", msg);
        Assert.Contains("Stripe", msg); // says what to set instead
    }

    // ---- simulate: only in the fake profile, and only for the client of the booking ----

    [Fact]
    public async Task Simulate_is_not_mapped_in_a_deployed_profile()
    {
        await using var fake = new CreateHost();
        await fake.WorkWeek();
        var (id, _) = await Pay.CreateBooking(fake);

        await using var deployed = new CreateHost().AsDeployed("Production");
        var r = await Pay.Simulate(deployed.ClientFor(Owner), id, "succeeded");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode); // no route at all, not even a 401/403 that would confirm it exists
        Assert.Equal(HttpStatusCode.NotFound, (await Pay.Simulate(deployed.ClientFor(null), id, "succeeded")).StatusCode);
    }

    [Fact]
    public async Task Another_signed_in_user_cannot_simulate_someone_elses_booking_and_it_does_not_change()
    {
        await using var h = new CreateHost { MeetingUrl = "https://meet.demo.invalid/room-1" };
        await h.WorkWeek();
        var (id, _) = await Pay.CreateBooking(h, Owner);

        foreach (var outcome in new[] { "succeeded", "failed" })
        {
            var r = await Pay.Simulate(h.ClientFor(Stranger), id, outcome);
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode); // 404, same as an unknown id: the booking's existence is not confirmed
        }
        Assert.Equal(BookingStatus.PendingPayment, (await h.Query(db => db.Bookings.Where(b => b.Id == id).Select(b => b.Status).SingleAsync())));
        Assert.Equal(0, await h.Query(db => db.WebhookEvents.CountAsync(e => e.EventId.StartsWith($"sim_{id:N}"))));
    }

    [Fact]
    public async Task The_practitioner_of_the_booking_is_not_its_client_and_cannot_simulate_either()
    {
        await using var h = new CreateHost();
        await h.WorkWeek();
        var (id, _) = await Pay.CreateBooking(h, Owner);
        Assert.Equal(HttpStatusCode.NotFound, (await Pay.Simulate(h.ClientFor(h.OwnerUserId), id, "succeeded")).StatusCode);
    }

    [Fact]
    public async Task An_admin_who_is_not_the_client_cannot_simulate_either()
    {
        await using var h = new CreateHost();
        await h.WorkWeek();
        var (id, _) = await Pay.CreateBooking(h, Owner);
        Assert.Equal(HttpStatusCode.NotFound, (await Pay.Simulate(h.ClientFor("demo-admin", "Admin"), id, "succeeded")).StatusCode);
    }

    [Fact]
    public async Task The_client_of_the_booking_can_simulate_it_in_the_fake_profile()
    {
        await using var h = new CreateHost { MeetingUrl = "https://meet.demo.invalid/room-1" };
        await h.WorkWeek();
        var (id, _) = await Pay.CreateBooking(h, Owner);
        Assert.Equal(HttpStatusCode.OK, (await Pay.Simulate(h.ClientFor(Owner), id, "succeeded")).StatusCode);
        Assert.Equal(BookingStatus.Confirmed, (await h.Query(db => db.Bookings.Where(b => b.Id == id).Select(b => b.Status).SingleAsync())));
    }

    // ---- webhook: no secret means Mock only ----

    [Fact]
    public async Task With_no_secret_the_webhook_is_accepted_for_Mock_and_rejected_for_any_other_provider_name()
    {
        await using var mock = new CreateHost();
        await mock.WorkWeek();
        var (id, pid) = await Pay.CreateBooking(mock);
        var body = Pay.EventJson("evt-1", "PaymentSucceeded", pid);
        Assert.Equal(HttpStatusCode.OK, (await Pay.Webhook(mock.ClientFor(null), body, null)).StatusCode);

        await using var other = new CreateHost().AsDeployed("Production");
        await other.WorkWeek();
        var (id2, pid2) = await Pay.CreateBooking(other);
        var body2 = Pay.EventJson("evt-2", "PaymentSucceeded", pid2);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Pay.Webhook(other.ClientFor(null), body2, null, "acme")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Pay.Webhook(other.ClientFor(null), body2, "00", "acme")).StatusCode);
        Assert.Equal(BookingStatus.PendingPayment, await other.Query(db => db.Bookings.Where(b => b.Id == id2).Select(b => b.Status).SingleAsync()));
    }
}
