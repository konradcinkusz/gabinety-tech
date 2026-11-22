using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.PractitionerArea;
using Gabinety.Web.Components.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Gabinety.Web.Tests.PractitionerAreaTests;

/// <summary>The plain-C# state and rules of the practitioner area, tested without a renderer (BLAZOR-WEB-APP section 10).</summary>
public class PriceInputTests
{
    [Theory]
    [InlineData("250", 25000)] [InlineData("250,50", 25050)] [InlineData("250.5", 25050)] [InlineData(" 180 ", 18000)] [InlineData("250,05", 25005)] [InlineData("50", 5000)] [InlineData("1500", 150000)]
    public void A_price_in_zloty_becomes_integer_grosz_without_a_float(string input, long grosz)
    {
        Assert.Equal((PriceProblem.None, grosz), PriceInput.Parse(input));
    }

    [Theory]
    [InlineData("")] [InlineData("abc")] [InlineData("250,555")] [InlineData("-5")] [InlineData("2 50")] [InlineData("250 zł")] [InlineData("1e3")]
    public void Text_that_is_not_a_price_is_a_format_problem(string input) => Assert.Equal(PriceProblem.Format, PriceInput.Parse(input).Problem);

    [Theory]
    [InlineData("49,99")] [InlineData("1500,01")] [InlineData("0")] [InlineData("9999999")]
    public void A_price_outside_50_to_1500_is_a_range_problem(string input) => Assert.Equal(PriceProblem.Range, PriceInput.Parse(input).Problem);

    [Theory] [InlineData(25000, "250")] [InlineData(25050, "250,50")] [InlineData(25005, "250,05")]
    public void The_field_value_round_trips(long grosz, string text)
    {
        Assert.Equal(text, PriceInput.ToInput(grosz));
        Assert.Equal(grosz, PriceInput.Parse(text).Grosz);
    }

    [Fact] public void The_price_is_shown_with_the_money_helper() => Assert.Equal("250,50 zł", Money.Pln(25050).Replace(' ', ' '));
}

public class ChecklistTests
{
    static PractitionerPrivateDto Profile(string headline = "", string bio = "", bool github = false, bool nip = false, string? portfolio = null, bool email = false, string status = "Draft") =>
        new(Guid.NewGuid(), "demo-x", status, null, "Demo X", headline, bio, portfolio, null, null, email, github, nip, null);

    static OfferingPrivateDto Offer(bool active) => new(Guid.NewGuid(), "Oferta", "", 30, new MoneyDto(10000, "PLN"), "code-review", [], active, null);

    [Fact]
    public void An_empty_draft_has_seven_open_steps_in_the_order_of_the_contract()
    {
        var items = OnboardingChecklist.Build(Profile(), []);
        Assert.Equal(["Nagłówek profilu", "Opis (co najmniej 200 znaków)", "Co najmniej jedna aktywna oferta", "Połączone konto GitHub", "Poprawny numer NIP", "Link do portfolio", "Potwierdzony adres e-mail"], items.Select(i => i.Title));
        Assert.All(items, i => Assert.False(i.Done));
        Assert.Equal(7, OnboardingChecklist.Remaining(items));
    }

    [Fact]
    public void A_step_is_done_only_when_the_service_data_says_so()
    {
        var bio199 = new string('a', 199);
        Assert.False(OnboardingChecklist.Build(Profile(bio: bio199), [])[1].Done);
        Assert.True(OnboardingChecklist.Build(Profile(bio: bio199 + "a"), [])[1].Done);
        Assert.False(OnboardingChecklist.Build(Profile(headline: "   "), [])[0].Done);
        Assert.False(OnboardingChecklist.Build(Profile(), [Offer(false)])[2].Done); // an inactive offer does not count
        Assert.True(OnboardingChecklist.Build(Profile(), [Offer(false), Offer(true)])[2].Done);
        var all = OnboardingChecklist.Build(Profile("Naglowek", new string('a', 200), true, true, "https://x.example.com/", true), [Offer(true)]);
        Assert.Equal(0, OnboardingChecklist.Remaining(all));
    }

