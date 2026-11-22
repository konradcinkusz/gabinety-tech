using Gabinety.Booking.Features.Proposals;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;

namespace Gabinety.Booking.Tests;

public class ProposalTextRulesTests
{
    [Theory]
    [InlineData("pisz na jan.kowalski@firma.pl")]
    [InlineData("mój mail: jan@firma.pl")]
    [InlineData("zadzwoń 600 123 456")]
    [InlineData("tel +48 600 123 456")]
    [InlineData("numer 600123456")]
    [InlineData("pisz na @jankowalski")]
    [InlineData("t.me/jankowalski")]
    [InlineData("wa.me/48600123456")]
    [InlineData("whatsapp 600123456")]
    [InlineData("telegram @jan_k")]
    [InlineData("zobacz https://moja-firma.pl/oferta")]
    [InlineData("zobacz www.moja-firma.pl")]
    [InlineData("zobacz moja-firma.pl/kontakt")]
    [InlineData("https://github.com/a https://github.com/b")]
    [InlineData("https://github.com@evil.test/")]
    public void Contact_details_and_foreign_links_are_refused(string text) => Assert.True(ProposalTextRules.HasContactDetails(ProposalTextRules.Clean(text)));

    [Theory]
    [InlineData("Szukamy programisty .NET do współpracy.")]
    [InlineData("Start 2027-01-15, trzy miesiące.")]
    [InlineData("Start 15-01-2027.")]
    [InlineData("Mamy repozytorium https://github.com/demo/projekt do przejrzenia.")]
    [InlineData("Budżet około 150 zł za godzinę, 20 godzin tygodniowo.")]
    [InlineData("Zespół pięciu osób, projekt trwa od 2023 do 2026.")]
    [InlineData("")]
    public void Ordinary_text_dates_and_a_code_host_link_are_accepted(string text) => Assert.False(ProposalTextRules.HasContactDetails(ProposalTextRules.Clean(text)));

    [Fact]
    public void Clean_removes_control_and_zero_width_characters_and_collapses_whitespace()
    {
        Assert.Equal("a b c", ProposalTextRules.Clean("  a\t\n b​   c \u0007"));
        Assert.Equal("", ProposalTextRules.Clean(null));
        Assert.Equal("", ProposalTextRules.Clean("  \n\t "));
    }

    [Fact]
    public void A_zero_width_character_does_not_hide_an_address() =>
        Assert.True(ProposalTextRules.HasContactDetails(ProposalTextRules.Clean("jan​@firma.pl")));
}

public class ProposalTermsRulesTests
{
    static readonly DateOnly Today = new(2026, 11, 16);
    static ProposalTerms Long(long rate = 15000, int? hours = 20, int? months = 6, DateOnly? start = null, int? total = null) =>
        new(new MoneyDto(rate), hours, total, months, start ?? Today.AddDays(10));
    static ProposalTerms Short(long rate = 15000, int? total = 40, int? months = null, DateOnly? start = null) =>
        new(new MoneyDto(rate), null, total, months, start ?? Today.AddDays(10));

    [Fact]
    public void Valid_terms_have_no_errors()
    {
        Assert.Empty(ProposalTermsRules.Validate(ProposalKind.LongTerm, Long(), Today));
        Assert.Empty(ProposalTermsRules.Validate(ProposalKind.ShortTerm, Short(), Today));
        Assert.Empty(ProposalTermsRules.Validate(ProposalKind.ShortTerm, Short(months: 3), Today));
    }

    [Theory]
    [InlineData(1_999L, false)]
    [InlineData(2_000L, true)]
    [InlineData(100_000L, true)]
    [InlineData(100_001L, false)]
    public void The_hourly_rate_is_bounded(long rate, bool ok) =>
        Assert.Equal(ok, ProposalTermsRules.Validate(ProposalKind.LongTerm, Long(rate: rate), Today).Count == 0);

