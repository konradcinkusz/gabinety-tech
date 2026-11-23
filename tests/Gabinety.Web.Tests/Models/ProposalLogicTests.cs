using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Bookings;
using Gabinety.Web.Components.Pages.PractitionerArea;
using Gabinety.Web.Components.Pages.Practitioners;
using Gabinety.Web.Components.Pages.Proposals;
using Gabinety.Web.Components.Pages.Shared;

namespace Gabinety.Web.Tests.Models;

/// <summary>The pure parts of contract availability and collaboration proposals (ADR proposal 0013): no renderer, no clock, no backend.</summary>
public class ContractAvailabilityFormatTests
{
    static readonly DateOnly Today = new(2026, 11, 23);

    [Theory]
    [InlineData(null, "od zaraz")]
    [InlineData("2026-11-23", "od zaraz")]
    [InlineData("2026-10-01", "od zaraz")]
    [InlineData("2026-11-24", "za 1 dzień")]
    [InlineData("2026-11-26", "za 3 dni")]
    [InlineData("2026-12-06", "za 13 dni")]
    [InlineData("2026-12-07", "za 2 tygodnie")]
    [InlineData("2026-12-14", "za 3 tygodnie")]
    [InlineData("2026-12-21", "za 4 tygodnie")]
    [InlineData("2026-12-28", "za 5 tygodni")]
    [InlineData("2027-01-17", "za 8 tygodni")]
    [InlineData("2027-01-23", "za 2 miesiące")]
    [InlineData("2027-02-23", "za 3 miesiące")]
    [InlineData("2027-03-23", "za 4 miesiące")]
    [InlineData("2027-04-23", "za 5 miesięcy")]
    [InlineData("2027-05-23", "za 6 miesięcy")]
    [InlineData("2027-06-23", "od czerwca 2027")]
    [InlineData("2028-01-05", "od stycznia 2028")]
    public void The_phrase_is_relative_for_half_a_year_and_a_month_after_that(string? from, string expected) =>
        Assert.Equal(expected, ContractAvailabilityFormat.Relative(from is null ? null : DateOnly.Parse(from), Today));

    [Fact]
    public void A_day_of_the_month_before_the_current_one_does_not_round_a_month_up()
    {
        // 2027-02-10 is 2 months and 17 days from 2026-11-23: "za 2 miesiące", not three
        Assert.Equal("za 2 miesiące", ContractAvailabilityFormat.Relative(new DateOnly(2027, 1, 30), Today));
        Assert.Equal("za 2 miesiące", ContractAvailabilityFormat.Relative(new DateOnly(2027, 2, 10), Today));
    }

    [Fact]
    public void The_profile_and_card_lines_say_now_or_when()
    {
        var now = new ContractAvailabilityDto(null, 20, null);
        var later = new ContractAvailabilityDto(new DateOnly(2027, 2, 23), 20, new MoneyDto(11000, "PLN"));
        Assert.Equal("Dostępny do współpracy od zaraz", ContractAvailabilityFormat.ProfileLine(now, Today));
        Assert.Equal("Dostępny do współpracy za 3 miesiące", ContractAvailabilityFormat.ProfileLine(later, Today));
        Assert.Equal("Dostępny do kontraktu", ContractAvailabilityFormat.CardLine(now, Today));
        Assert.Equal("Kontrakt: za 3 miesiące", ContractAvailabilityFormat.CardLine(later, Today));
        Assert.Equal("Dostępny do kontraktu", ContractAvailabilityFormat.CardLine(new ContractAvailabilityDto(new DateOnly(2026, 11, 1), 5, null), Today)); // a passed date reads as now
    }

    [Fact]
    public void Hours_and_the_optional_rate_have_their_own_lines()
    {
        Assert.Equal("20 godz. tygodniowo", ContractAvailabilityFormat.HoursLine(new ContractAvailabilityDto(null, 20, null)));
        Assert.Null(ContractAvailabilityFormat.RateLine(new ContractAvailabilityDto(null, 20, null)));
        Assert.Equal("Orientacyjna stawka: 110,00 zł za godzinę", ContractAvailabilityFormat.RateLine(new ContractAvailabilityDto(null, 20, new MoneyDto(11000, "PLN")))!.Replace('\u00a0', ' ')); // money uses a no-break space
    }
}

