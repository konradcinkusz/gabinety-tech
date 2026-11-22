using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Admin;
using Gabinety.Web.Text;

namespace Gabinety.Web.Tests.Admin;

/// <summary>The plain-C# logic of the admin area, no renderer (BLAZOR-WEB-APP section 10). Each rule has the case just inside and the case just outside it.</summary>
public class AdminLogicTests
{
    // ---- the reason / note rule: 10 to 500 after trimming (#125, ADR-0012)
    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void A_reason_is_10_to_500_characters(int length, bool valid) => Assert.Equal(valid, TextRule.IsValid(new string('x', length)));

    [Fact]
    public void A_reason_is_judged_after_trimming_so_padding_does_not_make_it_longer()
    {
        Assert.False(TextRule.IsValid("   short   "));
        Assert.True(TextRule.IsValid("  " + new string('x', 10) + "  "));
        Assert.True(TextRule.IsValid(" " + new string('x', 500) + " "));
        Assert.False(TextRule.IsValid(null));
        Assert.False(TextRule.IsValid("         "));
    }

    // ---- amounts: integer arithmetic, never rounded
    [Theory]
    [InlineData("12,50", 1250)]
    [InlineData("12.50", 1250)]
    [InlineData("12", 1200)]
    [InlineData("12,5", 1250)]
    [InlineData("0,01", 1)]
    [InlineData("  100,00 ", 10000)]
    [InlineData("999999999", 99999999900)]
    public void An_amount_is_parsed_into_grosz_exactly(string text, long grosz) => Assert.Equal((AmountProblem.None, grosz), PlnAmount.Parse(text));

    [Theory]
    [InlineData("", AmountProblem.Format)]
    [InlineData("abc", AmountProblem.Format)]
    [InlineData("12,", AmountProblem.Format)]
    [InlineData(",50", AmountProblem.Format)]
    [InlineData("-5", AmountProblem.Format)]
    [InlineData("1,2,3", AmountProblem.Format)]
    [InlineData("1 000", AmountProblem.Format)]
    [InlineData("1234567890", AmountProblem.Format)]
    [InlineData("12,505", AmountProblem.Decimals)]
    [InlineData("12.999", AmountProblem.Decimals)]
    public void A_malformed_amount_is_refused_and_more_than_two_decimals_is_never_rounded(string text, AmountProblem expected) => Assert.Equal(expected, PlnAmount.Parse(text).Problem);

    [Theory]
    [InlineData(0, 18000, AmountProblem.Range)]
    [InlineData(1, 18000, AmountProblem.None)]
    [InlineData(17999, 18000, AmountProblem.None)]
    [InlineData(18000, 18000, AmountProblem.Range)] // the whole price is "refund_full", not a partial refund
    [InlineData(18001, 18000, AmountProblem.Range)]
    public void A_partial_refund_is_strictly_between_zero_and_the_price(long grosz, long price, AmountProblem expected) => Assert.Equal(expected, PlnAmount.Range(grosz, price));

    [Fact]
    public void Without_a_known_price_only_the_lower_bound_is_checked() { Assert.Equal(AmountProblem.None, PlnAmount.Range(99999999, null)); Assert.Equal(AmountProblem.Range, PlnAmount.Range(0, null)); }

    // ---- the dispute form
    static DisputeResolveForm Form(string? resolution, string amount = "", string note = "Notatka o dziesięciu znakach", long? price = 18000) => DisputeResolveForm.From(resolution, amount, note, price);

    [Fact]
    public void Nothing_chosen_says_so() => Assert.Equal([("resolution", PlAdmin.Disputes.Pick)], Form(null).Validate().Select(p => (p.Field, p.Message)));

    [Fact]
    public void An_unknown_resolution_is_not_a_choice() => Assert.Contains(Form("refund_everything").Validate(), p => p.Field == "resolution");

