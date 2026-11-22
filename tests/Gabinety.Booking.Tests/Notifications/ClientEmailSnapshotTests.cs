using System.Net;
using Gabinety.Booking.Features.Internal.Erasure;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;

namespace Gabinety.Booking.Tests;

/// <summary>
/// T-04.09c: the client's e-mail is copied from the validated token into the booking row at creation; it is never returned by an endpoint, never logged,
/// and erased with the client (RUNBOOK 8.3).
/// </summary>
public class ClientEmailSnapshotTests
{
    const string Client = "demo-client-snap", Address = "demo-snap-klient@demo.invalid";

    static async Task<CreateHost> Host(string? email = Address)
    {
        var h = new CreateHost();
        if (email is not null) h.Emails[Client] = email;
        await h.WorkWeek();
        return h;
    }

    static async Task<(Guid Id, string Body)> Create(CreateHost h)
    {
        var r = await CreateHost.Post(h.ClientFor(Client), h.Req(), CreateHost.Key());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        return (System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("bookingId").GetGuid(), body);
    }

    [Fact]
    public async Task Creating_a_booking_copies_the_email_claim_of_the_token_into_the_row()
    {
        using var h = await Host();
        var (id, _) = await Create(h);
        Assert.Equal(Address, (await h.Bookings()).Single(b => b.Id == id).ClientEmail);
    }

    [Theory]
    [InlineData("")] // no claim at all
    [InlineData("not-an-address")]
    [InlineData("a@b.invalid,c@d.invalid")]
    [InlineData("Jan <jan@demo.invalid>")]
    public async Task A_token_without_a_usable_address_still_creates_the_booking_with_no_snapshot(string claim)
    {
        using var h = await Host(claim);
        var (id, _) = await Create(h);
        Assert.Null((await h.Bookings()).Single(b => b.Id == id).ClientEmail);
    }

    [Fact]
    public async Task The_snapshot_is_the_address_at_creation_not_a_later_one()
    {
        using var h = await Host();
        await Create(h);
        h.Emails[Client] = "demo-later@demo.invalid";
        var r = await CreateHost.Post(h.ClientFor(Client), h.Req("2026-11-17T13:00:00Z"), CreateHost.Key());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var rows = (await h.Bookings()).OrderBy(b => b.CreatedAt).ThenBy(b => b.StartUtc).Select(b => b.ClientEmail).ToList();
        Assert.Contains(Address, rows);
        Assert.Contains("demo-later@demo.invalid", rows);
    }

    [Fact]
    public async Task No_response_of_the_service_and_no_log_line_carries_the_address()
    {
        using var h = await Host();
        var (id, created) = await Create(h);
        var c = h.ClientFor(Client);
        var seen = new List<string> { created };
        seen.Add(await (await c.GetAsync($"/api/v1/bookings/{id}")).Content.ReadAsStringAsync());
        seen.Add(await (await c.GetAsync("/api/v1/me/bookings?role=client")).Content.ReadAsStringAsync());
        // the practitioner's view of the same booking (the client is visible to them by display name only)
        await h.Seed(db => db.Bookings.Single(b => b.Id == id).Status = Gabinety.Billing.BookingStatus.Confirmed);
        seen.Add(await (await h.ClientFor(h.OwnerUserId).GetAsync($"/api/v1/bookings/{id}")).Content.ReadAsStringAsync());
        seen.Add(await (await h.ClientFor(h.OwnerUserId).GetAsync("/api/v1/me/bookings?role=practitioner")).Content.ReadAsStringAsync());

        Assert.All(seen, body => { Assert.DoesNotContain(Address, body); Assert.DoesNotContain("demo-snap-klient", body); Assert.DoesNotContain("clientEmail", body, StringComparison.OrdinalIgnoreCase); });
        Assert.All(h.Logs.Lines, l => Assert.DoesNotContain("demo-snap-klient", l));
    }

    [Fact]
    public async Task Erasing_the_client_nulls_the_snapshot_and_erasing_the_practitioner_leaves_the_clients()
    {
        using var h = ErasureApiTests.NewHost();
        var client = ErasureApiTests.NewUser("c"); var practitioner = ErasureApiTests.NewUser("p");
        var id = await ErasureApiTests.SeedBooking(h, client, practitioner);
        await h.Seed(db => db.Bookings.Single(b => b.Id == id).ClientEmail = Address);

        Assert.Equal(HttpStatusCode.NoContent, (await ErasureApiTests.Erase(h, practitioner)).StatusCode);
        Assert.Equal(Address, (await Row(h, id)).ClientEmail); // the client's own data: a practitioner's erasure does not touch it

        Assert.Equal(HttpStatusCode.NoContent, (await ErasureApiTests.Erase(h, client)).StatusCode);
        var after = await Row(h, id);
        Assert.Null(after.ClientEmail);
        Assert.Equal(ErasureApiTests.Token(client), after.ClientUserId);
    }

    static Task<BookingEntity> Row(CreateHost h, Guid id) => h.Query(db => db.Bookings.AsNoTracking().SingleAsync(b => b.Id == id));

    [Fact]
    public void The_column_is_a_nullable_254_character_string_named_client_email()
    {
        using var h = new CreateHost();
        using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(h.Services);
        var db = (Gabinety.Booking.Persistence.BookingDbContext)scope.ServiceProvider.GetService(typeof(Gabinety.Booking.Persistence.BookingDbContext))!;
        var p = db.Model.FindEntityType(typeof(BookingEntity))!.FindProperty(nameof(BookingEntity.ClientEmail))!;
        Assert.Equal(("client_email", 254, true), (p.GetColumnName(), p.GetMaxLength(), p.IsNullable));
    }
}
