using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Persistence;

namespace Gabinety.Reputation.Tests.Reviews;

/// <summary>POST /api/v1/reviews/{id}/reply (CONTRACTS 5.2): the reviewed practitioner, once, on a Published review.</summary>
public class ReplyTests
{
    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    static async Task<string> Code(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;

    /// <summary>Text of exact length that no review rule trips (no run of one character or word).</summary>
    static string Varied(int n) => string.Concat(Enumerable.Repeat("abcdefghij", n / 10 + 1))[..n];

    static (ReplyHost h, string user, Guid pract) Arrange()
    {
        var h = new ReplyHost();
        var user = $"demo-pract-{Guid.NewGuid():N}";
        return (h, user, h.Directory.Add(user));
    }

    [Fact]
    public async Task The_reviewed_practitioner_replies_once_and_the_public_list_shows_the_reply()
    {
        var (h, user, pract) = Arrange(); using var _ = h;
        var id = await h.SeedReview(pract);
        h.Clock.Advance(TimeSpan.FromHours(1));

        var r = await ReplyHost.Reply(h.ClientFor(user), id);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var body = (await r.Content.ReadFromJsonAsync<ReplyDto>(Web))!;
        Assert.Equal(ReplyHost.GoodReply, body.Text);
        Assert.Equal(h.Clock.GetUtcNow(), body.RepliedAt);
        var row = await h.ReviewOf(id);
        Assert.Equal((ReplyHost.GoodReply, (DateTimeOffset?)h.Clock.GetUtcNow(), ReviewStatus.Published), (row.ReplyText, row.RepliedAt, row.Status));
        var page = (await (await h.ClientFor(null).GetAsync($"/api/v1/practitioners/{pract}/reviews")).Content.ReadFromJsonAsync<Page<ReviewDto>>(Web))!;
        Assert.Equal(ReplyHost.GoodReply, Assert.Single(page.Items).Reply!.Text);
    }

    [Fact]
    public async Task A_reply_writes_one_Replied_event_with_the_actor_and_no_text_and_leaves_the_rating_alone()
    {
        var (h, user, pract) = Arrange(); using var _ = h;
        var id = await h.SeedReview(pract);

        await ReplyHost.Reply(h.ClientFor(user), id);

        var e = Assert.Single(await h.EventsOf(id));
        Assert.Equal((ModerationEventKind.Replied, user, (string?)null), (e.Kind, e.Actor, e.Note));
        Assert.Null(await h.Query(db => Task.FromResult(db.RatingSummaries.SingleOrDefault(s => s.PractitionerId == pract))));
    }

    [Fact]
    public async Task A_second_reply_is_409_already_replied_and_keeps_the_first_text()
    {
        var (h, user, pract) = Arrange(); using var _ = h;
        var id = await h.SeedReview(pract);
        var c = h.ClientFor(user);
        Assert.Equal(HttpStatusCode.Created, (await ReplyHost.Reply(c, id)).StatusCode);

        var second = await ReplyHost.Reply(c, id, "Druga odpowiedź, której nie wolno zapisać.");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(ErrorCodes.AlreadyReplied, await Code(second));
        Assert.Equal(ReplyHost.GoodReply, (await h.ReviewOf(id)).ReplyText);
        Assert.Single(await h.EventsOf(id));
    }

    [Fact]
    public async Task Another_practitioner_gets_404_and_nothing_is_written()
    {
        var (h, _, pract) = Arrange(); using var _h = h;
        var other = $"demo-other-{Guid.NewGuid():N}"; h.Directory.Add(other);
        var id = await h.SeedReview(pract);

        var r = await ReplyHost.Reply(h.ClientFor(other), id);

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal(ErrorCodes.NotFound, await Code(r));
        Assert.Null((await h.ReviewOf(id)).ReplyText);
        Assert.Empty(await h.EventsOf(id));
    }

    [Fact]
    public async Task A_client_who_is_no_practitioner_gets_404_and_so_does_an_unknown_review()
    {
        var (h, user, pract) = Arrange(); using var _ = h;
        var id = await h.SeedReview(pract);

        Assert.Equal(HttpStatusCode.NotFound, (await ReplyHost.Reply(h.ClientFor("demo-client-1"), id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ReplyHost.Reply(h.ClientFor(user), Guid.NewGuid())).StatusCode);
        Assert.Null((await h.ReviewOf(id)).ReplyText);
    }

    [Fact]
    public async Task Unauthenticated_is_401_and_Directory_is_not_asked()
    {
        var (h, _, pract) = Arrange(); using var _h = h;
        var id = await h.SeedReview(pract);

        var r = await ReplyHost.Reply(h.ClientFor(null), id);

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal(0, h.Directory.Calls);
    }

    [Theory]
    [InlineData(ReviewStatus.Held)]
    [InlineData(ReviewStatus.Rejected)]
    public async Task A_review_that_is_not_Published_is_404_to_its_own_practitioner_and_stays_without_a_reply(ReviewStatus status)
    {
        var (h, user, pract) = Arrange(); using var _ = h;
        var id = await h.SeedReview(pract, status);

        var r = await ReplyHost.Reply(h.ClientFor(user), id);

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Null((await h.ReviewOf(id)).ReplyText);
        Assert.Empty(await h.EventsOf(id));
    }

    [Fact]
    public async Task Directory_down_is_503_dependency_unavailable_and_leaks_neither_url_nor_key_and_writes_nothing()
    {
        var (h, user, pract) = Arrange(); using var _ = h;
        var id = await h.SeedReview(pract);
        h.Directory.Down = true;

        var r = await ReplyHost.Reply(h.ClientFor(user), id);
        var raw = await r.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal(ErrorCodes.DependencyUnavailable, JsonDocument.Parse(raw).RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(FakeDirectory.Key, raw);
        Assert.DoesNotContain("directory.demo.invalid", raw);
        Assert.DoesNotContain(h.Logs.Lines, l => l.Contains(FakeDirectory.Key) || l.Contains("directory.demo.invalid"));
        Assert.Null((await h.ReviewOf(id)).ReplyText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("         ")]
    [InlineData("krótko")]
    [InlineData("   krótko   ")]
    public async Task A_reply_shorter_than_10_characters_after_trim_is_400_validation_failed(string text)
    {
        var (h, user, pract) = Arrange(); using var _ = h;
        var id = await h.SeedReview(pract);

        var r = await ReplyHost.Reply(h.ClientFor(user), id, text);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, await Code(r));
        Assert.Null((await h.ReviewOf(id)).ReplyText);
    }

    [Fact]
    public async Task The_length_limits_are_10_and_1000_after_trim_and_the_stored_text_is_trimmed()
    {
        var (h, user, pract) = Arrange(); using var _ = h;
        var c = h.ClientFor(user);
        var tooLong = await h.SeedReview(pract); var longest = await h.SeedReview(pract); var shortest = await h.SeedReview(pract);

        Assert.Equal(HttpStatusCode.BadRequest, (await ReplyHost.Reply(c, tooLong, Varied(1001))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ReplyHost.Reply(c, longest, "  " + Varied(1000) + "  ")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ReplyHost.Reply(c, shortest, " abcdefghij ")).StatusCode);

        Assert.Equal(Varied(1000), (await h.ReviewOf(longest)).ReplyText);
        Assert.Equal("abcdefghij", (await h.ReviewOf(shortest)).ReplyText);
    }

    [Fact]
    public async Task A_body_that_is_not_a_JSON_object_is_400()
    {
        var (h, user, pract) = Arrange(); using var _ = h;
        var id = await h.SeedReview(pract);

        var r = await h.ClientFor(user).PostAsync($"/api/v1/reviews/{id}/reply", new StringContent("null", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task The_reply_text_appears_in_no_log_line_not_on_success_nor_on_any_refusal()
    {
        var (h, user, pract) = Arrange(); using var _ = h;
        const string secret = "TAJNA-ODPOWIEDZ-demo-7731 dziękuję za zaufanie";
        var ok = await h.SeedReview(pract); var held = await h.SeedReview(pract, ReviewStatus.Held);
        var c = h.ClientFor(user);

        await ReplyHost.Reply(c, ok, secret);
        await ReplyHost.Reply(c, ok, secret + " drugi raz");  // 409
        await ReplyHost.Reply(c, held, secret);               // 404
        await ReplyHost.Reply(h.ClientFor("demo-nobody"), ok, secret); // 404
        h.Directory.Down = true;
        await ReplyHost.Reply(c, ok, secret);                 // 503

        Assert.NotEmpty(h.Logs.Lines);
        Assert.DoesNotContain(h.Logs.Lines, l => l.Contains("TAJNA-ODPOWIEDZ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Directory_is_asked_with_the_key_and_the_escaped_user_id()
    {
        var (h, user, pract) = Arrange(); using var _ = h;
        var id = await h.SeedReview(pract);

        await ReplyHost.Reply(h.ClientFor(user), id);

        Assert.Equal(FakeDirectory.Key, h.Directory.LastKeyHeader);
        Assert.StartsWith("/internal/v1/practitioners/by-user/", h.Directory.LastPath);
    }

    [Fact]
    public async Task Parallel_replies_to_one_review_give_exactly_one_201_and_the_rest_409()
    {
        var (h, user, pract) = Arrange(); using var _ = h;
        var id = await h.SeedReview(pract);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => ReplyHost.Reply(h.ClientFor(user), id, $"Odpowiedź numer {i} z synthetycznego testu.")));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(7, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Single(await h.EventsOf(id));
        Assert.StartsWith("Odpowiedź numer", (await h.ReviewOf(id)).ReplyText);
    }
}