    [Theory]
    [InlineData("release")]
    [InlineData("refund_full")]
    public void A_release_and_a_full_refund_need_a_note_and_nothing_else(string resolution)
    {
        Assert.Empty(Form(resolution).Validate());
        Assert.Equal([("note", PlAdmin.Disputes.NoteInvalid)], Form(resolution, note: "krótka").Validate().Select(p => (p.Field, p.Message)));
        Assert.Empty(Form(resolution, amount: "garbage").Validate()); // the amount belongs to a partial refund only
    }

    [Fact]
    public void A_partial_refund_needs_an_amount_inside_the_price_and_names_the_price_when_it_is_known()
    {
        Assert.Empty(Form("refund_partial", "100,00").Validate());
        var range = Assert.Single(Form("refund_partial", "180,00").Validate());
        Assert.Equal("amount", range.Field);
        Assert.Contains("180,00", range.Message);
        Assert.Equal(PlAdmin.Disputes.AmountFormat, Assert.Single(Form("refund_partial", "sto").Validate()).Message);
        Assert.Equal(PlAdmin.Disputes.AmountDecimals, Assert.Single(Form("refund_partial", "1,234").Validate()).Message);
        Assert.Equal(PlAdmin.Disputes.AmountRange, Assert.Single(Form("refund_partial", "0", price: null).Validate()).Message); // price unknown: the generic range text
    }

    [Fact]
    public void Every_problem_of_the_form_is_reported_at_once_in_field_order() =>
        Assert.Equal(["amount", "note"], Form("refund_partial", "x", note: "").Validate().Select(p => p.Field));

    [Fact]
    public void The_confirm_step_words_the_money_for_each_outcome()
    {
        Assert.Contains("180,00", Form("refund_full").Consequence());
        Assert.Contains("Programista nie otrzyma nic", Form("refund_full").Consequence());
        Assert.Contains("nie dostanie zwrotu", Form("release").Consequence());
        var partial = Form("refund_partial", "100,00").Consequence();
        Assert.Contains("100,00", partial);
        Assert.Contains("80,00", partial); // the rest the practitioner keeps (before the fee)
        Assert.DoesNotContain("180", Form("refund_partial", "100,00", price: null).Consequence()); // no price: no figure for the rest
        Assert.Contains("100,00", Form("refund_partial", "100,00", price: null).Consequence());
    }

    [Fact]
    public void Only_a_partial_refund_sends_an_amount()
    {
        Assert.Equal(10000, Form("refund_partial", "100").RefundToSend);
        Assert.Null(Form("release").RefundToSend);
        Assert.Null(Form("refund_full").RefundToSend);
    }

    // ---- the ledger period
    static readonly DateTimeOffset Now = new(2026, 12, 15, 23, 59, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("2026-12-01", "2026-12-15", PeriodProblem.None)]
    [InlineData("2026-12-15", "2026-12-15", PeriodProblem.None)] // one day, both ends inclusive
    [InlineData("2026-12-15", "2026-12-14", PeriodProblem.Order)]
    [InlineData("2026-02-30", "2026-03-01", PeriodProblem.Format)] // not a real date
    [InlineData("2026-12-1", "2026-12-15", PeriodProblem.Format)]
    [InlineData("", "", PeriodProblem.Format)]
    [InlineData("2026-01-01", "2026-12-31", PeriodProblem.None)] // 365 days
    [InlineData("2028-01-01", "2028-12-31", PeriodProblem.None)] // 366 days in a leap year: the limit, inclusive
    [InlineData("2027-01-01", "2028-01-01", PeriodProblem.None)] // 365 + 1 = 366 days counting both: still the limit
    [InlineData("2027-01-01", "2028-01-02", PeriodProblem.Range)] // 367 days
    public void A_period_is_two_real_days_in_order_and_at_most_366_days_counting_both(string from, string to, PeriodProblem expected) => Assert.Equal(expected, LedgerPeriod.Check(from, to));

