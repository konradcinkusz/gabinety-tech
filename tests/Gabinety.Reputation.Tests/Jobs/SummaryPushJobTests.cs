using System.Net;
using Gabinety.Reputation.Jobs.SummaryPush;
using Gabinety.Reputation.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gabinety.Reputation.Tests;

/// <summary>Stands in for <see cref="IDirectoryReputationClient"/>: records every push; <see cref="Result"/> answers, <see cref="Throw"/> simulates a client bug, <see cref="OnPush"/> runs inside the push.</summary>
sealed class RecordingReputationClient : IDirectoryReputationClient
{
    public readonly List<(Guid Practitioner, ReputationPush Body)> Pushes = new();
    public Func<Guid, PushResult> Result = _ => PushResult.Accepted;
    public Func<Guid, bool> Throw = _ => false;
    public Func<Guid, Task>? OnPush;

    public async Task<PushResult> PushAsync(Guid practitionerId, ReputationPush body, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Throw(practitionerId)) throw new InvalidOperationException("boom");
        lock (Pushes) Pushes.Add((practitionerId, body));
        if (OnPush != null) await OnPush(practitionerId);
        return Result(practitionerId);
    }

    public List<ReputationPush> For(Guid id) { lock (Pushes) return Pushes.Where(p => p.Practitioner == id).Select(p => p.Body).ToList(); }
}

public class SummaryPushJobTests
{
    static SummaryPushJob Job(InternalHost h, IDirectoryReputationClient client, ILogger<SummaryPushJob>? log = null) =>
        new(h.Services.GetRequiredService<IServiceScopeFactory>(), client, h.Clock, log ?? NullLogger<SummaryPushJob>.Instance);

    static Task Summary(InternalHost h, Guid p, int count, int sum, long version, long pushed) =>
        h.Seed(db => db.RatingSummaries.Add(new RatingSummary { PractitionerId = p, Count = count, Sum = sum, Version = version, PushedVersion = pushed }));

    [Fact]
    public async Task Only_summaries_with_a_version_above_the_pushed_one_are_sent_and_the_pushed_version_is_recorded()
    {
        await using var h = new InternalHost();
        Guid changed = Guid.NewGuid(), fresh = Guid.NewGuid(), pushed = Guid.NewGuid();
        await Summary(h, changed, 3, 13, 5, 2);
        await Summary(h, fresh, 1, 4, 1, 0);
        await Summary(h, pushed, 2, 9, 7, 7);
        var client = new RecordingReputationClient();

        Assert.Equal(2, await Job(h, client).RunOnceAsync(default));

        Assert.Equal(new ReputationPush(3, 4.33m, 5), Assert.Single(client.For(changed)));
        Assert.Equal(new ReputationPush(1, 4m, 1), Assert.Single(client.For(fresh)));
        Assert.Empty(client.For(pushed));
        Assert.Equal(5, (await h.SummaryOf(changed))!.PushedVersion);
        Assert.Equal(1, (await h.SummaryOf(fresh))!.PushedVersion);
        Assert.Equal(7, (await h.SummaryOf(pushed))!.PushedVersion);
    }

    [Fact]
    public async Task Running_twice_with_no_change_is_one_put_per_summary()
    {
        await using var h = new InternalHost();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        await Summary(h, a, 1, 5, 1, 0);
        await Summary(h, b, 2, 6, 3, 0);
        var client = new RecordingReputationClient();
        var job = Job(h, client);

        Assert.Equal(2, await job.RunOnceAsync(default));
        Assert.Equal(0, await job.RunOnceAsync(default));
        Assert.Equal(0, await Job(h, client).RunOnceAsync(default)); // a fresh job (a restart) reads the state from the table: still nothing to send
        Assert.Single(client.For(a));
        Assert.Single(client.For(b));
    }

