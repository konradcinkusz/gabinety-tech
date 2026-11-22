using System.Text.Json;
using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Booking;
using Gabinety.Web.Components.Pages.Bookings;
using Gabinety.Web.Components.Pages.Reviews;
using Gabinety.Web.Text;

namespace Gabinety.Web.Tests.Models;

/// <summary>The plain C# of the booking flow and the client area: no renderer, no host (BLAZOR-WEB-APP section 10).</summary>
public class BookingFlowModelTests
{
    // ---- the brief

    [Theory]
    [InlineData(0, false)] [InlineData(19, false)] [InlineData(20, true)] [InlineData(2000, true)] [InlineData(2001, false)]
    public void The_brief_is_20_to_2000_characters(int length, bool ok) => Assert.Equal(ok, BriefRules.Problem(new string('a', length)) is null);

    [Fact]
    public void The_brief_is_measured_after_trimming_and_says_which_side_is_wrong()
    {
        Assert.Equal(BookFlowText.Brief.TooShort, BriefRules.Problem("   " + new string('a', 19) + "   "));
        Assert.Equal(BookFlowText.Brief.TooLong, BriefRules.Problem(new string('a', 2001)));
        Assert.Equal(BookFlowText.Brief.TooShort, BriefRules.Problem(null));
        Assert.False(BriefRules.CanAskAssistant(new string('a', 19)));
        Assert.True(BriefRules.CanAskAssistant("  " + new string('a', 20)));
    }

    // ---- where the browser goes after the booking is created

    [Theory]
    [InlineData("/checkout/mock/abc", "/checkout/mock/abc")]
    [InlineData("http://localhost/checkout/mock/abc?x=1", "/checkout/mock/abc?x=1")] // the site's own absolute address becomes a path
    [InlineData("https://checkout.stripe.com/c/pay/cs_test_1", "https://checkout.stripe.com/c/pay/cs_test_1")] // the provider's page
    public void A_checkout_target_is_a_page_of_the_site_or_an_https_provider_page(string url, string expected) => Assert.Equal(expected, CheckoutTarget.Safe(url, "http://localhost"));

    [Theory]
    [InlineData("//evil.example/x")] [InlineData("/\\evil.example")] [InlineData("javascript:alert(1)")] [InlineData("http://evil.example/pay")] [InlineData("https://user:pw@evil.example/")]
    [InlineData("data:text/html,x")] [InlineData("")] [InlineData(null)] [InlineData("/ok\r\nSet-Cookie: x=1")]
    public void Anything_else_is_refused(string? url) => Assert.Null(CheckoutTarget.Safe(url, "http://localhost"));

    // ---- failures of the create call

    [Theory]
    [InlineData(409, "slot_taken", true)] [InlineData(422, "slot_unavailable", true)] [InlineData(422, "offering_inactive", true)] [InlineData(422, "own_offering", true)]
    [InlineData(422, "practitioner_not_bookable", true)] [InlineData(422, "practitioner_payments_not_ready", true)]
    [InlineData(429, "rate_limited", false)] [InlineData(0, "unavailable", false)] [InlineData(503, "dependency_unavailable", false)] [InlineData(400, "validation_failed", false)]
    public void Failures_of_the_create_call_get_polish_text_and_say_whether_to_offer_the_profile(int status, string code, bool profile)
    {
        var f = BookFailure.Of(new ApiFailure(status, code));
        Assert.Equal(profile, f.OffersProfile);
        Assert.NotEmpty(f.Text);
        Assert.DoesNotContain("_", f.Text); // never a code
    }

    [Fact]
    public void The_new_payments_not_ready_code_has_its_own_message()
    {
        Assert.Equal(BookFlowText.Errors.PaymentsNotReady, BookFailure.Of(new ApiFailure(422, "practitioner_payments_not_ready")).Text);
        Assert.NotEqual(BookFlowText.Errors.Generic, BookFlowText.Errors.PaymentsNotReady);
        Assert.NotEqual(BookFlowText.Errors.NotBookable, BookFlowText.Errors.PaymentsNotReady);
    }

    // ---- idempotency

