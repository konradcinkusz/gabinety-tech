using Gabinety.Contracts.Assistant;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Clients;
using Gabinety.Reputation.Jobs;
using Gabinety.Reputation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Reputation.Tests.Triage;

public class TriageJobTests
{
    static AssistantTriageResult Hold(params TriageReason[] r) => new AssistantTriageResult.Verdict(TriageVerdict.Hold, r, "demo-model-1");
    static readonly AssistantTriageResult None = new AssistantTriageResult.Verdict(TriageVerdict.None, [], "demo-model-1");
    static readonly AssistantTriageResult Down = new AssistantUnavailable("transport");

    [Fact]
    public async Task The_triage_result_is_stored_for_a_held_review()
    {
        using var host = new TriageHost();
        var text = TriageHost.UniqueText();
        var review = TriageHost.Held(text, rating: 2);
        await host.Seed(review);
        host.Assistant.Answer = (_, _) => Hold(TriageReason.PersonalData, TriageReason.OffTopic);

        await host.Run();

        var row = await host.ReviewOf(review.Id);
        Assert.Equal(TriageFlag.Hold, row.TriageFlag);
        Assert.Equal("demo-model-1", row.TriageModelId);
        Assert.Equal(["contains_link", "personal_data", "off_topic"], row.HoldReasons); // the moderator sees them next to the rule's reason
        var call = Assert.Single(host.Assistant.Calls);
        Assert.Equal((2, text), call);
        var ev = Assert.Single(await host.TriageEventsOf(review.Id));
        Assert.Equal(host.Clock.GetUtcNow(), ev.At);
    }

    [Fact]
    public async Task A_hold_stays_Held_and_a_none_does_not_publish()
    {
        using var host = new TriageHost();
        var a = TriageHost.Held(TriageHost.UniqueText());
        var b = TriageHost.Held(TriageHost.UniqueText());
        await host.Seed(a, b);
        host.Assistant.Answer = (_, text) => text == a.Text ? Hold(TriageReason.Spam) : None;

        await host.Run();

        var ra = await host.ReviewOf(a.Id);
        var rb = await host.ReviewOf(b.Id);
        Assert.Equal((ReviewStatus.Held, TriageFlag.Hold), (ra.Status, ra.TriageFlag));
        Assert.Equal((ReviewStatus.Held, TriageFlag.None), (rb.Status, rb.TriageFlag));
        Assert.Equal("demo-model-1", rb.TriageModelId);
        Assert.Null(rb.PublishedAt);
        Assert.Equal(["contains_link"], rb.HoldReasons);
        Assert.Empty(await host.Query(db => db.RatingSummaries.ToListAsync())); // nothing was counted
        Assert.Equal("triage:none", (await host.TriageEventsOf(b.Id)).Single().Note); // the event says what the model said
    }

    [Fact]
    public async Task An_outage_stores_unavailable_does_not_throw_and_does_not_change_the_state()
    {
        using var host = new TriageHost();
        var review = TriageHost.Held(TriageHost.UniqueText());
        await host.Seed(review);
        host.Assistant.Answer = (_, _) => Down;

        var ex = await Record.ExceptionAsync(() => host.Run());

        Assert.Null(ex);
        var row = await host.ReviewOf(review.Id);
        Assert.Equal((ReviewStatus.Held, TriageFlag.Unavailable, (string?)null), (row.Status, row.TriageFlag, row.TriageModelId));
        Assert.Equal(["contains_link"], row.HoldReasons);
        Assert.Null(row.PublishedAt);
    }

    [Fact]
    public async Task A_client_that_throws_is_the_same_as_an_outage()
    {
        using var host = new TriageHost();
        var review = TriageHost.Held(TriageHost.UniqueText());
        await host.Seed(review);
        host.Assistant.Answer = (_, _) => throw new InvalidOperationException("boom " + review.Text);

        var ex = await Record.ExceptionAsync(() => host.Run());

        Assert.Null(ex);
        Assert.Equal(TriageFlag.Unavailable, (await host.ReviewOf(review.Id)).TriageFlag);
        Assert.DoesNotContain(host.Logs.Lines, l => l.Contains(review.Text));
    }