public class ProposalInputsTests
{
    static readonly DateOnly Today = new(2026, 11, 23);

    [Theory]
    [InlineData("150", RateProblem.None, 15000)]
    [InlineData("150,5", RateProblem.None, 15050)]
    [InlineData("150.50", RateProblem.None, 15050)]
    [InlineData(" 20 ", RateProblem.None, 2000)]
    [InlineData("1000", RateProblem.None, 100000)]
    [InlineData("19,99", RateProblem.Range, 1999)]
    [InlineData("1000,01", RateProblem.Range, 100001)]
    [InlineData("", RateProblem.Format, 0)]
    [InlineData("abc", RateProblem.Format, 0)]
    [InlineData("1,234", RateProblem.Format, 0)]
    [InlineData("-5", RateProblem.Format, 0)]
    public void An_hourly_rate_is_read_with_integer_arithmetic_and_bounded(string input, RateProblem problem, long grosz)
    {
        var (p, g) = RateInput.Parse(input);
        Assert.Equal(problem, p);
        if (problem != RateProblem.Format) Assert.Equal(grosz, g);
    }

    [Fact]
    public void A_rate_round_trips_through_the_input_text() =>
        Assert.All(new long[] { 2000, 15000, 15050, 99999, 100000 }, g => Assert.Equal((RateProblem.None, g), RateInput.Parse(RateInput.ToInput(g))));

    [Fact]
    public void A_valid_long_term_form_gives_hours_a_week_and_months_and_no_total()
    {
        var (terms, errors) = ProposalInputs.ParseTerms(true, new TermsFields("150", "20", "6", "2026-12-01"), Today);
        Assert.Empty(errors);
        Assert.Equal(new ProposalTermsDto(new MoneyDto(15000, "PLN"), 20, null, 6, new DateOnly(2026, 12, 1)), terms);
    }

    [Fact]
    public void A_valid_short_term_form_gives_total_hours_and_optional_months()
    {
        var (withMonths, e1) = ProposalInputs.ParseTerms(false, new TermsFields("150", "40", "2", "2026-12-01"), Today);
        var (without, e2) = ProposalInputs.ParseTerms(false, new TermsFields("150", "40", "", "2026-12-01"), Today);
        Assert.Empty(e1); Assert.Empty(e2);
        Assert.Equal((null, 40, 2), (withMonths!.HoursPerWeek, withMonths.TotalHours, withMonths.DurationMonths));
        Assert.Equal((null, 40, null), (without!.HoursPerWeek, without.TotalHours, without.DurationMonths));
    }

    [Fact]
    public void Every_problem_is_reported_at_once_under_the_ids_of_the_form()
    {
        var (terms, errors) = ProposalInputs.ParseTerms(true, new TermsFields("5", "99", "", "2020-01-01"), Today);
        Assert.Null(terms);
        Assert.Equal(new[] { "durationMonths", "hoursPerWeek", "rate", "startDate" }, errors.Keys.Order(StringComparer.Ordinal));
        var (_, short1) = ProposalInputs.ParseTerms(false, new TermsFields("150", "0", "", "2026-12-01"), Today);
        Assert.Equal(["totalHours"], short1.Keys);
        var (_, bad) = ProposalInputs.ParseTerms(false, new TermsFields("150", "40", "99", "2026-12-01"), Today);
        Assert.Equal(["durationMonths"], bad.Keys); // an optional field is still checked when it is filled
    }

    [Theory]
    [InlineData("2026-11-23", true)]
    [InlineData("2026-11-22", false)]
    [InlineData("2028-11-23", true)]
    [InlineData("2028-11-24", false)]
    [InlineData("23.11.2026", false)]
    [InlineData("", false)]
    public void The_start_is_today_or_later_and_at_most_24_months_ahead(string start, bool ok) =>
        Assert.Equal(ok, !ProposalInputs.ParseTerms(true, new TermsFields("150", "20", "6", start), Today).Errors.ContainsKey("startDate"));

