using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Contracts.Assistant;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Clients;
using Gabinety.Reputation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Reputation.Tests.Triage;

/// <summary>CONTRACTS 5.3 step 2 in the create path: triage after the rules, before the write.</summary>
public class CreateTriageTests
{
    static AssistantTriageResult Hold(params TriageReason[] r) => new AssistantTriageResult.Verdict(TriageVerdict.Hold, r, "demo-model-1");
    static readonly AssistantTriageResult None = new AssistantTriageResult.Verdict(TriageVerdict.None, [], "demo-model-1");
    static readonly AssistantTriageResult Down = new AssistantUnavailable("transport");

    static async Task<(HttpStatusCode Code, JsonElement Body)> Created(HttpResponseMessage r) => (r.StatusCode, (await r.Content.ReadFromJsonAsync<JsonElement>()));

    [Theory]
    [InlineData(TriageReason.Spam, "spam")]            // the T-05.04 block's example
    [InlineData(TriageReason.PersonalData, "personal_data")]
    [InlineData(TriageReason.OffTopic, "off_topic")]
    public async Task A_hold_answer_makes_the_review_Held_with_the_reason_from_the_answer(TriageReason reason, string code)
    {
        using var h = new TriageHost();
        var (user, pract, booking) = h.NewBooking();
        h.Assistant.Answer = (_, _) => Hold(reason);

        var (status, body) = await Created(await h.Post(user, booking, rating: 2));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("Held", body.GetProperty("status").GetString());
        var row = Assert.Single(await h.ReviewsOf(pract));
        Assert.Equal((ReviewStatus.Held, TriageFlag.Hold, "demo-model-1"), (row.Status, row.TriageFlag, row.TriageModelId));
        Assert.Equal([code], row.HoldReasons);
        Assert.Null(row.PublishedAt);
        Assert.Null(await h.SummaryOf(pract)); // a held review is not counted
        var events = await h.EventsOf(row.Id);
        Assert.Equal([("system", ModerationEventKind.Created), ("triage", ModerationEventKind.Held)], events.Select(e => (e.Actor, e.Kind)).ToArray());
    }

    [Fact]
    public async Task A_none_answer_publishes_and_the_summary_moves_with_it()
    {
        using var h = new TriageHost();
        var (user, pract, booking) = h.NewBooking();
        h.Assistant.Answer = (_, _) => None;

        var (status, body) = await Created(await h.Post(user, booking, rating: 4));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("Published", body.GetProperty("status").GetString());
        var row = Assert.Single(await h.ReviewsOf(pract));
        Assert.Equal((ReviewStatus.Published, TriageFlag.None, "demo-model-1"), (row.Status, row.TriageFlag, row.TriageModelId));
        Assert.Equal(h.Clock.GetUtcNow(), row.PublishedAt);
        var s = await h.SummaryOf(pract);
        Assert.Equal((1, 4, 1L), (s!.Count, s.Sum, s.Version));
    }

    [Fact]
    public async Task With_the_assistant_down_the_review_is_published_and_flagged_unavailable()
    {
        using var h = new TriageHost();
        var (user, pract, booking) = h.NewBooking();
        h.Assistant.Answer = (_, _) => Down;

        var (status, body) = await Created(await h.Post(user, booking, rating: 5));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("Published", body.GetProperty("status").GetString());
        var row = Assert.Single(await h.ReviewsOf(pract));
        Assert.Equal((ReviewStatus.Published, TriageFlag.Unavailable, (string?)null), (row.Status, row.TriageFlag, row.TriageModelId));
        Assert.Equal(1, (await h.SummaryOf(pract))!.Count);
        Assert.Single(h.Assistant.Calls); // no retry (SPEC 5.1)
    }

    [Fact]
    public async Task A_client_that_throws_is_the_same_as_an_outage_and_nothing_is_logged_from_it()
    {
        using var h = new TriageHost();
        var (user, pract, booking) = h.NewBooking();
        h.Assistant.Answer = (_, t) => throw new HttpRequestException($"{t} {FakeAssistantHandler.Key} {FakeAssistantHandler.BaseUrl}");

        var r = await h.Post(user, booking);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(TriageFlag.Unavailable, Assert.Single(await h.ReviewsOf(pract)).TriageFlag);
        Assert.DoesNotContain(h.Logs.Lines, l => l.Contains("TRIAGE-TEKST") || l.Contains(FakeAssistantHandler.Key) || l.Contains("demo.invalid"));
    }

    [Fact]
    public async Task A_review_that_fails_the_rules_is_Held_and_never_reaches_the_assistant()
    {
        using var h = new TriageHost();
        var (user, pract, booking) = h.NewBooking();

        var r = await h.Post(user, booking, text: "Synthetyczna opinia, kontakt: demo@example.invalid lub +48 600 100 200, polecam.");

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Empty(h.Assistant.Calls);
        var row = Assert.Single(await h.ReviewsOf(pract));
        Assert.Equal((ReviewStatus.Held, TriageFlag.None, (string?)null), (row.Status, row.TriageFlag, row.TriageModelId));
        Assert.Contains("contact_details", row.HoldReasons);
    }

    [Fact]
    public async Task Requests_that_end_before_the_publication_decision_never_reach_the_assistant()
    {
        using var h = new TriageHost();
        var (user, pract, booking) = h.NewBooking();
        var notReviewable = h.Booking.Add(user, pract, reviewable: false);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await h.Post(user, booking, text: "za krótko")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await h.Post(user, notReviewable)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Post("demo-other", booking)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await h.Post(user, booking)).StatusCode);
        var calls = h.Assistant.Calls.Count;
        Assert.Equal(HttpStatusCode.Conflict, (await h.Post(user, booking)).StatusCode);

        Assert.Equal(1, calls);                         // only the one that passed everything
        Assert.Equal(calls, h.Assistant.Calls.Count);   // the 409 did not ask again
    }

    [Fact]
    public async Task The_assistant_is_called_before_the_review_is_written_with_the_rating_and_the_trimmed_text()
    {
        using var h = new TriageHost();
        var (user, pract, booking) = h.NewBooking();
        var rowsWhenAsked = -1;
        h.Assistant.Answer = (_, _) =>
        {
            rowsWhenAsked = h.ReviewsOf(pract).GetAwaiter().GetResult().Count; // no row, so no transaction can be open across the call
            return None;
        };

        await h.Post(user, booking, rating: 3, text: "   " + TriageHost.GoodText + "  ");

        Assert.Equal(0, rowsWhenAsked);
        Assert.Equal((3, TriageHost.GoodText), Assert.Single(h.Assistant.Calls));
    }

    [Fact]
    public async Task The_review_text_appears_in_no_log_line_on_any_path()
    {
        using var h = new TriageHost();
        foreach (var answer in new[] { None, Hold(TriageReason.Abuse), Down })
        {
            var (user, _, booking) = h.NewBooking();
            h.Assistant.Answer = (_, _) => answer;
            await h.Post(user, booking);
        }

        Assert.Contains(h.Logs.Lines, l => l.Contains("created for practitioner")); // the path does log, so the check can fail
        Assert.DoesNotContain(h.Logs.Lines, l => l.Contains("TRIAGE-TEKST"));
    }
}
