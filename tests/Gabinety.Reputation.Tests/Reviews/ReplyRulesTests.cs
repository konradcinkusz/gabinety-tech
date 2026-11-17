using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Contracts.Common;
using Gabinety.Reputation.Features.Reviews.Reply;

namespace Gabinety.Reputation.Tests.Reviews;

/// <summary>A reply is public text: the review rules about leakage and abuse apply to it and a tripped rule REFUSES it (never holds it).</summary>
public class ReplyRulesTests
{
    public static TheoryData<string, string, string> Refused => new()
    {
        { "Zapraszam do kontaktu: demo.gabinet@example.com w sprawie terminu.", ErrorCodes.ContactDetailsNotAllowed, "contact_details" },
        { "Zadzwoń do mnie na 600 123 456 i umówimy termin.", ErrorCodes.ContactDetailsNotAllowed, "contact_details" },
        { "Napisz na Telegramie @demo_gabinet to ustalimy szczegóły.", ErrorCodes.ContactDetailsNotAllowed, "contact_details" },
        { "Szczegóły oferty są na https://demo-gabinet.example.com/oferta dla państwa.", ErrorCodes.ContactDetailsNotAllowed, "contains_link" },
        { "Dwa linki https://github.com/demo/a oraz https://github.com/demo/b razem.", ErrorCodes.ContactDetailsNotAllowed, "contains_link" },
        { "To była zwykła nieprawda i totalne DEMOZAKAZANE zdanie w opinii.", ErrorCodes.InvalidInput, "blocked_term" },
        { "Dziękuję dziękuję dziękuję dziękuję dziękuję dziękuję dziękuję dziękuję dziękuję bardzo.", ErrorCodes.InvalidInput, "repetition" },
    };

    static ReplyHost Host()
    {
        var h = new ReplyHost();
        h.ExtraSettings["Reviews:BlockedTerms:0"] = "demozakazane";
        return h;
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task A_reply_that_trips_a_rule_is_422_with_the_mapped_code_and_nothing_is_stored_or_logged(string text, string code, string reason)
    {
        using var h = Host();
        var user = $"demo-pract-{Guid.NewGuid():N}"; var pract = h.Directory.Add(user);
        var id = await h.SeedReview(pract);

        var r = await ReplyHost.Reply(h.ClientFor(user), id, text);
        var raw = await r.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var root = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(code, root.GetProperty("code").GetString());
        Assert.Contains(reason, root.GetProperty("errors").GetProperty("text").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain(text[..20], raw);                       // the answer carries codes, not the text
        var row = await h.ReviewOf(id);
        Assert.Null(row.ReplyText);                                     // refused, not stored
        Assert.Equal(Gabinety.Contracts.Reputation.ReviewStatus.Published, row.Status); // and not held either
        Assert.Empty(await h.EventsOf(id));
        Assert.DoesNotContain(h.Logs.Lines, l => l.Contains(text[..20], StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_refused_reply_can_be_retried_with_clean_text()
    {
        using var h = Host();
        var user = $"demo-pract-{Guid.NewGuid():N}"; var pract = h.Directory.Add(user);
        var id = await h.SeedReview(pract);
        var c = h.ClientFor(user);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ReplyHost.Reply(c, id, "Pisz na demo.gabinet@example.com proszę bardzo.")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ReplyHost.Reply(c, id)).StatusCode);
    }

    [Fact]
    public async Task A_reply_of_10_to_19_characters_is_not_refused_by_the_review_length_floor()
    {
        using var h = Host();
        var user = $"demo-pract-{Guid.NewGuid():N}"; var pract = h.Directory.Add(user);
        var id = await h.SeedReview(pract);

        Assert.Equal(HttpStatusCode.Created, (await ReplyHost.Reply(h.ClientFor(user), id, "Dziękuję!!!")).StatusCode);
        Assert.Equal(ReplyReviewFeature.MinLength, "0123456789".Length);
    }

    [Fact]
    public async Task The_rules_run_before_Directory_so_a_leaky_reply_never_costs_a_lookup()
    {
        using var h = Host();
        var user = $"demo-pract-{Guid.NewGuid():N}"; h.Directory.Add(user);

        await ReplyHost.Reply(h.ClientFor(user), Guid.NewGuid(), "Pisz na demo.gabinet@example.com proszę bardzo.");

        Assert.Equal(0, h.Directory.Calls);
    }
}