    [Theory]
    [InlineData("1", true)]
    [InlineData("60", true)]
    [InlineData("0", false)]
    [InlineData("61", false)]
    [InlineData("1e1", false)]
    [InlineData("-5", false)]
    [InlineData("+5", false)]
    [InlineData(" 5", false)]
    [InlineData("", false)]
    public void Hours_are_whole_digits_only_and_bounded(string hours, bool ok) =>
        Assert.Equal(ok, !ProposalInputs.ParseTerms(true, new TermsFields("150", hours, "6", "2026-12-01"), Today).Errors.ContainsKey("hoursPerWeek"));

    [Fact]
    public void The_message_is_20_to_1000_characters_when_required_and_a_note_may_be_empty()
    {
        Assert.NotNull(ProposalInputs.TextProblem("za krótko", required: true));
        Assert.Null(ProposalInputs.TextProblem(new string('a', 20), required: true));
        Assert.Null(ProposalInputs.TextProblem(new string('a', 1000), required: true));
        Assert.NotNull(ProposalInputs.TextProblem(new string('a', 1001), required: true));
        Assert.Null(ProposalInputs.TextProblem("", required: false));
        Assert.Null(ProposalInputs.TextProblem("   ", required: false));
        Assert.NotNull(ProposalInputs.TextProblem(new string('a', 1001), required: false));
        Assert.Null(ProposalInputs.TextProblem("  " + new string('a', 20) + "  ", required: true)); // trimmed first
    }

    [Fact]
    public void The_company_name_is_at_most_120_characters()
    {
        Assert.Null(ProposalInputs.CompanyProblem(null));
        Assert.Null(ProposalInputs.CompanyProblem(new string('f', 120)));
        Assert.NotNull(ProposalInputs.CompanyProblem(new string('f', 121)));
    }

    [Fact]
    public void A_contact_refusal_belongs_to_the_free_text_fields_there_are()
    {
        Assert.Equal(["text"], ProposalInputs.FreeTextFields(false));
        Assert.Equal(["text", "companyName"], ProposalInputs.FreeTextFields(true));
    }

    [Fact]
    public void The_same_body_has_the_same_fingerprint_and_another_body_another_one()
    {
        var terms = new ProposalTermsDto(new MoneyDto(15000, "PLN"), 20, null, 6, new DateOnly(2026, 12, 1));
        var practitioner = Guid.NewGuid();
        var a = ProposalAttempt.Fingerprint(practitioner, "LongTerm", terms, "wiadomość klienta", null);
        Assert.Equal(a, ProposalAttempt.Fingerprint(practitioner, "LongTerm", terms, "  wiadomość klienta  ", null)); // trimmed
        Assert.NotEqual(a, ProposalAttempt.Fingerprint(practitioner, "ShortTerm", terms, "wiadomość klienta", null));
        Assert.NotEqual(a, ProposalAttempt.Fingerprint(practitioner, "LongTerm", terms with { StartDate = new DateOnly(2026, 12, 2) }, "wiadomość klienta", null));
        Assert.NotEqual(a, ProposalAttempt.Fingerprint(practitioner, "LongTerm", terms, "wiadomość klienta", "Firma"));
        Assert.DoesNotContain("wiadomość", a); // only a hash goes back to the browser
    }

    [Fact]
    public void The_posted_key_is_kept_for_the_same_body_and_replaced_for_another()
    {
        var key = Guid.NewGuid();
        var fresh = Guid.NewGuid();
        Assert.Equal(key, ProposalAttempt.KeyFor(key.ToString(), "fp", "fp", () => fresh));
        Assert.Equal(fresh, ProposalAttempt.KeyFor(key.ToString(), "fp", "other", () => fresh));
        Assert.Equal(fresh, ProposalAttempt.KeyFor(null, null, "fp", () => fresh));
        Assert.Equal(fresh, ProposalAttempt.KeyFor("not-a-guid", "fp", "fp", () => fresh));
        Assert.Equal(fresh, ProposalAttempt.KeyFor(Guid.Empty.ToString(), "fp", "fp", () => fresh));
    }
}

public class ContractInputTests
{
    static readonly DateOnly Today = new(2026, 11, 23);