    [Fact]
    public async Task A_changed_summary_is_pushed_again_with_a_higher_version_and_versions_never_go_backwards()
    {
        await using var h = new InternalHost();
        var p = Guid.NewGuid();
        await Summary(h, p, 1, 5, 1, 0);
        var client = new RecordingReputationClient();
        var job = Job(h, client);
        await job.RunOnceAsync(default);
        await h.Seed(db => { var s = db.RatingSummaries.Single(x => x.PractitionerId == p); s.Count = 2; s.Sum = 7; s.Version = 2; });
        await job.RunOnceAsync(default);

        var sent = client.For(p);
        Assert.Equal([new ReputationPush(1, 5m, 1), new ReputationPush(2, 3.5m, 2)], sent);
        Assert.Equal(2, (await h.SummaryOf(p))!.PushedVersion);
    }

    [Fact]
    public async Task A_change_made_while_the_push_is_in_flight_is_not_lost_and_the_pushed_version_is_the_one_that_was_sent()
    {
        await using var h = new InternalHost();
        var p = Guid.NewGuid();
        await Summary(h, p, 1, 5, 1, 0);
        var bumped = false;
        var client = new RecordingReputationClient
        {
            OnPush = async _ =>
            {
                if (bumped) return;
                bumped = true;
                await h.Seed(db => { var s = db.RatingSummaries.Single(x => x.PractitionerId == p); s.Count = 2; s.Sum = 9; s.Version = 2; });
            },
        };
        var job = Job(h, client);

        await job.RunOnceAsync(default);
        var mid = (await h.SummaryOf(p))!;
        Assert.Equal((1L, 2L), (mid.PushedVersion, mid.Version)); // the sent version, not the newer one, and the newer data is intact
        Assert.Equal((2, 9), (mid.Count, mid.Sum));

        await job.RunOnceAsync(default);
        Assert.Equal([new ReputationPush(1, 5m, 1), new ReputationPush(2, 4.5m, 2)], client.For(p));
        Assert.Equal(2, (await h.SummaryOf(p))!.PushedVersion);
    }

    [Fact]
    public async Task A_summary_with_no_published_reviews_left_is_pushed_as_zero_count_and_zero_average()
    {
        await using var h = new InternalHost();
        var p = Guid.NewGuid();
        await Summary(h, p, 0, 0, 4, 3);
        var client = new RecordingReputationClient();
        await Job(h, client).RunOnceAsync(default);
        Assert.Equal(new ReputationPush(0, 0m, 4), Assert.Single(client.For(p)));
    }

    [Fact]
    public async Task A_refusal_leaves_the_summary_unpushed_and_the_next_run_pushes_it()
    {
        await using var h = new InternalHost();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        await Summary(h, a, 1, 5, 1, 0);
        await Summary(h, b, 1, 4, 1, 0);
        var down = true;
        var client = new RecordingReputationClient { Result = id => down && id == a ? PushResult.Failed : PushResult.Accepted };
        var job = Job(h, client);

        Assert.Equal(1, await job.RunOnceAsync(default)); // b went through, a did not, and the run did not throw
        Assert.Equal(0, (await h.SummaryOf(a))!.PushedVersion);
        Assert.Equal(1, (await h.SummaryOf(b))!.PushedVersion);

        down = false;
        Assert.Equal(1, await job.RunOnceAsync(default));
        Assert.Equal(1, (await h.SummaryOf(a))!.PushedVersion);
        Assert.Equal(2, client.For(a).Count);
        Assert.Single(client.For(b));
    }

    [Fact]
    public async Task A_client_that_throws_never_throws_out_of_the_job_and_the_others_are_still_pushed()
    {
        await using var h = new InternalHost();
        Guid bad = Guid.NewGuid(), good = Guid.NewGuid();
        await Summary(h, bad, 1, 5, 1, 0);
        await Summary(h, good, 1, 4, 1, 0);
        var client = new RecordingReputationClient { Throw = id => id == bad };

        Assert.Equal(1, await Job(h, client).RunOnceAsync(default));
        Assert.Equal(0, (await h.SummaryOf(bad))!.PushedVersion);
        Assert.Equal(1, (await h.SummaryOf(good))!.PushedVersion);
    }

