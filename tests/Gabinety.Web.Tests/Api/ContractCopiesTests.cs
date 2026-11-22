using System.Text.Json;
using System.Text.Json.Serialization;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Contracts.Reputation;
using W = Gabinety.Web.Api;

namespace Gabinety.Web.Tests.Backends;

/// <summary>
/// BLAZOR-WEB-APP section 5: the web project keeps its OWN copies of the DTOs it reads; this is the one test that references both sides. It serializes the real contract types the way the
/// services do (CONTRACTS 0: camelCase, enums as strings, instants with Z) and reads them into the copies, then writes the copies and checks that every property they carry is in the real
/// JSON with the same value. A renamed field on either side is a failing test here, not a blank column on a page.
/// </summary>
public class ContractCopiesTests
{
    static readonly JsonSerializerOptions Services = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    static readonly Guid Id = Guid.Parse("018f0000-0000-7000-8000-000000000001");
    static readonly DateTimeOffset At = new(2026, 12, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Every property of <paramref name="copy"/>'s JSON exists in <paramref name="real"/>'s JSON with an equal value (the copy may carry fewer fields, never different ones).</summary>
    static void AssertSubset(JsonElement copy, JsonElement real, string path = "$")
    {
        switch (copy.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in copy.EnumerateObject())
                {
                    JsonElement found = default;
                    var present = real.ValueKind == JsonValueKind.Object && real.TryGetProperty(p.Name, out found);
                    Assert.True(present, $"{path}.{p.Name} is in the web copy but not in the contract's JSON");
                    AssertSubset(p.Value, found, $"{path}.{p.Name}");
                }
                break;
            case JsonValueKind.Array:
                Assert.Equal(real.GetArrayLength(), copy.GetArrayLength());
                for (var i = 0; i < copy.GetArrayLength(); i++) AssertSubset(copy[i], real[i], $"{path}[{i}]");
                break;
            case JsonValueKind.String when DateTimeOffset.TryParse(copy.GetString(), out var a) && DateTimeOffset.TryParse(real.GetString(), out var b):
                Assert.True(a == b, $"{path}: copy {copy} != contract {real}"); // the same instant: "Z" and "+00:00" are one representation of it
                break;
            default:
                Assert.True(copy.ToString() == real.ToString(), $"{path}: copy {copy} != contract {real}");
                break;
        }
    }