    [Fact]
    public void Not_available_ignores_everything_else()
    {
        var (body, errors) = ContractInput.Parse(new ContractFields("not_available", "garbage", "garbage", "garbage"), Today);
        Assert.Empty(errors);
        Assert.Equal(new ContractAvailabilitySettingsDto("not_available", null, null, null), body);
    }

    [Fact]
    public void Available_now_needs_hours_and_ignores_the_date()
    {
        var (body, errors) = ContractInput.Parse(new ContractFields("available_now", "2027-01-01", "20", "110"), Today);
        Assert.Empty(errors);
        Assert.Equal(new ContractAvailabilitySettingsDto("available_now", null, 20, 11000), body);
        Assert.Equal(["contractHours"], ContractInput.Parse(new ContractFields("available_now", "", "", ""), Today).Errors.Keys);
    }

    [Fact]
    public void Available_from_needs_a_day_after_today_and_at_most_24_months_ahead()
    {
        var (body, errors) = ContractInput.Parse(new ContractFields("available_from", "2027-02-23", "20", ""), Today);
        Assert.Empty(errors);
        Assert.Equal(new ContractAvailabilitySettingsDto("available_from", new DateOnly(2027, 2, 23), 20, null), body);
        foreach (var bad in new[] { "", "2026-11-23", "2026-11-22", "2028-11-24", "x" })
            Assert.Equal(["contractFrom"], ContractInput.Parse(new ContractFields("available_from", bad, "20", ""), Today).Errors.Keys);
    }

    [Fact]
    public void The_rate_is_optional_but_bounded_and_an_unknown_status_is_refused_alone()
    {
        Assert.Equal(["contractRate"], ContractInput.Parse(new ContractFields("available_now", "", "20", "5"), Today).Errors.Keys);
        Assert.Equal(["contractStatus"], ContractInput.Parse(new ContractFields("maybe", "", "20", ""), Today).Errors.Keys);
    }

    [Fact]
    public void Every_problem_is_reported_at_once_in_the_order_of_the_form()
    {
        var errors = ContractInput.Parse(new ContractFields("available_from", "", "", "5"), Today).Errors;
        Assert.Equal(["contractFrom", "contractHours", "contractRate"], ContractInput.FieldOrder.Where(errors.ContainsKey));
    }

    [Fact]
    public void The_service_form_is_read_back_and_an_unknown_status_falls_to_not_available()
    {
        Assert.Equal(new ContractFields("available_from", "2027-02-23", "20", "110"), ContractInput.From(new ContractAvailabilitySettingsDto("available_from", new DateOnly(2027, 2, 23), 20, 11000)));
        Assert.Equal(new ContractFields("available_now", "", "5", "150,50"), ContractInput.From(new ContractAvailabilitySettingsDto("available_now", null, 5, 15050)));
        Assert.Equal("not_available", ContractInput.From(new ContractAvailabilitySettingsDto("surprise", null, null, null)).Status);
        Assert.Equal(ContractFields.Default, ContractInput.From(new ContractAvailabilitySettingsDto("not_available", null, null, null)));
    }

    [Fact]
    public void The_fields_the_service_names_are_mapped_to_the_ids_of_the_form_and_the_rest_dropped()
    {
        var failure = new ApiFailure(422, "validation_failed", ["status", "availableFrom", "hoursPerWeek", "rateMinor", "somethingNew"]);
        Assert.Equal(["contractStatus", "contractFrom", "contractHours", "contractRate"], ContractInput.FieldErrors(failure).Select(e => e.Field));
        Assert.Empty(ContractInput.FieldErrors(new ApiFailure(503, "dependency_unavailable")));
    }
}

public class ProposalFormatTests
{
    static ProposalTermsDto Long() => new(new MoneyDto(15000, "PLN"), 20, null, 6, new DateOnly(2026, 12, 1));
    static ProposalTermsDto Short() => new(new MoneyDto(15050, "PLN"), null, 40, null, new DateOnly(2026, 12, 1));