    [Fact]
    public void Every_open_step_says_where_it_is_done_except_the_two_that_are_done_elsewhere()
    {
        var hrefs = OnboardingChecklist.Build(Profile(), []).ToDictionary(i => i.Key, i => i.Href);
        Assert.Equal("/gabinet-programisty/profil", hrefs[ChecklistKey.Headline]);
        Assert.Equal("/gabinet-programisty/oferty", hrefs[ChecklistKey.Offering]);
        Assert.Null(hrefs[ChecklistKey.Github]); // on the dashboard itself
        Assert.Null(hrefs[ChecklistKey.Email]);  // the link in the e-mail
    }

    [Fact]
    public void The_unmet_items_of_a_refused_submit_map_to_the_checklist_keys_and_unknown_words_are_dropped()
    {
        Assert.Equal([ChecklistKey.Headline, ChecklistKey.Offering, ChecklistKey.Github, ChecklistKey.Portfolio], OnboardingChecklist.FromServer(["portfolioUrl", "github_linked", "something_new", "headline", "offering", "offering"]));
    }

    [Theory] [InlineData("Draft", true)] [InlineData("Rejected", true)] [InlineData("Submitted", false)] [InlineData("Approved", false)] [InlineData("Suspended", false)] [InlineData("Whatever", false)]
    public void Only_a_draft_or_a_rejected_profile_can_be_submitted(string status, bool can) => Assert.Equal(can, OnboardingChecklist.CanBeSubmitted(status));
}

public class NextStepsTests
{
    static PractitionerPrivateDto Profile(string status) => new(Guid.NewGuid(), "demo-x", status, null, "Demo X", "", "", null, null, null, false, false, false, null);
    static OfferingPrivateDto Offer(bool active) => new(Guid.NewGuid(), "Oferta", "", 30, new MoneyDto(10000, "PLN"), "code-review", [], active, null);

    [Fact]
    public void The_order_is_verification_offers_availability_payments_and_the_first_open_one_is_next()
    {
        var steps = NextSteps.Build(Profile("Draft"), [], 0, PaymentsState.None);
        Assert.Equal(["Weryfikacja profilu", "Oferty", "Dostępność", "Płatności"], steps.Select(s => s.Title));
        Assert.Equal("verification", NextSteps.Next(steps)!.Key);
        steps = NextSteps.Build(Profile("Approved"), [], 0, PaymentsState.None);
        Assert.Equal("offers", NextSteps.Next(steps)!.Key);
        steps = NextSteps.Build(Profile("Approved"), [Offer(true)], 0, PaymentsState.None);
        Assert.Equal("availability", NextSteps.Next(steps)!.Key);
        steps = NextSteps.Build(Profile("Approved"), [Offer(true)], 3, PaymentsState.Pending);
        Assert.Equal("payments", NextSteps.Next(steps)!.Key);
        steps = NextSteps.Build(Profile("Approved"), [Offer(true)], 3, PaymentsState.Ready);
        Assert.Null(NextSteps.Next(steps));
    }

    [Fact]
    public void An_inactive_offer_and_unknown_availability_are_not_done()
    {
        var steps = NextSteps.Build(Profile("Approved"), [Offer(false)], null, PaymentsState.Ready);
        Assert.False(steps[1].Done);
        Assert.False(steps[2].Done);
    }

    [Fact]
    public void The_demo_provider_needs_nothing_and_an_unreadable_answer_is_never_the_next_step()
    {
        var demo = NextSteps.Build(Profile("Approved"), [Offer(true)], 1, PaymentsState.NotApplicable);
        Assert.True(demo[3].Done);
        var unknown = NextSteps.Build(Profile("Approved"), [Offer(true)], 1, PaymentsState.Unknown);
        Assert.False(unknown[3].Done);
        Assert.Null(NextSteps.Next(unknown));
    }

    [Theory]
    [InlineData(PaymentsState.None, true)] [InlineData(PaymentsState.Pending, true)] [InlineData(PaymentsState.Ready, false)] [InlineData(PaymentsState.NotApplicable, false)] [InlineData(PaymentsState.Unknown, false)]
    public void The_banner_shows_only_for_a_real_provider_whose_account_is_not_ready(PaymentsState state, bool banner) => Assert.Equal(banner, NextSteps.ShowPaymentsBanner(state));