    [Fact]
    public void The_key_is_kept_for_the_same_body_and_replaced_when_the_body_differs()
    {
        var offering = Guid.NewGuid();
        var start = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);
        var first = BookingAttempt.Fingerprint(offering, start, "Opis problemu z integracją", null, false);
        var key = Guid.NewGuid();
        Assert.Equal(key, BookingAttempt.KeyFor(key.ToString(), first, first, () => throw new InvalidOperationException("must reuse")));
        var edited = BookingAttempt.Fingerprint(offering, start, "Inny opis problemu z integracją", null, false);
        Assert.NotEqual(first, edited);
        var fresh = Guid.NewGuid();
        Assert.Equal(fresh, BookingAttempt.KeyFor(key.ToString(), first, edited, () => fresh)); // same key with another body would be a 409
        Assert.Equal(fresh, BookingAttempt.KeyFor(null, null, first, () => fresh));
        Assert.Equal(fresh, BookingAttempt.KeyFor("not-a-guid", first, first, () => fresh));
        Assert.Equal(fresh, BookingAttempt.KeyFor(Guid.Empty.ToString(), first, first, () => fresh));
    }

    [Fact]
    public void The_fingerprint_ignores_surrounding_blanks_and_covers_every_part_of_the_request()
    {
        var o = Guid.NewGuid();
        var t = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);
        var baseline = BookingAttempt.Fingerprint(o, t, "tekst", "audyt", true);
        Assert.Equal(baseline, BookingAttempt.Fingerprint(o, t, "  tekst ", "audyt", true));
        Assert.NotEqual(baseline, BookingAttempt.Fingerprint(Guid.NewGuid(), t, "tekst", "audyt", true));
        Assert.NotEqual(baseline, BookingAttempt.Fingerprint(o, t.AddMinutes(30), "tekst", "audyt", true));
        Assert.NotEqual(baseline, BookingAttempt.Fingerprint(o, t, "tekst", "inny", true));
        Assert.NotEqual(baseline, BookingAttempt.Fingerprint(o, t, "tekst", "audyt", false));
        Assert.DoesNotContain("tekst", baseline); // a hash: the brief itself never goes back to the browser in a field
    }

    // ---- the hold

    [Theory]
    [InlineData(600, "10:00")] [InlineData(598.2, "9:59")] [InlineData(59.01, "1:00")] [InlineData(5, "0:05")] [InlineData(0, "0:00")] [InlineData(-30, "0:00")]
    public void The_clock_rounds_up_and_is_never_negative(double seconds, string text) => Assert.Equal(text, HoldCountdown.Clock(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Time_left_is_the_expiry_minus_now_and_never_negative()
    {
        var now = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromMinutes(10), HoldCountdown.Left(now.AddMinutes(10), now));
        Assert.Equal(TimeSpan.Zero, HoldCountdown.Left(now.AddSeconds(-1), now));
        Assert.True(HoldCountdown.Expired(TimeSpan.Zero));
        Assert.False(HoldCountdown.Expired(TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData(601, HoldCountdown.Phase.Plenty)] [InlineData(301, HoldCountdown.Phase.Plenty)] [InlineData(300, HoldCountdown.Phase.FiveMinutes)] [InlineData(61, HoldCountdown.Phase.FiveMinutes)]
    [InlineData(60, HoldCountdown.Phase.OneMinute)] [InlineData(1, HoldCountdown.Phase.OneMinute)] [InlineData(0, HoldCountdown.Phase.Expired)]
    public void A_screen_reader_is_told_at_five_minutes_and_at_one_minute_not_every_second(double seconds, HoldCountdown.Phase phase) => Assert.Equal(phase, HoldCountdown.PhaseOf(TimeSpan.FromSeconds(seconds)));

    // ---- how a booking is described

    [Fact]
    public void A_meta_line_never_starts_or_ends_with_a_separator_F03()
    {
        Assert.Equal("poniedziałek, 23 listopada · 180,00\u00a0zł", BookingFormat.MetaLine(null, "poniedziałek, 23 listopada", "180,00\u00a0zł")); // the audit's "· poniedziałek..." has no name
        Assert.Equal("Anna · wtorek", BookingFormat.MetaLine("Anna", "", "wtorek", "   "));
        Assert.Equal("", BookingFormat.MetaLine(null, ""));
        Assert.Null(BookingFormat.Name("   "));
        Assert.Equal("Anna", BookingFormat.Name(" Anna "));
    }

    [Fact]
    public void A_range_is_shown_in_warsaw_time_whatever_the_server_zone()
    {
        // 12:00 UTC on 1 December is 13:00 in Warsaw (CET); 12:00 UTC on 1 July is 14:00 (CEST).
        Assert.Equal("wtorek, 1 grudnia, 13:00–14:00", BookingFormat.When(new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 1, 13, 0, 0, TimeSpan.Zero)));
        Assert.Equal("środa, 1 lipca, 14:00–14:30", BookingFormat.When(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 1, 12, 30, 0, TimeSpan.Zero)));
    }

    // ---- badges (G-8)

    [Theory]
    [InlineData("PendingPayment", "Oczekuje na płatność", BadgeKind.Warning)] [InlineData("Confirmed", "Potwierdzona", BadgeKind.Info)] [InlineData("Completed", "Zakończona", BadgeKind.Success)]
    [InlineData("CancelledByClient", "Anulowana przez klienta", BadgeKind.Neutral)] [InlineData("LateCancelledByClient", "Anulowana przez klienta (bez zwrotu)", BadgeKind.Warning)]
    [InlineData("CancelledByPractitioner", "Anulowana przez programistę", BadgeKind.Danger)] [InlineData("Expired", "Wygasła", BadgeKind.Neutral)] [InlineData("ClientNoShow", "Klient nie stawił się", BadgeKind.Danger)]
    [InlineData("Disputed", "Spór w toku", BadgeKind.Warning)] [InlineData("Resolved", "Spór rozstrzygnięty", BadgeKind.Neutral)] [InlineData("Whatever", "Status nieznany", BadgeKind.Neutral)]
    public void A_booking_state_has_a_label_and_a_kind(string status, string label, BadgeKind kind) => Assert.Equal((label, kind), (Badge.ForBooking(status).Label, Badge.ForBooking(status).Kind));

    [Theory]
    [InlineData("Succeeded", "Opłacona", BadgeKind.Success)] [InlineData("Refunded", "Zwrócona w całości", BadgeKind.Info)] [InlineData("PartiallyRefunded", "Zwrócona częściowo", BadgeKind.Info)]
    [InlineData("Created", "Płatność rozpoczęta", BadgeKind.Warning)] [InlineData("Failed", "Płatność nieudana", BadgeKind.Danger)] [InlineData("?", "Stan płatności nieznany", BadgeKind.Neutral)]
    public void A_payment_state_has_a_label_and_a_kind(string status, string label, BadgeKind kind) => Assert.Equal((label, kind), (Badge.ForPayment(status).Label, Badge.ForPayment(status).Kind));

    [Fact]
    public void Every_booking_status_the_contract_has_is_mapped_and_the_css_class_follows_the_kind()
    {
        foreach (var s in BookingFormat.Statuses) Assert.NotEqual(Pl.Bookings.UnknownStatus, Badge.ForBooking(s).Label);
        Assert.Equal("badge badge--warning", Badge.ForBooking("Disputed").CssClass);
    }

    // ---- what cancelling means (F-11)

    static readonly MoneyDto Price = new(18000, "PLN");
    static CancelOutcomeDto Outcome(string kind, long refund) => new(kind, new MoneyDto(refund, "PLN"));

    [Fact]
    public void Without_a_cancel_outcome_the_dialog_says_the_general_rule_and_computes_nothing()
    {
        Assert.Null(CancelOutcome.From(null, Price));
        Assert.Equal(BookingsText.Cancel.ClientRule, CancelOutcome.Sentence(null, "client"));
        Assert.Equal(BookingsText.Cancel.PractitionerRule, CancelOutcome.Sentence(null, "practitioner"));
    }

    [Theory]
    [InlineData("full_refund", 18000)] [InlineData("partial", 9000)] [InlineData("no_refund", 0)]
    public void With_a_cancel_outcome_the_dialog_says_the_amount_the_service_computed(string kind, long refund)
    {
        var o = CancelOutcome.From(Outcome(kind, refund), Price)!;
        Assert.Equal(refund, o.RefundMinor);
        var sentence = CancelOutcome.Sentence(o, "client");
        Assert.NotEqual(BookingsText.Cancel.ClientRule, sentence);
        if (refund == 18000) Assert.Equal("Otrzymasz zwrot 180,00\u00a0zł.", sentence);
        else if (refund == 0) Assert.Equal(BookingsText.Cancel.OutcomeNone, sentence);
        else Assert.Equal("Otrzymasz zwrot 90,00\u00a0zł z 180,00\u00a0zł.", sentence);
    }

    [Theory]
    [InlineData(-1)] [InlineData(18001)]
    public void A_refund_outside_zero_to_the_price_is_not_believed_and_the_general_rule_stays(long refund) => Assert.Null(CancelOutcome.From(Outcome("partial", refund), Price));

    [Fact]
    public void A_practitioner_is_told_the_client_is_refunded_and_that_they_are_not_paid()
    {
        var o = CancelOutcome.From(Outcome("full_refund", 18000), Price);
        Assert.Equal("Klient dostanie zwrot 180,00\u00a0zł, a Ty nie otrzymasz wypłaty za tę konsultację.", CancelOutcome.Sentence(o, "practitioner"));
    }

    // ---- failures of the client area

    [Theory]
    [InlineData(0, "unavailable", "Nie udało się połączyć z usługą. Spróbuj ponownie.")] [InlineData(409, "invalid_transition", "To nie jest już możliwe: stan rezerwacji się zmienił. Odśwież stronę.")]
    [InlineData(503, "dependency_unavailable", "Usługa rezerwacji jest chwilowo niedostępna. Spróbuj ponownie za chwilę.")] [InlineData(404, "not_found", "Nie znaleziono takiej rezerwacji.")]
    [InlineData(422, "validation_failed", "Sprawdź wypełnione pola i spróbuj ponownie.")] [InlineData(403, "forbidden", "Nie masz dostępu do tej rezerwacji.")]
    [InlineData(429, "rate_limited", "Zbyt wiele prób. Odczekaj chwilę i spróbuj ponownie.")] [InlineData(500, "http_500", "Coś poszło nie tak. Spróbuj ponownie.")]
    public void A_failed_action_is_told_from_its_code(int status, string code, string text) => Assert.Equal(text, ActionFailure.Text(new ApiFailure(status, code)));

    [Fact]
    public void Only_a_changed_state_offers_a_refresh() { Assert.True(ActionFailure.OffersRefresh(new ApiFailure(409, "invalid_transition"))); Assert.False(ActionFailure.OffersRefresh(new ApiFailure(500, "x"))); }

    [Theory]
    [InlineData(9, false)] [InlineData(10, true)] [InlineData(1000, true)] [InlineData(1001, false)]
    public void A_dispute_text_is_10_to_1000_characters(int n, bool ok) => Assert.Equal(ok, DisputeRules.TextProblem(new string('x', n)) is null);

    [Fact]
    public void Dispute_kinds_and_cancel_reasons_are_checked()
    {
        Assert.All(DisputeRules.Kinds, k => Assert.Null(DisputeRules.KindProblem(k)));
        Assert.NotNull(DisputeRules.KindProblem("fraud"));
        Assert.NotNull(DisputeRules.KindProblem(null));
        Assert.Null(DisputeRules.ReasonProblem(null));
        Assert.Null(DisputeRules.ReasonProblem(new string('a', 500)));
        Assert.NotNull(DisputeRules.ReasonProblem(new string('a', 501)));
        Assert.All(DisputeRules.Kinds, k => Assert.DoesNotContain("_", DisputeRules.KindLabel(k)));
    }

    // ---- reviews

    [Theory]
    [InlineData(null, false)] [InlineData(0, false)] [InlineData(1, true)] [InlineData(5, true)] [InlineData(6, false)]
    public void A_rating_is_one_to_five(int? rating, bool ok) => Assert.Equal(ok, ReviewRules.RatingProblem(rating) is null);

    [Theory]
    [InlineData(19, false)] [InlineData(20, true)] [InlineData(2000, true)] [InlineData(2001, false)]
    public void A_review_is_20_to_2000_characters(int n, bool ok) => Assert.Equal(ok, ReviewRules.TextProblem(new string('x', n)) is null);

    [Theory]
    [InlineData(9, false)] [InlineData(10, true)] [InlineData(1000, true)] [InlineData(1001, false)]
    public void A_reply_is_10_to_1000_characters(int n, bool ok) => Assert.Equal(ok, ReviewRules.ReplyProblem(new string('x', n)) is null);

    [Fact]
    public void Only_an_explicit_published_is_announced_as_published()
    {
        Assert.True(ReviewRules.IsPublished("Published"));
        Assert.False(ReviewRules.IsPublished("Held"));
        Assert.False(ReviewRules.IsPublished(null));
        Assert.False(ReviewRules.IsPublished("published")); // an unknown spelling never promises publication
    }

    [Fact]
    public void Each_rating_has_its_word() => Assert.Equal(["Słabo", "Przeciętnie", "Dobrze", "Bardzo dobrze", "Świetnie"], ReviewsText.Scale);

    [Fact]
    public void In_a_fake_profile_the_mock_checkout_of_another_origin_is_followed_as_a_path_of_this_site()
    {
        const string url = "http://localhost:3000/checkout/mock/018f0000-0000-7000-8000-000000000001";
        Assert.Equal("/checkout/mock/018f0000-0000-7000-8000-000000000001", CheckoutTarget.Safe(url, "http://localhost:3001", mockPayments: true));
        Assert.Null(CheckoutTarget.Safe(url, "http://localhost:3001")); // deployed: refused, as before
        Assert.Null(CheckoutTarget.Safe("http://evil.example/checkout/mock/018f0000-0000-7000-8000-000000000001?next=x", "http://localhost:3001", mockPayments: true)); // a query: refused
        Assert.Null(CheckoutTarget.Safe("http://evil.example/somewhere/else", "http://localhost:3001", mockPayments: true));
        Assert.Null(CheckoutTarget.Safe("javascript:alert(1)", "http://localhost:3001", mockPayments: true));
    }
}
