using System.Text.Json;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;

namespace Gabinety.Architecture.Tests;

public class ContractsTests
{
    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Money_is_an_object_with_integer_minor_units_never_a_decimal()
    {
        var json = JsonSerializer.Serialize(new MoneyDto(25000), Web);
        Assert.Equal("""{"amountMinor":25000,"currency":"PLN"}""", json);
    }

    [Fact]
    public void Enums_are_strings_and_names_are_camelCase()
    {
        var json = JsonSerializer.Serialize(new MeDto("u1", null, PractitionerStatus.Approved, false, false), Web);
        Assert.Contains("\"practitionerStatus\":\"Approved\"", json);
        Assert.Contains("\"userId\":\"u1\"", json);
    }

    [Fact]
    public void Booking_statuses_are_exactly_the_state_machine_of_CONTRACTS_8_1()
    {
        var expected = new[] { "PendingPayment", "Confirmed", "CancelledByClient", "LateCancelledByClient", "CancelledByPractitioner",
            "Expired", "Completed", "ClientNoShow", "Disputed", "Resolved" };
        Assert.Equal(expected.Order(), Enum.GetNames<BookingStatus>().Order());
    }

    [Fact]
    public void Problem_type_is_a_urn_with_the_code()
    {
        var p = ApiProblem.Of(ErrorCodes.SlotTaken, "Conflict", 409);
        Assert.Equal("urn:gabinety:error:slot_taken", p.Type);
        Assert.Equal("slot_taken", p.Code);
    }

    [Fact]
    public void Every_error_code_of_CONTRACTS_10_1_exists_with_its_snake_case_value()
    {
        var codes = typeof(ErrorCodes).GetFields().Select(f => (string)f.GetRawConstantValue()!).ToHashSet();
        foreach (var c in new[] { "validation_failed", "unauthenticated", "forbidden", "not_found", "invalid_transition", "slot_taken",
            "idempotency_conflict", "practitioner_exists", "already_reviewed", "already_replied", "slot_unavailable", "offering_inactive",
            "own_offering", "practitioner_not_bookable", "submit_incomplete", "too_many_offerings", "contact_details_not_allowed",
            "github_not_linked", "not_reviewable", "invalid_input", "rate_limited", "assistant_unavailable", "dependency_unavailable",
            "cross_site_request" })
            Assert.Contains(c, codes);
    }

    [Fact]
    public void A_booking_request_round_trips()
    {
        var req = new CreateBookingRequest(Guid.NewGuid(), new DateTimeOffset(2026, 12, 1, 9, 30, 0, TimeSpan.Zero),
            new BriefDto("Opis problemu dłuższy niż dwadzieścia znaków", "audyt-kodu", true));
        var json = JsonSerializer.Serialize(req, Web);
        Assert.Contains("\"startUtc\":\"2026-12-01T09:30:00Z\"", json); // CONTRACTS 0: UTC with Z, not +00:00
        var back = JsonSerializer.Deserialize<CreateBookingRequest>(json, Web)!;
        Assert.Equal(req.Brief, back.Brief);
        Assert.Equal(req.StartUtc, back.StartUtc);
    }
}

public class InstantTests
{
    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Nullable_and_non_zero_offset_instants_are_written_in_UTC_with_Z()
    {
        var v = new BookingView(Guid.Empty, BookingStatus.PendingPayment, null, new DateTimeOffset(2026, 12, 1, 10, 30, 0, TimeSpan.FromHours(1)),
            new DateTimeOffset(2026, 12, 1, 11, 0, 0, TimeSpan.FromHours(1)), 30, "t", new MoneyDto(1), new PractitionerRef(Guid.Empty, "d", "s"),
            null, null, null, null, new PaymentView(PaymentStatus.Created, new MoneyDto(0)), null, null, false, false, false, false);
        var json = JsonSerializer.Serialize(v, Web);
        Assert.Contains("\"startUtc\":\"2026-12-01T09:30:00Z\"", json);
        Assert.Contains("\"holdExpiresAt\":null", json);
        var withHold = v with { HoldExpiresAt = new DateTimeOffset(2026, 12, 1, 9, 40, 0, TimeSpan.Zero) };
        Assert.Contains("\"holdExpiresAt\":\"2026-12-01T09:40:00Z\"", JsonSerializer.Serialize(withHold, Web));
    }
}