    [Fact]
    public void The_provider_answer_maps_to_a_state_and_a_404_means_the_demo_provider()
    {
        Assert.Equal(PaymentsState.Ready, NextSteps.PaymentsOf(ApiResult<PaymentAccountDto>.Ok(new("Stripe", "ready"))));
        Assert.Equal(PaymentsState.Pending, NextSteps.PaymentsOf(ApiResult<PaymentAccountDto>.Ok(new("Stripe", "pending"))));
        Assert.Equal(PaymentsState.None, NextSteps.PaymentsOf(ApiResult<PaymentAccountDto>.Ok(new("Stripe", "none"))));
        Assert.Equal(PaymentsState.NotApplicable, NextSteps.PaymentsOf(ApiResult<PaymentAccountDto>.Fail(404, "not_found")));
        Assert.Equal(PaymentsState.Unknown, NextSteps.PaymentsOf(ApiResult<PaymentAccountDto>.Fail(503, "dependency_unavailable")));
        Assert.Equal(PaymentsState.Unknown, NextSteps.PaymentsOf(ApiResult<PaymentAccountDto>.Fail(ApiFailure.NoAnswer)));
    }
}

public class AvailabilityDraftTests
{
    static AvailabilityDraft Draft(params (int Day, string From, string To)[] rules)
    {
        var d = new AvailabilityDraft();
        foreach (var r in rules) d.Rules.Add(new AvailabilityRuleDto(r.Day, r.From, r.To));
        return d;
    }

    static IReadOnlyList<AvailabilityIssueCode> Codes(AvailabilityDraft d) => [.. d.Validate().Select(i => i.Code)];

    [Fact]
    public void Copying_a_weekday_replaces_the_other_working_days_and_leaves_the_weekend_alone()
    {
        var d = Draft((1, "09:00", "12:00"), (1, "13:00", "15:00"), (3, "08:00", "09:00"), (6, "10:00", "11:00"));
        d.CopyToWeekdays(1);
        foreach (var day in new[] { 1, 2, 3, 4, 5 }) Assert.Equal([("09:00", "12:00"), ("13:00", "15:00")], d.Rules.Where(r => r.IsoWeekday == day).Select(r => (r.StartLocal, r.EndLocal)));
        Assert.Equal([("10:00", "11:00")], d.Rules.Where(r => r.IsoWeekday == 6).Select(r => (r.StartLocal, r.EndLocal)));
        Assert.Equal(11, d.Rules.Count);
        Assert.Empty(d.Validate());
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(0)]
    public void Only_a_working_day_with_intervals_is_a_source_for_copying(int source)
    {
        var d = Draft((1, "09:00", "12:00"), (6, "10:00", "11:00"));
        d.CopyToWeekdays(source);
        Assert.Equal(2, d.Rules.Count);
        d.CopyToWeekdays(2); // Tuesday has nothing: the week is not cleared
        Assert.Equal(2, d.Rules.Count);
    }

    [Fact]
    public void Copying_stops_at_the_row_limit_and_validation_still_reports_it()
    {
        var d = Draft();
        for (var i = 0; i < 8; i++) d.Rules.Add(new AvailabilityRuleDto(1, $"{8 + i:00}:00", $"{8 + i:00}:30"));
        d.CopyToWeekdays(1);
        Assert.Equal(AvailabilityDraft.MaxRules + 1, d.Rules.Count);
        Assert.Contains(AvailabilityIssueCode.TooManyRules, Codes(d));
    }

    [Fact]
    public void Adding_for_a_day_uses_that_day_and_an_unknown_day_falls_back_to_monday()
    {
        var d = Draft();
        d.AddRule(4);
        d.AddRule(99);
        Assert.Equal([4, 1], d.Rules.Select(r => r.IsoWeekday));
    }

    [Theory]
    [InlineData("2026-12-24", "czwartek, 24 grudnia 2026")]
    [InlineData("2027-02-03", "środa, 3 lutego 2027")]
    [InlineData("2027-02-30", "2027-02-30")]
    [InlineData("", "")]
    public void An_exception_date_reads_in_polish_and_an_invalid_one_is_left_as_typed(string iso, string expected) => Assert.Equal(expected, AvailabilityDraft.LongDate(iso));

