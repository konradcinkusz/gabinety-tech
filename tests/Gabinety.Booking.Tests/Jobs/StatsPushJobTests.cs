using Gabinety.Booking.Jobs.StatsPush;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

public class StatsPushJobTests : IDisposable
{
    readonly CreateHost h = Life.Host(new RecordingProvider());
    readonly RecordingStatsClient client = new();
    public void Dispose() => h.Dispose();

    [Fact]
    public async Task First_run_pushes_once_and_the_next_run_with_nothing_changed_pushes_nothing()
    {
        await StatsFx.Seed(h, BookingStatus.Completed);
        var job = StatsFx.Job(h, client);
        await job.RunOnceAsync(default);
        await job.RunOnceAsync(default);
        await job.RunOnceAsync(default);
        var pushes = client.For(h.Practitioner);
        var p = Assert.Single(pushes);
        Assert.Equal(1, p.CompletedCount);
    }

    static Task Change(CreateHost h, string what) => what switch
    {
        "completed" => StatsFx.Seed(h, BookingStatus.Completed),
        "practitioner cancel" => StatsFx.Seed(h, BookingStatus.CancelledByPractitioner),
        "no-show upheld" => StatsFx.Seed(h, BookingStatus.Resolved, "refund_full", "practitioner_no_show"),
        "open slots" => h.Seed(db => db.Exceptions.Add(new() { Id = Guid.NewGuid(), PractitionerId = h.Practitioner, DateLocal = new(2026, 11, 18) })),
        _ => throw new ArgumentOutOfRangeException(nameof(what)),
    };

    [Theory]
    [InlineData("completed")]
    [InlineData("practitioner cancel")]
    [InlineData("no-show upheld")]
    [InlineData("open slots")]
    public async Task Any_one_of_the_four_values_changing_triggers_exactly_one_new_push(string what)
    {
        await h.WorkWeek();
        await StatsFx.Seed(h, BookingStatus.Completed);
        var job = StatsFx.Job(h, client);
        await job.RunOnceAsync(default);
        Assert.Single(client.For(h.Practitioner));

        await Change(h, what);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await job.RunOnceAsync(default);
        await job.RunOnceAsync(default);
        Assert.Equal(2, client.For(h.Practitioner).Count);
    }

    [Fact]
    public async Task Versions_strictly_increase_even_when_the_clock_does_not_move_and_are_unix_milliseconds()
    {
        await StatsFx.Seed(h, BookingStatus.Completed);
        var job = StatsFx.Job(h, client);
        await job.RunOnceAsync(default);
        for (var i = 0; i < 3; i++) { await StatsFx.Seed(h, BookingStatus.Completed); await job.RunOnceAsync(default); } // same instant each time
        var versions = client.For(h.Practitioner).Select(p => p.Version).ToList();
        Assert.Equal(4, versions.Count);
        Assert.Equal(versions.OrderBy(v => v).Distinct(), versions);
        // the version counter is shared by every test host in the process and other tests advance their own clocks (by minutes) before pushing,
        // so the first version is this clock's value or later, but never a different unit (seconds, ticks): allow a day of drift upwards
        var clockMs = h.Clock.GetUtcNow().ToUnixTimeMilliseconds();
        Assert.InRange(versions[0], clockMs, clockMs + (long)TimeSpan.FromDays(1).TotalMilliseconds);
    }

    [Fact]
    public async Task A_fresh_job_after_a_restart_pushes_equal_data_once_with_a_higher_version_and_then_stays_quiet()
    {
        await StatsFx.Seed(h, BookingStatus.Completed);
        await StatsFx.Job(h, client).RunOnceAsync(default);
        h.Clock.Advance(TimeSpan.FromMinutes(5)); // the restart takes a while
        var restarted = StatsFx.Job(h, client);
        await restarted.RunOnceAsync(default);
        await restarted.RunOnceAsync(default);
        var pushes = client.For(h.Practitioner);
        Assert.Equal(2, pushes.Count);
        Assert.True(pushes[1].Version > pushes[0].Version);
        Assert.Equal(pushes[0] with { Version = 0 }, pushes[1] with { Version = 0 });
    }

    [Fact]
    public async Task A_failed_push_is_not_remembered_and_is_tried_again_next_cycle()
    {
        await StatsFx.Seed(h, BookingStatus.Completed);
        var job = StatsFx.Job(h, client);
        client.Result = false;
        await job.RunOnceAsync(default);
        client.Result = true;
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await job.RunOnceAsync(default);
        await job.RunOnceAsync(default);
        Assert.Equal(2, client.For(h.Practitioner).Count);
    }

    [Fact]
    public async Task A_client_that_throws_never_throws_out_of_the_job()
    {
        await StatsFx.Seed(h, BookingStatus.Completed);
        client.Throw = true;
        await StatsFx.Job(h, client).RunOnceAsync(default);
        client.Throw = false;
        await StatsFx.Job(h, client).RunOnceAsync(default);
        Assert.Single(client.For(h.Practitioner));
    }

    [Fact]
    public async Task Cancellation_is_honoured()
    {
        await StatsFx.Seed(h, BookingStatus.Completed);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StatsFx.Job(h, client).RunOnceAsync(cts.Token));
    }

    [Fact]
    public async Task The_job_logs_ids_and_counts_only()
    {
        await StatsFx.Seed(h, BookingStatus.Completed);
        await h.Seed(db => db.Bookings.Single(b => b.PractitionerId == h.Practitioner).BriefText = "demo-secret-brief-text");
        client.Throw = true;
        await StatsFx.Job(h, client, h.Services.GetRequiredService<ILogger<StatsPushJob>>()).RunOnceAsync(default);
        var mine = h.Logs.Lines.Where(l => l.Contains("StatsPush")).ToList();
        Assert.NotEmpty(mine);
        Assert.DoesNotContain(mine, l => l.Contains("demo-secret-brief-text") || l.Contains(FakeDirectory.Key) || l.Contains(FakeDirectory.BaseUrl));
    }
}