    static void RoundTrip<TCopy>(object real, JsonSerializerOptions? wire = null, Action<TCopy>? check = null, params string[] notYetInContract)
    {
        var json = JsonSerializer.Serialize(real, real.GetType(), wire ?? Services);
        var copy = JsonSerializer.Deserialize<TCopy>(json, W.ApiClient.Json)!;
        Assert.NotNull(copy);
        check?.Invoke(copy);
        using var realDoc = JsonDocument.Parse(json);
        // a field the web copy reads before the service sends it (see the DTO's remarks) is left out of the comparison, and only that one
        var copyJson = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(copy, W.ApiClient.Json))!;
        foreach (var name in notYetInContract) copyJson.AsObject().Remove(name);
        using var copyDoc = JsonDocument.Parse(copyJson.ToJsonString());
        AssertSubset(copyDoc.RootElement, realDoc.RootElement);
    }

    [Fact]
    public void The_practitioner_page() =>
        RoundTrip<W.PageDto<W.PractitionerCardDto>>(
            new Page<PractitionerCard>([new PractitionerCard(Id, "demo-anna-demowska", "Anna Demowska", "Audyty", "demo-anna", [Badge.Email, Badge.Github, Badge.Nip, Badge.Approved],
                new RatingDto(4.5, 12), false, new MoneyDto(25000), ["audyt-kodu"], [30, 60])], "NEXT"),
            check: c => { Assert.Equal(["email", "github", "nip", "approved"], c.Items[0].Badges); Assert.Equal(25000, c.Items[0].FromPrice.AmountMinor); Assert.Equal("NEXT", c.NextCursor); Assert.Equal(4.5, c.Items[0].Rating!.Average); });

    [Fact]
    public void The_last_page_has_no_cursor_and_a_card_without_rating_has_none() =>
        RoundTrip<W.PageDto<W.PractitionerCardDto>>(
            new Page<PractitionerCard>([new PractitionerCard(Id, "s", "N", "H", null, [], null, true, new MoneyDto(5000), [], [30])]), check: c => { Assert.Null(c.NextCursor); Assert.Null(c.Items[0].Rating); });

    [Fact]
    public void The_taxonomy() =>
        RoundTrip<W.TaxonomyDto>(new Gabinety.Contracts.Directory.TaxonomyDto("v", [new CategoryDto("a", "A")], [new CategoryDto("t", "T")]), check: c => Assert.Equal("A", c.Problems[0].NamePl));

    [Fact]
    public void The_sitemap() =>
        RoundTrip<W.SitemapDto>(new Gabinety.Contracts.Directory.SitemapDto([new SitemapPractitioner("s", At)], [new SitemapCategory("/programisci", 6, true)]), check: c => Assert.True(c.Categories[0].Indexable));

    [Theory]
    [InlineData(BookingStatus.PendingPayment)] [InlineData(BookingStatus.Confirmed)] [InlineData(BookingStatus.LateCancelledByClient)] [InlineData(BookingStatus.Resolved)]
    public void A_booking_view_with_its_status_as_the_wire_name(BookingStatus status)
    {
        // Booking serializes BookingStatus with the attribute converter: PascalCase, which is also what the Polish labels are keyed by.
        var real = new BookingView(Id, status, null, At, At.AddHours(1), 60, "Konsultacja", new MoneyDto(25000), new PractitionerRef(Id, "Anna", "demo-anna"), new ClientRef("demo-client"), null, null, null,
            new PaymentView(PaymentStatus.Succeeded, new MoneyDto(0)), null, null, true, false, false, false);
        RoundTrip<W.PageDto<W.BookingViewDto>>(new Page<BookingView>([real], null), new JsonSerializerOptions(JsonSerializerDefaults.Web), c =>
        {
            Assert.Equal(status.ToString(), c.Items[0].Status);
            Assert.Contains(c.Items[0].Status, Components.Pages.Bookings.BookingFormat.Statuses);
            Assert.Equal(At, c.Items[0].StartUtc);
            Assert.Equal("demo-client", c.Items[0].Client!.DisplayName);
        });
    }

    [Fact]
    public void A_review_with_a_reply() =>
        RoundTrip<W.PageDto<W.ReviewDto>>(new Page<Gabinety.Contracts.Reputation.ReviewDto>([new Gabinety.Contracts.Reputation.ReviewDto(Id, 5, "Opinia", At, new ReplyDto("Dziękuję", At))]),
            check: c => Assert.Equal("Dziękuję", c.Items[0].Reply!.Text));

    [Fact] public void The_money_object_is_minor_units_and_a_currency() => RoundTrip<W.MoneyDto>(new MoneyDto(1500000), check: c => Assert.Equal(("PLN", 1500000L), (c.Currency, c.AmountMinor)));

    [Fact]
    public void A_suspicious_pair_of_the_admin_list() =>
        RoundTrip<W.PageDto<W.SuspiciousPairDto>>(new Page<Gabinety.Contracts.Reputation.SuspiciousPairDto>([new Gabinety.Contracts.Reputation.SuspiciousPairDto("pair-1", "demo-client-1", Id, 4, 4)], "NEXT"),
            check: c => Assert.Equal((Id, 4, "NEXT"), (c.Items[0].PractitionerId, c.Items[0].Reviews, c.NextCursor)));

    [Theory]
    [InlineData(PaymentStatus.Succeeded)] [InlineData(PaymentStatus.PartiallyRefunded)]
    public void The_booking_a_dispute_card_reads_is_the_booking_view_with_the_payment_as_its_wire_name(PaymentStatus payment)
    {
        var real = new BookingView(Id, BookingStatus.Disputed, null, At, At.AddHours(1), 60, "Audyt kodu", new MoneyDto(18000), new PractitionerRef(Id, "", "demo-anna"), null, null, null, null,
            new PaymentView(payment, new MoneyDto(10000)), null, null, false, false, false, false);
        RoundTrip<W.AdminBookingDto>(real, new JsonSerializerOptions(JsonSerializerDefaults.Web), c =>
        {
            Assert.Equal(payment.ToString(), c.Payment.Status); // the keys of the Polish labels
            Assert.Equal((18000L, 10000L, "Disputed"), (c.Price.AmountMinor, c.Payment.Refunded.AmountMinor, c.Status));
            Assert.Equal(At, c.StartUtc);
        });
    }
    // ---- the booking flow and the client area

    static readonly JsonSerializerOptions Plain = new(JsonSerializerDefaults.Web);

    [Fact]
    public void The_booking_detail_with_every_field_the_pages_read()
    {
        var real = new BookingView(Id, BookingStatus.Confirmed, "powód", At, At.AddHours(1), 60, "Audyt", new MoneyDto(25000), new PractitionerRef(Id, "Anna", "demo-anna"), new ClientRef("demo-client"),
            new BriefDto("Opis problemu demo, syntetyczny.", "audyt-kodu", true), "https://meet.example/x", At.AddMinutes(10), new PaymentView(PaymentStatus.PartiallyRefunded, new MoneyDto(9000)), new MoneyDto(3750), new MoneyDto(21250),
            true, false, true, false, new CancelOutcomeDto(CancelOutcomeKind.Partial, new MoneyDto(9000)));
        RoundTrip<W.BookingDetailDto>(real, Plain, c =>
        {
            Assert.Equal(("Confirmed", "PartiallyRefunded", 9000L), (c.Status, c.Payment.Status, c.Payment.Refunded.AmountMinor));
            Assert.Equal((true, false, true, false), (c.CanCancel, c.CanDispute, c.CanReportNoShow, c.CanReview));
            Assert.Equal(("audyt-kodu", true, "https://meet.example/x"), (c.Brief!.ProblemSlug, c.Brief.AiAssisted, c.MeetingUrl));
            Assert.Equal((3750L, 21250L, At.AddMinutes(10)), (c.Fee!.AmountMinor, c.Payout!.AmountMinor, c.HoldExpiresAt));
            Assert.Equal(("Anna", "demo-anna", "demo-client"), (c.Practitioner.DisplayName, c.Practitioner.Slug, c.Client!.DisplayName));
            Assert.Equal(("partial", 9000L), (c.CancelOutcome!.Outcome, c.CancelOutcome.Refund.AmountMinor)); // F-11: the wire name is snake_case
        });
    }

    [Fact]
    public void The_booking_detail_without_the_optional_parts_has_no_cancel_outcome()
    {
        var real = new BookingView(Id, BookingStatus.PendingPayment, null, At, At.AddHours(1), 30, "Audyt", new MoneyDto(25000), new PractitionerRef(Id, "Anna", "demo-anna"), null, null, null, null,
            new PaymentView(PaymentStatus.Created, new MoneyDto(0)), null, null, false, false, false, false);
        RoundTrip<W.BookingDetailDto>(real, Plain, c => { Assert.Null(c.Client); Assert.Null(c.Brief); Assert.Null(c.MeetingUrl); Assert.Null(c.Fee); Assert.Null(c.CancelOutcome); });
    }

    [Fact]
    public void The_create_response_carries_the_checkout_url_of_the_mock_or_of_the_provider() =>
        RoundTrip<W.CreateBookingResponseDto>(new CreateBookingResponse(Id, BookingStatus.PendingPayment, At, "https://checkout.stripe.com/c/pay/cs_test_1"), Plain,
            c => Assert.Equal(("PendingPayment", "https://checkout.stripe.com/c/pay/cs_test_1", At), (c.Status, c.CheckoutUrl, c.HoldExpiresAt)));

    [Fact]
    public void The_create_request_is_what_the_service_binds_the_instant_with_a_Z()
    {
        var copy = new W.CreateBookingRequestDto(Id, new DateTimeOffset(2026, 12, 8, 12, 0, 0, TimeSpan.FromHours(1)), new W.BriefDto("Opis problemu demo, syntetyczny.", "audyt-kodu", true));
        var json = JsonSerializer.Serialize(copy, W.ApiClient.Json);
        var real = JsonSerializer.Deserialize<CreateBookingRequest>(json, Plain)!; // the service's own record reads what the web writes
        Assert.Equal((Id, new DateTimeOffset(2026, 12, 8, 11, 0, 0, TimeSpan.Zero), "audyt-kodu", true), (real.OfferingId, real.StartUtc, real.Brief.ProblemSlug, real.Brief.AiAssisted));
        Assert.Contains("\"startUtc\":\"2026-12-08T11:00:00Z\"", json);
    }

    [Fact]
    public void The_brief_suggestion_of_the_assistant() =>
        RoundTrip<W.BriefSuggestionDto>(new Gabinety.Contracts.Assistant.BriefSuggestion("audyt-kodu", ["dotnet"], "Streszczenie", ["Jaki budżet?"], 60, Gabinety.Contracts.Assistant.Confidence.High, "fake", "v1"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new SnakeCaseLowerEnumConverter() } }, c => Assert.Equal(("audyt-kodu", "Streszczenie", "high"), (c.ProblemSlug, c.Summary, c.Confidence)));

    [Theory]
    [InlineData(Gabinety.Contracts.Reputation.ReviewStatus.Published)] [InlineData(Gabinety.Contracts.Reputation.ReviewStatus.Held)]
    public void The_created_review_status_is_the_wire_name_the_page_compares(Gabinety.Contracts.Reputation.ReviewStatus status) =>
        RoundTrip<W.CreateReviewResponseDto>(new Gabinety.Contracts.Reputation.CreateReviewResponse(Id, status), Plain, c => Assert.Equal(status.ToString(), c.Status));

    [Fact]
    public void Who_am_I() =>
        RoundTrip<W.MeDto>(new MeDto("user-1", Id, PractitionerStatus.Approved, false, true), Plain, c => Assert.Equal((Id, "Approved", true), (c.PractitionerId, c.PractitionerStatus, c.IsModerator)));
}