    [Fact] public void A_sound_draft_has_no_issue() => Assert.Empty(Draft((1, "09:00", "12:00"), (1, "13:00", "17:30"), (3, "10:00", "11:00")).Validate());

    [Theory]
    [InlineData("09:15", "10:00")] [InlineData("09:00", "10:10")] [InlineData("9:00", "10:00")] [InlineData("09:00", "24:00")] [InlineData("", "10:00")]
    public void Times_must_be_on_the_hour_or_half_past_and_at_most_23_30(string from, string to) => Assert.Equal([AvailabilityIssueCode.NotHalfHour], Codes(Draft((1, from, to))));

    [Theory] [InlineData("10:00", "10:00")] [InlineData("11:00", "10:00")]
    public void The_end_must_be_after_the_start(string from, string to) => Assert.Equal([AvailabilityIssueCode.EndBeforeStart], Codes(Draft((1, from, to))));

    [Fact]
    public void Windows_of_one_weekday_must_not_overlap_but_may_touch_and_other_days_do_not_matter()
    {
        Assert.Empty(Draft((1, "09:00", "10:00"), (1, "10:00", "11:00")).Validate());
        Assert.Empty(Draft((1, "09:00", "10:00"), (2, "09:30", "10:30")).Validate());
        var issues = Draft((3, "09:00", "11:00"), (3, "10:30", "12:00")).Validate();
        var overlap = Assert.Single(issues);
        Assert.Equal((AvailabilityIssueCode.Overlap, 3), (overlap.Code, overlap.Weekday));
        Assert.Contains("środa", overlap.Message);
    }

    [Theory] [InlineData(0)] [InlineData(8)] [InlineData(-1)]
    public void A_weekday_is_1_to_7(int day) => Assert.Contains(AvailabilityIssueCode.BadWeekday, Codes(Draft((day, "09:00", "10:00"))));

    [Fact]
    public void A_row_issue_names_the_row_counting_from_one()
    {
        var issue = Assert.Single(Draft((1, "09:00", "10:00"), (2, "10:00", "09:00")).Validate());
        Assert.Equal(1, issue.Row);
        Assert.StartsWith("Przedział 2: ", issue.Message);
    }

    [Fact]
    public void At_most_28_windows_and_90_blocked_days()
    {
        var d = new AvailabilityDraft();
        for (var i = 0; i < 28; i++) d.Rules.Add(new AvailabilityRuleDto(i % 7 + 1, $"{i / 7 * 4 + 8:00}:00", $"{i / 7 * 4 + 9:00}:00"));
        Assert.Empty(d.Validate());
        d.AddRule();
        Assert.Contains(AvailabilityIssueCode.TooManyRules, Codes(d));
        var e = new AvailabilityDraft();
        for (var i = 0; i < 91; i++) e.Exceptions.Add(new DateOnly(2027, 1, 1).AddDays(i).ToString("yyyy-MM-dd"));
        Assert.Contains(AvailabilityIssueCode.TooManyExceptions, Codes(e));
    }

    [Fact]
    public void A_blocked_day_must_be_a_real_date_and_appear_once()
    {
        var d = new AvailabilityDraft();
        d.AddException("2027-02-30");
        Assert.Equal([AvailabilityIssueCode.BadDate], Codes(d));
        var twice = new AvailabilityDraft();
        twice.AddException("2027-03-01");
        twice.AddException("2027-03-01");
        Assert.Equal([AvailabilityIssueCode.DuplicateException], Codes(twice));
        var ok = new AvailabilityDraft();
        ok.AddException("2024-02-29");
        Assert.Empty(ok.Validate());
    }