    [Fact]
    public async Task An_unknown_practitioner_is_not_pushed_again_in_a_tight_loop_but_is_retried_later_or_when_it_changes()
    {
        await using var h = new InternalHost();
        var p = Guid.NewGuid();
        await Summary(h, p, 1, 5, 1, 0);
        var client = new RecordingReputationClient { Result = _ => PushResult.NotFound };
        var job = Job(h, client);

        await job.RunOnceAsync(default);
        h.Clock.Advance(TimeSpan.FromSeconds(60));
        await job.RunOnceAsync(default);
        h.Clock.Advance(TimeSpan.FromSeconds(60));
        await job.RunOnceAsync(default);
        Assert.Single(client.For(p)); // two more cycles later, still one call
        Assert.Equal(0, (await h.SummaryOf(p))!.PushedVersion);

        await h.Seed(db => { var s = db.RatingSummaries.Single(x => x.PractitionerId == p); s.Count = 2; s.Sum = 9; s.Version = 2; });
        await job.RunOnceAsync(default);
        Assert.Equal(2, client.For(p).Count); // new data: tried at once

        h.Clock.Advance(TimeSpan.FromMinutes(11));
        await job.RunOnceAsync(default);
        Assert.Equal(3, client.For(p).Count); // the quiet period is over
    }

    [Fact]
    public async Task Cancellation_is_the_only_thing_that_escapes()
    {
        await using var h = new InternalHost();
        await Summary(h, Guid.NewGuid(), 1, 5, 1, 0);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Job(h, new RecordingReputationClient()).RunOnceAsync(cts.Token));
    }

    [Fact]
    public async Task With_the_real_client_a_down_directory_is_hit_at_most_three_times_per_run_with_no_real_sleep_and_the_next_run_succeeds()
    {
        await using var h = new InternalHost();
        var p = Guid.NewGuid();
        await Summary(h, p, 2, 9, 3, 0);
        var handler = new CountingHandler { Answer = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) };
        var delays = new List<TimeSpan>();
        var client = new DirectoryReputationClient(new HttpClient(handler) { BaseAddress = new Uri(SummaryPushClientTests.BaseUrl + "/") }, SummaryPushClientTests.Key,
            NullLogger<DirectoryReputationClient>.Instance, delay: (d, _) => { delays.Add(d); return Task.CompletedTask; });
        var job = Job(h, client);

        Assert.Equal(0, await job.RunOnceAsync(default));
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(2, delays.Count);
        Assert.All(delays, d => Assert.InRange(d, TimeSpan.Zero, TimeSpan.FromSeconds(5)));
        Assert.Equal(0, (await h.SummaryOf(p))!.PushedVersion);

        handler.Answer = _ => new HttpResponseMessage(HttpStatusCode.NoContent);
        Assert.Equal(1, await job.RunOnceAsync(default));
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal(3, (await h.SummaryOf(p))!.PushedVersion);
        Assert.Equal(0, await job.RunOnceAsync(default));
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task The_job_is_registered_as_a_hosted_service_every_60_seconds_and_no_log_line_carries_a_key_or_review_text()
    {
        await using var h = new InternalHost();
        h.CreateClient();
        Assert.Contains(h.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>(), s => s is SummaryPushHostedService);
        Assert.Equal(TimeSpan.FromSeconds(60), SummaryPushHostedService.Interval);

        var p = Guid.NewGuid();
        await Summary(h, p, 1, 5, 1, 0);
        await h.Seed(db => db.Reviews.Add(InternalHost.NewReview(p, "demo-r")));
        var log = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(h.Logs)).CreateLogger<SummaryPushJob>();
        await Job(h, new RecordingReputationClient { Result = _ => PushResult.Failed }, log).RunOnceAsync(default);
        await Job(h, new RecordingReputationClient { Throw = _ => true }, log).RunOnceAsync(default);
        Assert.DoesNotContain(h.Logs.Lines, l => l.Contains("TEKST-NIE-DO-LOGU") || l.Contains(InternalHost.Key) || l.Contains(SummaryPushClientTests.Key));
    }
}
