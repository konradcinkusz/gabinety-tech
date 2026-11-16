using Gabinety.Reputation.Rules;

namespace Gabinety.Reputation.Tests.Rules;

public class ContainsLinkRuleTests
{
    static string? Check(string text, params string[] hosts) => new ContainsLinkRule(hosts.Length == 0 ? null : hosts).Check(ReviewText.From(text));

    [Theory]
    [InlineData("Kod do wglądu: https://github.com/demo-org/demo-repo, polecam całość.")]
    [InlineData("Repozytorium na https://gitlab.com/demo/demo i nic więcej.")]
    [InlineData("Zobacz https://docs.github.com/pages jako przykład, bardzo dobra praca.")] // subdomain of an allow-listed host
    [InlineData("Opis projektu leży na codeberg.org/demo/demo, a reszta to opinia.")] // bare domain with a path
    [InlineData("Świetna współpraca, jasne wyjaśnienia i terminowość, polecam.")]
    [InlineData("Wersja .NET 8.0.1 i EF Core 9 działały bez zarzutu, 5 lat doświadczenia.")]
    public void Not_flagged(string text) => Assert.Null(Check(text));

    [Theory]
    [InlineData("Zobacz https://example.test/oferta po więcej informacji.")] // one URL, host outside the allow-list
    [InlineData("Najlepsze ceny na www.example.test, serdecznie polecam usługę.")]
    [InlineData("Wszystko tutaj: example.test/promocja, naprawdę warto.")]
    [InlineData("https://github.com/demo/a oraz https://github.com/demo/b to oba repozytoria.")] // two URLs, both allow-listed
    [InlineData("Zobacz https://notgithub.com/demo i https://github.com.example.test/x")]
    [InlineData("Link: https://github.com@example.test/demo prowadzi gdzie indziej.")] // userinfo trick
    public void Flagged(string text) => Assert.Equal("contains_link", Check(text));

    [Fact]
    public void Look_alike_host_is_not_a_subdomain() => Assert.Equal("contains_link", Check("Kod na https://evilgithub.com/demo/demo jest tutaj."));

    [Fact]
    public void Hosts_come_from_the_constructor() => Assert.Null(Check("Plik: https://example.test/demo/x jest tutaj.", "example.test"));
}

public class ContactDetailsRuleTests
{
    static string? Check(string text) => new ContactDetailsRule().Check(ReviewText.From(text));

    [Theory]
    [InlineData("Napisz do mnie: demo-user@example.test, odpowiem.")]
    [InlineData("Dzwoń na 000 000 000 po godzinie osiemnastej.")]
    [InlineData("Mój numer to +48 000-000-000, zapraszam.")]
    [InlineData("Kontakt 000-0000 dla chętnych, bardzo polecam.")]
    [InlineData("Znajdziesz mnie jako @demo_user na portalu, polecam.")]
    [InlineData("Pisz na t.me/demo_user w sprawie dalszej współpracy.")]
    [InlineData("Szybki kontakt: wa.me/48000000000 zawsze działa.")]
    [InlineData("Najlepiej przez WhatsApp 000 000 000 bo szybciej.")]
    [InlineData("Telegram: @demo_user to najszybsza droga kontaktu.")]
    public void Flagged(string text) => Assert.Equal("contact_details", Check(text));

    [Theory]
    [InlineData("Użyliśmy .NET 8.0.1 oraz EF Core 9 w całym projekcie.")]
    [InlineData("Spotkanie trwało 2 godziny i było bardzo konkretne.")]
    [InlineData("Specjalista z 5 lat doświadczenia, wszystko wytłumaczył.")]
    [InlineData("Termin 2026-11-16 został dotrzymany co do minuty, super.")]
    [InlineData("Dobry signal i świetny kontakt, wszystko jasne od początku.")]
    [InlineData("Pracowałem z nim 3 miesiące, ocena 5 na 5, serdecznie polecam.")]
    public void Not_flagged(string text) => Assert.Null(Check(text));
}

public class BlockedTermRuleTests
{
    static string? Check(string text, params string[] terms) => new BlockedTermRule(terms).Check(ReviewText.From(text));

    [Fact]
    public void Configured_term_is_flagged_whole_word_case_and_diacritics_insensitive()
    {
        Assert.Equal("blocked_term", Check("To była zwykła OSZUSTWO i strata czasu.", "oszustwo"));
        Assert.Equal("blocked_term", Check("Prawdziwa żółć, nic więcej do dodania tutaj.", "ZOLC"));
        Assert.Equal("blocked_term", Check("Zwykła Łajza, nic więcej do dodania tutaj.", "lajza"));
        Assert.Equal("blocked_term", Check("To jest bardzo zły pomysł, wierzcie mi.", "zły pomysł"));
    }

    [Fact]
    public void Part_of_a_longer_word_is_not_flagged() => Assert.Null(Check("Oszustwo-podobne rzeczy nie, ale oszustwonie tak.", "oszust"));

    [Fact]
    public void Empty_list_flags_nothing() => Assert.Null(Check("Cokolwiek tu jest napisane, w tym oszustwo.", []));

    [Fact]
    public void Blank_entries_are_ignored() => Assert.Null(Check("Cokolwiek tu jest napisane, nic złego.", "", "  "));
}

public class RepetitionRuleTests
{
    static string? Check(string text) => new RepetitionRule().Check(ReviewText.From(text));

    [Theory]
    [InlineData("Super współpraca!!!!!!!!! polecam z całego serca.")] // 9 x '!'
    [InlineData("Naprawdę dobrzeeeeeeeee wytłumaczone wszystko.")] // 9 x 'e'
    [InlineData("polecam polecam polecam polecam polecam polecam polecam polecam polecam")] // 9 words
    [InlineData("Polecam POLECAM polecam Polecam polecam polecam polecam polecam polecam")] // case-insensitive
    public void Flagged(string text) => Assert.Equal("repetition", Check(text));

    [Theory]
    [InlineData("Super współpraca!!!!!!!! polecam z całego serca.")] // 8 is allowed
    [InlineData("polecam polecam polecam polecam polecam polecam polecam polecam")] // 8 words
    [InlineData("Bardzo dobrze wytłumaczone, polecam polecam i jeszcze raz polecam.")]
    [InlineData("Specjalista rzetelny, punktualny i konkretny, spotkanie było owocne.")]
    public void Not_flagged(string text) => Assert.Null(Check(text));
}

public class TooShortAfterTrimRuleTests
{
    static string? Check(string text) => new TooShortAfterTrimRule().Check(ReviewText.From(text));

    [Fact]
    public void Twenty_zero_width_characters_pass_the_length_check_but_not_this_rule()
    {
        var text = new string('​', 20);
        Assert.Empty(new ReviewValidator().Validate(text));
        Assert.Equal("too_short_after_trim", Check(text));
    }

    [Fact]
    public void Control_characters_and_collapsed_whitespace_do_not_count() =>
        Assert.Equal("too_short_after_trim", Check("ok\u0001\u0002 \t\t \n\n  dobrze  ​ ​ tak"));

    [Fact]
    public void Twenty_visible_characters_pass() => Assert.Null(Check("Bardzo dobra usługa!"));

    [Fact]
    public void Odd_input_never_throws()
    {
        Assert.Equal("too_short_after_trim", Check(""));
        Assert.Equal("too_short_after_trim", Check("   \t\n "));
        Assert.Null(Check(new string((char)120, 200_000) + " " + new string((char)261, 100_000)));
    }
}