    [Fact]
    public async Task Running_twice_calls_the_assistant_once_per_review()
    {
        using var host = new TriageHost();
        var a = TriageHost.Held(TriageHost.UniqueText());
        var b = TriageHost.Held(TriageHost.UniqueText());
        await host.Seed(a, b);

        await host.Run();
        await host.Run();
        host.Clock.Advance(TimeSpan.FromDays(1));
        await host.Run();

        Assert.Equal(1, host.Assistant.CallsFor(a.Text));
        Assert.Equal(1, host.Assistant.CallsFor(b.Text));
    }

    [Fact]
    public async Task Only_Held_reviews_without_a_result_are_asked()
    {
        using var host = new TriageHost();
        var held = TriageHost.Held(TriageHost.UniqueText());
        var published = TriageHost.Held(TriageHost.UniqueText()); published.Status = ReviewStatus.Published;
        var rejected = TriageHost.Held(TriageHost.UniqueText()); rejected.Status = ReviewStatus.Rejected;
        await host.Seed(held, published, rejected);

        await host.Run();

        Assert.Equal([held.Text], host.Assistant.Calls.Select(c => c.Text));
    }

    [Fact]
    public async Task The_batch_is_bounded_and_oldest_first()
    {
        using var host = new TriageHost();
        var t0 = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var all = Enumerable.Range(0, TriageJob.BatchSize + 5).Select(i => TriageHost.Held(TriageHost.UniqueText(), t0.AddMinutes(-i))).ToArray(); // index 0 is the newest
        await host.Seed(all);

        var first = await host.Run();

        Assert.Equal(TriageJob.BatchSize, first);
        var asked = host.Assistant.Calls.Select(c => c.Text).ToHashSet();
        Assert.All(all.Skip(5), r => Assert.Contains(r.Text, asked));    // the 20 oldest
        Assert.All(all.Take(5), r => Assert.DoesNotContain(r.Text, asked)); // the 5 newest wait
        Assert.Equal(5, await host.Run());
    }

    [Fact]
    public async Task After_an_outage_it_retries_later_with_backoff_for_a_bounded_number_of_attempts()
    {
        using var host = new TriageHost();
        var review = TriageHost.Held(TriageHost.UniqueText());
        await host.Seed(review);
        host.Assistant.Answer = (_, _) => Down;

        await host.Run();                                       // attempt 1
        await host.Run();                                       // too early
        Assert.Equal(1, host.Assistant.CallsFor(review.Text));
        host.Clock.Advance(TriageJob.Backoffs[0] - TimeSpan.FromSeconds(1));
        await host.Run();                                       // still too early
        Assert.Equal(1, host.Assistant.CallsFor(review.Text));
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        await host.Run();                                       // attempt 2
        Assert.Equal(2, host.Assistant.CallsFor(review.Text));
        host.Clock.Advance(TriageJob.Backoffs[1]);
        await host.Run();                                       // attempt 3
        Assert.Equal(3, host.Assistant.CallsFor(review.Text));
        host.Clock.Advance(TimeSpan.FromDays(7));
        await host.Run();                                       // gives up: bounded
        Assert.Equal(TriageJob.MaxAttempts, host.Assistant.CallsFor(review.Text));
        Assert.Equal(3, TriageJob.MaxAttempts);

        var row = await host.ReviewOf(review.Id);
        Assert.Equal((ReviewStatus.Held, TriageFlag.Unavailable), (row.Status, row.TriageFlag));
        Assert.Equal(3, (await host.TriageEventsOf(review.Id)).Count);
    }