    [Fact]
    public void The_terms_read_as_one_line_without_ids_or_codes()
    {
        static string Plain(string s) => s.Replace('\u00a0', ' '); // money uses a no-break space
        Assert.Equal("150,00 zł za godzinę · 20 godz. tygodniowo · 6 mies. · start 1 grudnia 2026", Plain(ProposalFormat.Summary(Long())));
        Assert.Equal("150,50 zł za godzinę · 40 godz. łącznie · start 1 grudnia 2026", Plain(ProposalFormat.Summary(Short())));
        Assert.Equal("150,00 zł za godzinę · 40 godz. łącznie · 2 mies. · start 1 grudnia 2026", Plain(ProposalFormat.Summary(Short() with { Rate = new MoneyDto(15000, "PLN"), DurationMonths = 2 })));
    }

    [Theory]
    [InlineData("AwaitingPractitioner", "Czeka na programistę", BadgeKind.Warning)]
    [InlineData("AwaitingClient", "Czeka na klienta", BadgeKind.Warning)]
    [InlineData("Accepted", "Zaakceptowana", BadgeKind.Success)]
    [InlineData("Rejected", "Odrzucona", BadgeKind.Danger)]
    [InlineData("Withdrawn", "Wycofana", BadgeKind.Neutral)]
    [InlineData("Expired", "Wygasła", BadgeKind.Neutral)]
    [InlineData("SomethingNew", "Nieznany stan", BadgeKind.Neutral)]
    public void A_state_is_a_label_and_a_kind_and_an_unknown_one_still_renders(string status, string label, BadgeKind kind)
    {
        var badge = ProposalFormat.StatusBadge(status);
        Assert.Equal((label, kind), (badge.Label, badge.Kind));
    }

    [Fact]
    public void Only_the_two_awaiting_states_are_open() =>
        Assert.Equal(["AwaitingClient", "AwaitingPractitioner"], new[] { "AwaitingPractitioner", "AwaitingClient", "Accepted", "Rejected", "Withdrawn", "Expired", "x" }.Where(ProposalFormat.IsOpen).Order());

    [Fact]
    public void The_kind_and_the_rounds_have_Polish_names()
    {
        Assert.Equal("Współpraca długoterminowa", ProposalFormat.KindLabel("LongTerm"));
        Assert.Equal("Krótka współpraca", ProposalFormat.KindLabel("ShortTerm"));
        var round = new ProposalRoundDto(2, "Practitioner", "Countered", null, null, DateTimeOffset.UnixEpoch);
        Assert.Equal("Runda 2: Kontrpropozycja (programista)", ProposalFormat.RoundHeading(round));
        Assert.Equal("Runda 3: Wygaśnięcie (system)", ProposalFormat.RoundHeading(round with { Number = 3, By = "System", Action = "Expired" }));
    }

    [Theory]
    [InlineData(0, "unavailable", "Nie udało się połączyć z serwisem. Spróbuj ponownie za chwilę.")]
    [InlineData(409, "invalid_transition", "Propozycja zmieniła się w międzyczasie. Odśwież stronę i sprawdź jej aktualny stan.")]
    [InlineData(422, "own_profile", "Nie możesz złożyć propozycji samemu sobie.")]
    [InlineData(422, "practitioner_not_available", "Ten programista nie przyjmuje teraz propozycji współpracy.")]
    [InlineData(409, "proposal_exists", "Masz już otwartą propozycję dla tego programisty. Odpowiedz w niej albo ją wycofaj.")]
    [InlineData(429, "rate_limited", "Wysłano zbyt wiele propozycji. Spróbuj ponownie później.")]
    [InlineData(503, "dependency_unavailable", "Ta część serwisu jest chwilowo niedostępna. Spróbuj ponownie za chwilę.")]
    [InlineData(404, "not_found", "Nie znaleziono takiej propozycji.")]
    [InlineData(422, "validation_failed", "Sprawdź wpisane dane i spróbuj ponownie.")]
    [InlineData(403, "forbidden", "Nie masz uprawnień do tej czynności.")]
    [InlineData(500, "boom", "Coś poszło nie tak. Spróbuj ponownie za chwilę.")]
    public void A_failure_is_said_from_the_status_and_the_code_only(int status, string code, string text) =>
        Assert.Equal(text, ProposalFormat.FailureText(new ApiFailure(status, code)));