    [Fact]
    public void The_draft_travels_in_the_form_and_the_commands_change_it()
    {
        var values = new Dictionary<string, string> { ["rule-0-day"] = "2", ["rule-0-from"] = "09:00", ["rule-0-to"] = "10:30", ["rule-1-day"] = "5", ["rule-1-from"] = "12:00", ["rule-1-to"] = "13:00", ["exception-0"] = "2027-01-06" };
        var d = AvailabilityDraft.FromForm(k => values.GetValueOrDefault(k));
        Assert.Equal(new AvailabilityRuleDto(2, "09:00", "10:30"), d.Rules[0]);
        Assert.Equal(["2027-01-06"], d.Exceptions);
        d.AddRule();
        Assert.Equal(3, d.Rules.Count);
        d.RemoveRule(0);
        Assert.Equal([5, 1], d.Rules.Select(r => r.IsoWeekday));
        d.RemoveRule(99); d.RemoveRule(-1); // out of range: nothing happens
        Assert.Equal(2, d.Rules.Count);
        d.AddException("  "); // blank: ignored
        d.RemoveException(0);
        Assert.Empty(d.Exceptions);
    }

    [Fact]
    public void A_hostile_form_cannot_make_the_draft_long_and_a_bad_number_becomes_an_issue_not_an_exception()
    {
        var d = AvailabilityDraft.FromForm(k => k.StartsWith("rule-") && k.EndsWith("-day") ? "x" : k.StartsWith("rule-") ? "09:00" : null);
        Assert.Equal(AvailabilityDraft.MaxRules + 1, d.Rules.Count);
        Assert.Contains(AvailabilityIssueCode.TooManyRules, Codes(d));
        Assert.Equal(0, d.Rules[0].IsoWeekday);
    }

    [Fact]
    public void A_loaded_availability_is_sorted_by_weekday_and_time_so_the_rows_read_as_a_week()
    {
        var d = AvailabilityDraft.From(new AvailabilityDto([new(3, "10:00", "11:00"), new(1, "13:00", "14:00"), new(1, "09:00", "10:00")], [new("2027-02-01"), new("2027-01-01")]));
        Assert.Equal([(1, "09:00"), (1, "13:00"), (3, "10:00")], d.Rules.Select(r => (r.IsoWeekday, r.StartLocal)));
        Assert.Equal(["2027-01-01", "2027-02-01"], d.Exceptions);
        Assert.Equal(48, AvailabilityDraft.Times.Length);
        Assert.Equal(["00:00", "00:30", "23:30"], [AvailabilityDraft.Times[0], AvailabilityDraft.Times[1], AvailabilityDraft.Times[^1]]);
    }

    [Fact]
    public void The_servers_row_fields_become_row_notes_without_any_server_text()
    {
        Assert.Equal(["Przedział 3: sprawdź dzień i godziny"], AvailabilityDraft.ServerRows(["rules[2].endLocal", "rules[2].startLocal", "rules", "exceptions"]));
    }
}

public class FieldErrorsTests
{
    [Theory]
    [InlineData("napisz do mnie: jan@example.com", true)] [InlineData("zadzwoń 600 700 800", true)] [InlineData("mój telegram", true)] [InlineData("pisz @janek", true)]
    [InlineData("Audyt kodu .NET od 2015 roku", false)] [InlineData("", false)] [InlineData(null, false)]
    public void The_contact_look_decides_where_to_put_the_error_only(string? text, bool contact) => Assert.Equal(contact, FieldErrors.LooksLikeContact(text));

    [Fact]
    public void A_field_the_service_names_gets_the_text_we_wrote_for_it_and_an_unknown_field_a_neutral_one()
    {
        var errors = FieldErrors.From(new ApiFailure(422, "validation_failed", ["headline", "nip", "brandNewField"]), new Dictionary<string, string?>());
        Assert.Equal("Nagłówek ma mieć od 10 do 120 znaków.", errors["headline"]);
        Assert.Equal("NIP ma mieć 10 cyfr i poprawną sumę kontrolną.", errors["nip"]);
        Assert.Equal("To pole jest nieprawidłowe.", errors["brandNewField"]);
    }

    [Fact]
    public void Contact_details_go_on_the_field_that_looks_like_it_else_on_every_free_text_field()
    {
        var failure = new ApiFailure(422, "contact_details_not_allowed");
        var one = FieldErrors.From(failure, new Dictionary<string, string?> { ["headline"] = "Audyt", ["bio"] = "pisz na jan@example.com" });
        Assert.Equal(["bio"], one.Keys);
        var both = FieldErrors.From(failure, new Dictionary<string, string?> { ["headline"] = "Audyt", ["bio"] = "tekst" });
        Assert.Equal(["bio", "headline"], both.Keys.Order());
    }