    [Fact]
    public void The_rate_must_be_in_zloty() =>
        Assert.Contains("terms.rate", ProposalTermsRules.Validate(ProposalKind.LongTerm, Long() with { Rate = new MoneyDto(15000, "EUR") }, Today).Keys);

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void A_long_term_proposal_needs_hours_a_week(int? hours, bool ok) =>
        Assert.Equal(ok, ProposalTermsRules.Validate(ProposalKind.LongTerm, Long(hours: hours), Today).Count == 0);

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(36, true)]
    [InlineData(37, false)]
    public void A_long_term_proposal_needs_months(int? months, bool ok) =>
        Assert.Equal(ok, ProposalTermsRules.Validate(ProposalKind.LongTerm, Long(months: months), Today).Count == 0);

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(400, true)]
    [InlineData(401, false)]
    public void A_short_term_proposal_needs_total_hours(int? total, bool ok) =>
        Assert.Equal(ok, ProposalTermsRules.Validate(ProposalKind.ShortTerm, Short(total: total), Today).Count == 0);

    [Fact]
    public void The_start_date_is_today_or_later_and_at_most_24_months_ahead()
    {
        Assert.Empty(ProposalTermsRules.Validate(ProposalKind.LongTerm, Long(start: Today), Today));
        Assert.Contains("terms.startDate", ProposalTermsRules.Validate(ProposalKind.LongTerm, Long(start: Today.AddDays(-1)), Today).Keys);
        Assert.Empty(ProposalTermsRules.Validate(ProposalKind.LongTerm, Long(start: Today.AddMonths(24)), Today));
        Assert.Contains("terms.startDate", ProposalTermsRules.Validate(ProposalKind.LongTerm, Long(start: Today.AddMonths(24).AddDays(1)), Today).Keys);
    }

    [Fact]
    public void Missing_terms_or_an_undefined_kind_are_errors()
    {
        Assert.Contains("terms", ProposalTermsRules.Validate(ProposalKind.LongTerm, null, Today).Keys);
        Assert.Contains("kind", ProposalTermsRules.Validate((ProposalKind)9, Long(), Today).Keys);
    }

    [Fact]
    public void Normalising_drops_the_fields_that_do_not_belong_to_the_kind()
    {
        var longTerm = ProposalTermsRules.Normalize(ProposalKind.LongTerm, Long(total: 99));
        Assert.Null(longTerm.TotalHours); Assert.Equal(20, longTerm.HoursPerWeek);
        var shortTerm = ProposalTermsRules.Normalize(ProposalKind.ShortTerm, Short(total: 40) with { HoursPerWeek = 30 });
        Assert.Null(shortTerm.HoursPerWeek); Assert.Equal(40, shortTerm.TotalHours);
    }
}

public class ProposalRateLimiterTests
{
    sealed class Clock : TimeProvider
    {
        DateTimeOffset _now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan t) => _now += t;
    }

    [Fact]
    public void Five_per_hour_then_a_refusal_with_the_time_to_wait()
    {
        var clock = new Clock();
        var l = new ProposalRateLimiter(clock);
        for (var i = 0; i < ProposalRateLimiter.PerHour; i++) { Assert.True(l.TryAcquire("u", out _)); clock.Advance(TimeSpan.FromMinutes(1)); }
        Assert.False(l.TryAcquire("u", out var wait));
        Assert.InRange(wait, TimeSpan.FromMinutes(54), TimeSpan.FromMinutes(56));
    }

    [Fact]
    public void The_hour_window_slides()
    {
        var clock = new Clock();
        var l = new ProposalRateLimiter(clock);
        for (var i = 0; i < ProposalRateLimiter.PerHour; i++) Assert.True(l.TryAcquire("u", out _));
        Assert.False(l.TryAcquire("u", out _));
        clock.Advance(TimeSpan.FromMinutes(61));
        Assert.True(l.TryAcquire("u", out _));
    }

    [Fact]
    public void Twenty_per_day_even_when_the_hours_are_free()
    {
        var clock = new Clock();
        var l = new ProposalRateLimiter(clock);
        for (var i = 0; i < ProposalRateLimiter.PerDay; i++)
        {
            Assert.True(l.TryAcquire("u", out _), $"call {i + 1}");
            clock.Advance(TimeSpan.FromMinutes(13)); // never five inside one hour
        }
        Assert.False(l.TryAcquire("u", out var wait));
        Assert.True(wait > TimeSpan.Zero);
        clock.Advance(TimeSpan.FromHours(24));
        Assert.True(l.TryAcquire("u", out _));
    }

    [Fact]
    public void Users_are_counted_apart_and_a_refused_call_is_not_counted()
    {
        var clock = new Clock();
        var l = new ProposalRateLimiter(clock);
        for (var i = 0; i < ProposalRateLimiter.PerHour; i++) l.TryAcquire("a", out _);
        Assert.False(l.TryAcquire("a", out _));
        Assert.True(l.TryAcquire("b", out _));
        for (var i = 0; i < 10; i++) l.TryAcquire("a", out _); // refused calls must not extend the penalty
        clock.Advance(TimeSpan.FromMinutes(61));
        Assert.True(l.TryAcquire("a", out _));
    }
}