    [Fact]
    public void The_fields_the_service_names_are_mapped_and_a_terms_prefix_is_dropped()
    {
        var failure = new ApiFailure(422, "validation_failed", ["terms.rate", "terms.startDate", "terms.hoursPerWeek", "terms.totalHours", "terms.durationMonths", "text", "companyName", "terms.nonsense", "kind"]);
        Assert.Equal(["rate", "startDate", "hoursPerWeek", "totalHours", "durationMonths", "text", "companyName"], ProposalFormat.FieldErrors(failure, true).Select(e => e.Field));
    }

    [Theory]
    [InlineData("open", "open")]
    [InlineData("closed", "closed")]
    [InlineData("", null)]
    [InlineData("all", null)]
    [InlineData(null, null)]
    [InlineData("OPEN", null)]
    public void A_state_filter_is_open_or_closed_and_anything_else_is_every_state(string? state, string? expected) => Assert.Equal(expected, ProposalFormat.StateFilter(state));

    [Fact]
    public void A_date_reads_as_day_month_year_in_the_genitive_and_an_instant_in_Warsaw_time()
    {
        Assert.Equal("1 grudnia 2026", ProposalFormat.Date(new DateOnly(2026, 12, 1)));
        Assert.Equal("2 grudnia 2026", ProposalFormat.Date(new DateTimeOffset(2026, 12, 1, 23, 30, 0, TimeSpan.Zero))); // 00:30 in Warsaw
    }

    [Fact]
    public void The_counterpart_of_a_row_is_the_other_party()
    {
        var row = new ProposalSummaryDto(Guid.NewGuid(), "LongTerm", "AwaitingPractitioner", Long(), new PractitionerRefDto(Guid.NewGuid(), "Anna Demowska", "demo-anna"), new ClientRefDto("Klient J."), null, 1, true, DateTimeOffset.UnixEpoch, null);
        Assert.Equal("Anna Demowska", ProposalFormat.Counterpart(row, viewerIsClient: true));
        Assert.Equal("Klient J.", ProposalFormat.Counterpart(row, viewerIsClient: false));
    }
}

public class ListStateContractTests
{
    [Fact]
    public void The_contract_filter_is_on_only_for_the_value_one()
    {
        Assert.True(ListState.Parse(null, null, null, null, null, null, contract: "1").Contract);
        foreach (var other in new[] { "0", "true", "yes", "", null }) Assert.False(ListState.Parse(null, null, null, null, null, null, contract: other).Contract);
        Assert.False(ListState.Parse(null, null, null, null, null, null).Contract); // the old six-argument call still works
    }

    [Fact]
    public void The_filter_is_part_of_the_link_and_of_the_variant_and_goes_back_to_the_first_page()
    {
        var state = new ListState("audyt-kodu", Cursor: "C1").WithContract(true);
        Assert.True(state.Contract);
        Assert.Null(state.Cursor);
        Assert.Equal("/programisci/audyt-kodu?contract=1", state.Href());
        Assert.True(state.IsVariant); // a filtered list is not indexed
        Assert.False(state.WithContract(false).IsVariant);
        Assert.Equal("/programisci/audyt-kodu", state.WithContract(false).Href());
        Assert.Equal("/programisci?duration=30&contract=1&q=kod", new ListState(Duration: 30, Q: "kod", Contract: true).Href());
    }

    [Fact]
    public void Clearing_the_filters_drops_it_and_it_counts_as_an_active_filter()
    {
        var state = new ListState("audyt-kodu", Contract: true);
        Assert.False(state.Cleared().Contract);
        var view = new ListView(state, Sample.Taxonomy, Sample.Sitemap(5, true, "/programisci/audyt-kodu"));
        Assert.Equal(2, view.ActiveFilters); // the problem and the contract filter
        var group = view.Filters().Last();
        Assert.Equal("Współpraca", group.Title);
        Assert.Equal(["Wszyscy", "Dostępni do kontraktu"], group.Items.Select(i => i.Label));
        Assert.Equal(["Dostępni do kontraktu"], group.Items.Where(i => i.Current).Select(i => i.Label));
        Assert.True(group.Segmented);
        Assert.Equal(["/programisci/audyt-kodu", "/programisci/audyt-kodu?contract=1"], group.Items.Select(i => i.Href));
    }
}