    [Theory]
    [InlineData(0, "Nie udało się połączyć z serwerem. Spróbuj ponownie.")] [InlineData(403, "Nie masz dostępu do tej części panelu.")] [InlineData(503, "Ta część serwisu jest chwilowo niedostępna. Spróbuj ponownie za chwilę.")] [InlineData(500, "inne")]
    public void A_failure_without_a_field_gets_one_line(int status, string expected) => Assert.Equal(expected, FieldErrors.General(new ApiFailure(status, "x"), "inne"));
}

public class FormValuesTests
{
    static FormValues Form(params (string, string[])[] f) => new(new FormCollection(f.ToDictionary(x => x.Item1, x => new StringValues(x.Item2))));

    [Fact]
    public void Values_are_trimmed_and_a_missing_field_is_null_or_empty_by_the_method_you_ask()
    {
        var f = Form(("a", ["  x  "]));
        Assert.Equal("x", f.Get("a"));
        Assert.Null(f.Get("b"));
        Assert.Equal("", f.Text("b"));
        Assert.True(f.Has("a"));
        Assert.False(f.Has("b"));
    }

    [Fact] public void A_checkbox_group_drops_blanks_and_repeats_and_is_bounded() => Assert.Equal(["a", "b"], Form(("t", ["a", " ", "b", "a"])).All("t"));

    [Fact] public void A_hostile_group_is_cut_at_the_limit() => Assert.Equal(3, Form(("t", Enumerable.Range(0, 100).Select(i => $"v{i}").ToArray())).All("t", 3).Count);
}

public class PaymentsViewTests
{
    [Fact]
    public void The_status_words_are_ready_and_needs_work_and_only_a_real_unfinished_account_has_an_action()
    {
        Assert.Equal("Gotowe", PaymentsView.Label(PaymentsState.Ready));
        Assert.Equal("Wymaga uzupełnienia", PaymentsView.Label(PaymentsState.Pending));
        Assert.Equal("Rozpocznij konfigurację płatności", PaymentsView.Action(PaymentsState.None));
        Assert.Equal("Dokończ konfigurację płatności", PaymentsView.Action(PaymentsState.Pending));
        Assert.Null(PaymentsView.Action(PaymentsState.Ready));
        Assert.Null(PaymentsView.Action(PaymentsState.NotApplicable));
        Assert.Null(PaymentsView.Action(PaymentsState.Unknown));
    }

    [Theory]
    [InlineData(PaymentsState.Ready, "success")] [InlineData(PaymentsState.Pending, "warning")] [InlineData(PaymentsState.None, "neutral")]
    [InlineData(PaymentsState.NotApplicable, "neutral")] [InlineData(PaymentsState.Unknown, "neutral")]
    public void The_status_badge_kind_is_success_for_ready_warning_for_pending_and_neutral_otherwise(PaymentsState state, string kind) => Assert.Equal(kind, PaymentsView.Kind(state));

    [Theory]
    [InlineData("https://connect.stripe.com/setup/e/acct_1/abc", true)] [InlineData("http://connect.stripe.com/x", false)] [InlineData("javascript:alert(1)", false)] [InlineData("data:text/html,x", false)]
    [InlineData("//evil.example/x", false)] [InlineData("/gabinet-programisty", false)] [InlineData("https://user:pw@connect.stripe.com/x", false)] [InlineData("", false)] [InlineData(null, false)]
    public void Only_an_absolute_https_link_without_credentials_is_followed(string? url, bool ok) => Assert.Equal(ok, PaymentsView.IsFollowable(url));
}

public class ProfileStatusTests
{
    [Theory]
    [InlineData("Draft", "Szkic", "neutral")] [InlineData("Submitted", "Czeka na weryfikację", "warning")] [InlineData("Approved", "Zatwierdzony", "success")]
    [InlineData("Rejected", "Odrzucony", "danger")] [InlineData("Suspended", "Zawieszony", "danger")] [InlineData("Surprise", "Status nieznany", "neutral")]
    public void A_status_has_the_label_the_journeys_read_and_a_kind(string status, string label, string kind) => Assert.Equal((label, kind), (ProfileStatus.Label(status), ProfileStatus.Kind(status)));
}
