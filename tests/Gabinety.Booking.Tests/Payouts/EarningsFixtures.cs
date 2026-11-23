using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Features.Payouts;
using Gabinety.Contracts.Booking;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>Seeding and reading for the earnings tests. The host clock is 2026-11-16 12:00 UTC, so the default period is 1 to 30 November 2026 in Warsaw (UTC+1).</summary>
static class Earn
{
    public const string OtherUser = "demo-other-practitioner-user", NobodyUser = "demo-not-a-practitioner";

    /// <summary>A host with Directory answering for the host's practitioner and a second one (<paramref name="other"/>: a fresh id per host, because the in-memory store outlives a host), the payout job off (it would take the seeded rows up), and Mock payments.</summary>
    public static CreateHost Host(Guid other, Action<CreateHost>? tweak = null)
    {
        var h = new CreateHost();
        h.Settings["Jobs:Enabled"] = "false";
        tweak?.Invoke(h);
        var original = h.Directory.Override;
        h.Directory.Override = (r, n) =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path == $"/internal/v1/practitioners/by-user/{h.OwnerUserId}") return FakeDirectory.Json(new { practitionerId = h.Practitioner, status = "Approved" });
            if (path == $"/internal/v1/practitioners/by-user/{OtherUser}") return FakeDirectory.Json(new { practitionerId = other, status = "Approved" });
            return original!(r, n);
        };
        return h;
    }

    public static DateTimeOffset Utc(string iso) => DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal);

    public static Task<Guid> Booking(CreateHost h, Guid practitioner, BookingStatus status, long price, string startUtc, long? payoutMinor = null, string client = "demo-client-1") =>
        Booking(h, practitioner, status, price, Utc(startUtc), payoutMinor, client);

    public static async Task<Guid> Booking(CreateHost h, Guid practitioner, BookingStatus status, long price, DateTimeOffset start, long? payoutMinor = null, string client = "demo-client-1")
    {
        var id = Guid.NewGuid();
        await h.Seed(db => db.Bookings.Add(new BookingEntity
        {
            Id = id, ClientUserId = client, PractitionerId = practitioner, PractitionerUserId = "demo-user-of-" + practitioner, OfferingId = h.Offering,
            OfferingTitle = h.Title, DurationMinutes = 60, PriceMinor = price, StartUtc = start, EndUtc = start.AddHours(1), Status = status,
            BriefText = "demo-brief: a first review of my plan", IdempotencyKey = Guid.NewGuid(), CreatedAt = start.AddDays(-3),
            PayoutMinor = payoutMinor,
        }));
        return id;
    }

    public static Task Payout(CreateHost h, Guid booking, Guid practitioner, PayoutStatus status, long amount, string? sentAtUtc = null) =>
        h.Seed(db => db.Payouts.Add(new Payout
        {
            Id = Guid.NewGuid(), BookingId = booking, PractitionerId = practitioner, AmountMinor = amount, Status = status,
            CreatedAt = Utc("2026-11-01T00:00:00Z"), SentAt = sentAtUtc is null ? null : Utc(sentAtUtc),
        }));

    public static Task Fee(CreateHost h, Guid booking, long amount, string atUtc) =>
        h.Seed(db => db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(), BookingId = booking, Kind = LedgerKind.PlatformFee, AmountMinor = amount, PairKey = "demo-pair-" + Guid.NewGuid().ToString("N"), CreatedAt = Utc(atUtc),
        }));

    /// <summary>
    /// The acceptance scenario of T-11.15, all in November 2026: one Sent payout (21250 on a 25000 booking, fee 3750), one Pending payout (5000 on a 5000 booking of the same
    /// pair, so no second fee), one Disputed booking of 12000. Hand sums: paidOut 21250, pending 5000, disputed 12000, fees 3750, count 3.
    /// </summary>
    public static async Task SeedScenario(CreateHost h, Guid practitioner)
    {
        var sent = await Booking(h, practitioner, BookingStatus.Completed, 25000, "2026-11-03T10:00:00Z", payoutMinor: 21250);
        await Payout(h, sent, practitioner, PayoutStatus.Sent, 21250, sentAtUtc: "2026-11-06T09:00:00Z");
        await Fee(h, sent, 3750, "2026-11-05T10:00:00Z");

        var pending = await Booking(h, practitioner, BookingStatus.Completed, 5000, "2026-11-12T10:00:00Z", payoutMinor: 5000);
        await Payout(h, pending, practitioner, PayoutStatus.Pending, 5000);

        await Booking(h, practitioner, BookingStatus.Disputed, 12000, "2026-11-14T10:00:00Z");
    }

    public static async Task<JsonElement> Get(HttpClient c, string query = "")
    {
        var r = await c.GetAsync("/api/v1/me/earnings" + query);
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>());
    }

    /// <summary>The wire figures in the order of the contract: paidOut, pending, disputed, fees, count.</summary>
    public static (long PaidOut, long Pending, long Disputed, long Fees, int Count) Figures(JsonElement b) =>
        (Minor(b, "paidOut"), Minor(b, "pending"), Minor(b, "disputed"), Minor(b, "fees"), b.GetProperty("count").GetInt32());

    static long Minor(JsonElement b, string name)
    {
        var m = b.GetProperty(name);
        Assert.Equal("PLN", m.GetProperty("currency").GetString());
        return m.GetProperty("amountMinor").GetInt64();
    }
}