    [Fact]
    public void With_no_query_the_period_is_this_month_so_far_in_utc_from_the_clock_given()
    {
        var range = LedgerPeriod.Of(null, null, Now);
        Assert.Equal(("2026-12-01", "2026-12-15", PeriodProblem.None), (range.From, range.To, range.Problem));
        Assert.Equal(PeriodProblem.Format, LedgerPeriod.Of("2026-12-01", null, Now).Problem); // one end only is an error, not a default
        Assert.Equal(PeriodProblem.Format, LedgerPeriod.Of(null, "2026-12-01", Now).Problem);
    }

    [Fact]
    public void The_presets_are_this_month_last_month_and_this_year_and_cross_the_year_boundary()
    {
        var presets = LedgerPeriod.Presets(Now);
        Assert.Equal([("thisMonth", "2026-12-01", "2026-12-15"), ("lastMonth", "2026-11-01", "2026-11-30"), ("thisYear", "2026-01-01", "2026-12-15")], presets.Select(p => (p.Id, p.From, p.To)));
        var january = LedgerPeriod.Presets(new DateTimeOffset(2027, 1, 3, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(("2026-12-01", "2026-12-31"), (january[1].From, january[1].To));
        Assert.All(presets, p => Assert.Equal(PeriodProblem.None, LedgerPeriod.Check(p.From, p.To)));
    }

    [Fact]
    public void The_day_is_the_utc_day_not_the_local_one() =>
        Assert.Equal("2026-12-15", LedgerPeriod.Presets(new DateTimeOffset(2026, 12, 16, 0, 30, 0, TimeSpan.FromHours(2)))[0].To); // 00:30 in Warsaw is still the 15th in UTC

    [Fact]
    public void The_file_name_is_built_from_the_validated_days() => Assert.Equal("ledger-2026-12-01-2026-12-15.csv", LedgerPeriod.FileName("2026-12-01", "2026-12-15"));

    // ---- formatting
    [Fact]
    public void A_date_is_short_without_seconds_and_in_warsaw_time()
    {
        Assert.Equal("24 lis 2026, 13:00", AdminFormat.When(new DateTimeOffset(2026, 11, 24, 12, 0, 9, TimeSpan.Zero))); // winter: UTC+1, seconds dropped
        Assert.Equal("1 lip 2026, 14:05", AdminFormat.When(new DateTimeOffset(2026, 7, 1, 12, 5, 0, TimeSpan.Zero))); // summer: UTC+2
        Assert.Null(AdminFormat.When((DateTimeOffset?)null));
    }

    [Fact]
    public void A_reference_is_eight_characters_and_a_guid_is_never_the_whole_text()
    {
        var id = Guid.Parse("0b9e1d2c-aaaa-bbbb-cccc-1234567890ab");
        Assert.Equal("0b9e1d2c", AdminFormat.ShortRef(id));
        Assert.Equal("demo-cli", AdminFormat.ShortRef("demo-client-2"));
        Assert.Equal("abc", AdminFormat.ShortRef("abc"));
    }

    [Fact]
    public void An_unknown_status_is_never_shown_as_its_wire_value()
    {
        Assert.Equal(PlAdmin.Common.Unknown, AdminFormat.PractitionerStatus("Banana"));
        Assert.Equal(PlAdmin.Verification.EventUnknown, AdminFormat.VerificationEvent("Banana"));
        Assert.Equal(PlAdmin.Disputes.KindUnknown, AdminFormat.DisputeKind("Banana"));
        Assert.Equal(PlAdmin.Reviews.TriageUnknown, AdminFormat.Triage("Banana"));
        Assert.Equal(PlAdmin.Reviews.ReasonOther, AdminFormat.HoldReason("Banana"));
        Assert.Equal(PlAdmin.Ledger.KindUnknown, AdminFormat.LedgerKind("Banana"));
    }

    [Theory]
    [InlineData("Approved", BadgeKind.Success)]
    [InlineData("Submitted", BadgeKind.Warning)]
    [InlineData("Rejected", BadgeKind.Danger)]
    [InlineData("Suspended", BadgeKind.Danger)]
    [InlineData("Draft", BadgeKind.Neutral)]
    public void Verification_states_get_the_badge_kind_of_the_design_contract(string status, BadgeKind kind) => Assert.Equal(kind, AdminFormat.PractitionerStatusKind(status));

    [Fact]
    public void A_link_is_only_made_from_an_http_address_and_a_plausible_github_login()
    {
        Assert.True(AdminFormat.IsHttp("https://demo.example/portfolio"));
        Assert.False(AdminFormat.IsHttp("javascript:alert(1)"));
        Assert.False(AdminFormat.IsHttp("ftp://demo.example"));
        Assert.False(AdminFormat.IsHttp(null));
        Assert.True(AdminFormat.IsGithubLogin("demo-dev-1"));
        Assert.False(AdminFormat.IsGithubLogin("a/b"));
        Assert.False(AdminFormat.IsGithubLogin(new string('a', 40)));
    }

    [Fact]
    public void A_negative_amount_keeps_its_sign() { Assert.StartsWith("−", AdminFormat.Pln(-500)); Assert.Equal("1" + (char)0xA0 + "000,00" + (char)0xA0 + "zł", AdminFormat.Pln(100000)); }

    // ---- failures
    [Theory]
    [InlineData(0, "Nie udało się połączyć")]
    [InlineData(401, "Sesja wygasła")]
    [InlineData(403, "Nie masz uprawnień")]
    [InlineData(404, "Nie znaleziono")]
    [InlineData(429, "Zbyt wiele")]
    [InlineData(500, "Nie udało się wykonać")]
    public void A_failure_is_worded_from_its_status_only(int status, string contains) => Assert.Contains(contains, AdminFailure.Text(new ApiFailure(status, "any_code_SERVER-TEXT")));

    [Fact]
    public void A_conflict_is_recognised_as_such() { Assert.True(AdminFailure.IsConflict(new ApiFailure(409, "invalid_transition"))); Assert.False(AdminFailure.IsConflict(new ApiFailure(422, "x"))); }

    // ---- posted form names
    [Fact]
    public void A_form_name_round_trips_and_anything_else_is_not_ours()
    {
        var id = Guid.NewGuid();
        Assert.Equal(("approve", id), PostedForm.Parse(PostedForm.Name("approve", id)));
        Assert.Equal(("reject-open", id), PostedForm.Parse(PostedForm.Name("reject-open", id)));
        Assert.Null(PostedForm.Parse("approve"));
        Assert.Null(PostedForm.Parse("approve-not-a-guid"));
        Assert.Null(PostedForm.Parse(null));
        Assert.Null(PostedForm.Parse(""));
    }

    // ---- the ledger CSV row of the contract
    [Fact]
    public void A_ledger_row_is_read_in_the_contracts_shape_and_a_line_of_another_shape_is_skipped()
    {
        var row = LedgerCsv.TryParseRow("0b9e1d2c-1111-2222-3333-444455556666,ClientCharge,18000,PLN,2026-12-01T10:00:00.1234567Z");
        Assert.NotNull(row);
        Assert.Equal(("ClientCharge", 18000L, "PLN"), (row.Kind, row.AmountMinor, row.Currency));
        Assert.Equal(new DateTime(2026, 12, 1, 10, 0, 0, DateTimeKind.Utc).AddTicks(1234567), row.CreatedAt.UtcDateTime);
        Assert.Null(LedgerCsv.TryParseRow("not,a,row"));
        Assert.Null(LedgerCsv.TryParseRow("x,ClientCharge,18000,PLN,2026-12-01T10:00:00Z"));
        Assert.Null(LedgerCsv.TryParseRow("0b9e1d2c-1111-2222-3333-444455556666,ClientCharge,18000.5,PLN,2026-12-01T10:00:00Z"));
        Assert.Null(LedgerCsv.TryParseRow(""));
    }
}
