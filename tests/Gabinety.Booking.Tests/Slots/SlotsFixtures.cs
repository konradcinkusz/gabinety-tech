using Gabinety.Billing;
using Gabinety.Booking.Clients;
using Gabinety.Booking.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Booking.Tests;

using Booking = Gabinety.Booking.Features.Bookings.Entities.Booking;

/// <summary>Public-endpoint host: fake Directory and a TimeProvider the test sets (now = 2026-11-16 12:00 UTC unless advanced).</summary>
sealed class SlotsHost : WebApplicationFactory<Program>
{
    public FakeDirectory Directory { get; } = new();
    public ManualClock Clock { get; } = new();
    public Guid Practitioner { get; } = Guid.NewGuid();
    public Guid Offering { get; } = Guid.NewGuid();
    public int DurationMinutes = 60;
    public string Status = "Approved";
    public bool Active = true;
    public Guid OfferingPractitioner;

    public SlotsHost()
    {
        OfferingPractitioner = Practitioner;
        Directory.Override = (r, _) => r.RequestUri!.AbsolutePath == $"/internal/v1/offerings/{Offering}"
            ? FakeDirectory.Json(new
            {
                offeringId = Offering, practitionerId = OfferingPractitioner, practitionerUserId = "demo-user-1", practitionerStatus = Status,
                title = "demo-offering", durationMinutes = DurationMinutes, price = new { amountMinor = 10000, currency = "PLN" },
                meetingUrl = (string?)null, isActive = Active,
            })
            : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("GABINETY_FAKE", "1"); // Mock payments run only in the fake profile (F-01)
        builder.UseSetting("Backend:Directory", FakeDirectory.BaseUrl);
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IDirectoryClient>();
            s.AddSingleton<IDirectoryClient>(new DirectoryClient(
                new HttpClient(Directory) { BaseAddress = new Uri(FakeDirectory.BaseUrl) }, FakeDirectory.Key, retryDelay: TimeSpan.Zero));
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(Clock);
        });
    }

    public async Task Seed(Action<BookingDbContext> act)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        act(db);
        await db.SaveChangesAsync();
    }

    /// <summary>Mon-Fri 09:00-17:00 local for the practitioner.</summary>
    public Task WorkWeek() => Seed(db =>
    {
        for (short d = 1; d <= 5; d++)
            db.Rules.Add(new() { Id = Guid.NewGuid(), PractitionerId = Practitioner, IsoWeekday = d, StartLocal = new(9, 0), EndLocal = new(17, 0) });
    });

    public Task Book(BookingStatus status, DateTimeOffset start, int minutes = 60, DateTimeOffset? hold = null) =>
        Seed(db => db.Bookings.Add(Slots.NewBooking(Practitioner, start, minutes, status, hold)));

    public string Url(string from, string to) => $"/api/v1/practitioners/{Practitioner}/slots?offeringId={Offering}&from={from}&to={to}";
}

static class Slots
{
    public static Booking NewBooking(Guid practitioner, DateTimeOffset start, int minutes, BookingStatus status, DateTimeOffset? hold = null) => new()
    {
        Id = Guid.NewGuid(), ClientUserId = "demo-client-" + Guid.NewGuid().ToString("N")[..8], PractitionerId = practitioner,
        PractitionerUserId = "demo-user-1", OfferingId = Guid.NewGuid(), OfferingTitle = "demo-offering", DurationMinutes = (short)minutes,
        PriceMinor = 10000, StartUtc = start, EndUtc = start.AddMinutes(minutes), Status = status, BriefText = "demo-brief",
        HoldExpiresAt = hold, IdempotencyKey = Guid.NewGuid(), CreatedAt = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero),
    };
}