    [Fact]
    public async Task A_retry_that_succeeds_replaces_unavailable_with_the_result()
    {
        using var host = new TriageHost();
        var review = TriageHost.Held(TriageHost.UniqueText());
        await host.Seed(review);
        host.Assistant.Answer = (_, _) => Down;
        await host.Run();
        host.Assistant.Answer = (_, _) => Hold(TriageReason.Abuse);
        host.Clock.Advance(TriageJob.Backoffs[0]);

        await host.Run();

        var row = await host.ReviewOf(review.Id);
        Assert.Equal((ReviewStatus.Held, TriageFlag.Hold, "demo-model-1"), (row.Status, row.TriageFlag, row.TriageModelId));
        host.Clock.Advance(TimeSpan.FromDays(1));
        await host.Run();
        Assert.Equal(2, host.Assistant.CallsFor(review.Text));
    }

    [Fact]
    public async Task A_review_moderated_while_the_call_was_in_flight_is_not_touched()
    {
        using var host = new TriageHost();
        var review = TriageHost.Held(TriageHost.UniqueText());
        await host.Seed(review);
        host.Assistant.Answer = (_, _) =>
        {
            // The administrator publishes while the model is thinking.
            host.Query(async db => { var r = await db.Reviews.SingleAsync(x => x.Id == review.Id); r.Status = ReviewStatus.Published; r.PublishedAt = host.Clock.GetUtcNow(); await db.SaveChangesAsync(); return 0; }).GetAwaiter().GetResult();
            return Hold(TriageReason.Spam);
        };

        await host.Run();

        var row = await host.ReviewOf(review.Id);
        Assert.Equal((ReviewStatus.Published, TriageFlag.None, (string?)null), (row.Status, row.TriageFlag, row.TriageModelId));
        Assert.Empty(await host.TriageEventsOf(review.Id));
    }

    [Fact]
    public async Task The_review_text_and_the_internal_key_appear_in_no_log_line()
    {
        using var host = new TriageHost();
        var text = TriageHost.UniqueText() + " TRIAGE-TEKST-NIE-DO-LOGU";
        var ok = TriageHost.Held(text);
        var bad = TriageHost.Held(text + " drugi");
        await host.Seed(ok, bad);
        host.Assistant.Answer = (_, t) => t == ok.Text ? Hold(TriageReason.Spam) : throw new HttpRequestException($"{t} {FakeAssistantHandler.Key} {FakeAssistantHandler.BaseUrl}");

        await host.Run();

        Assert.Contains(host.Logs.Lines, l => l.Contains(nameof(TriageJob))); // the job does log (counts and ids), so the checks below can fail
        Assert.DoesNotContain(host.Logs.Lines, l => l.Contains("TRIAGE-TEKST") || l.Contains(FakeAssistantHandler.Key) || l.Contains("demo.invalid"));
    }

    [Fact]
    public async Task The_hosted_service_runs_the_job_on_its_interval_when_an_assistant_is_configured()
    {
        using var host = new TriageHost
        {
            Settings = { ["Backend:Assistant"] = FakeAssistantHandler.BaseUrl, ["Reviews:TriageIntervalSeconds"] = "0.05" },
        };
        var review = TriageHost.Held(TriageHost.UniqueText());
        await host.Seed(review);
        _ = host.Server; // starts the host and its hosted services

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && (await host.ReviewOf(review.Id)).TriageModelId is null) await Task.Delay(50);

        Assert.Equal("demo-model", (await host.ReviewOf(review.Id)).TriageModelId);
    }

    [Fact]
    public async Task Without_a_configured_assistant_the_hosted_service_stays_idle()
    {
        using var host = new TriageHost { Settings = { ["Reviews:TriageIntervalSeconds"] = "0.05" } };
        var review = TriageHost.Held(TriageHost.UniqueText());
        await host.Seed(review);
        _ = host.Server;

        await Task.Delay(600);

        Assert.Empty(host.Assistant.Calls);
        Assert.Equal(TriageFlag.None, (await host.ReviewOf(review.Id)).TriageFlag);
    }
}
