using Gabinety.Booking.Jobs.Base;
using Microsoft.Extensions.Logging;

namespace Gabinety.Booking.Tests;

public class BasePeriodicJobTests : IDisposable
{
    static readonly TimeSpan Minute = TimeSpan.FromSeconds(60);
    readonly TickClock clock = new();
    readonly TestMeterFactory meters = new();
    readonly LogSink logs = new();
    readonly MetricCapture capture;

    public BasePeriodicJobTests() => capture = new MetricCapture(meters);
    public void Dispose() { capture.Dispose(); meters.Dispose(); }

    TestJob Job(Func<int, CancellationToken, Task>? body = null, params (string Key, string Value)[] config) =>
        new(clock, BaseFx.Config(config), new JobMetrics(meters), logs.CreateLogger("job"), body);

    /// <summary>Starts the job and waits until its loop has created the timer: the host starts ExecuteAsync on its own thread, so the clock must not move before that.</summary>
    async Task Start(TestJob job)
    {
        await job.StartAsync(default);
        await BaseFx.Eventually(() => clock.TimerCount == 1, "the loop created its timer");
    }

    static async Task StopQuickly(TestJob job)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await job.StopAsync(cts.Token);
    }

    [Fact]
    public async Task The_interval_is_60_seconds_of_the_injected_clock_and_nothing_runs_earlier()
    {
        var job = Job();
        await Start(job);

        clock.Advance(TimeSpan.FromSeconds(59));
        clock.Advance(TimeSpan.FromSeconds(1));
        await BaseFx.Eventually(() => job.Runs.Count == 1, "first run");
        Assert.Equal(clock.Start + Minute, job.Runs[0]); // a run at +59 s would be Runs[0]

        clock.Advance(Minute);
        await BaseFx.Eventually(() => job.Runs.Count == 2, "second run");
        Assert.Equal(clock.Start + 2 * Minute, job.Runs[1]);
        await StopQuickly(job);
    }

    [Fact]
    public async Task The_interval_comes_from_Jobs_IntervalSeconds()
    {
        var job = Job(null, ("Jobs:IntervalSeconds", "2"));
        await Start(job);
        clock.Advance(TimeSpan.FromSeconds(2));
        await BaseFx.Eventually(() => job.Runs.Count == 1, "first run");
        clock.Advance(TimeSpan.FromSeconds(2));
        await BaseFx.Eventually(() => job.Runs.Count == 2, "second run");
        Assert.Equal([clock.Start.AddSeconds(2), clock.Start.AddSeconds(4)], job.Runs);
        await StopQuickly(job);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("soon")]
    public async Task A_non_positive_or_unreadable_interval_falls_back_to_60_seconds(string value)
    {
        var job = Job(null, ("Jobs:IntervalSeconds", value));
        await Start(job);
        clock.Advance(TimeSpan.FromSeconds(59));
        clock.Advance(TimeSpan.FromSeconds(1));
        await BaseFx.Eventually(() => job.Runs.Count == 1, "first run");
        Assert.Equal(clock.Start + Minute, job.Runs[0]);
        await StopQuickly(job);
    }

    [Fact]
    public async Task An_exception_in_a_run_never_escapes_the_loop_and_the_next_cycle_runs()
    {
        var job = Job((n, _) => n == 1 ? throw new InvalidOperationException("demo-brief: SECRET text") : Task.CompletedTask);
        await Start(job);

        clock.Advance(Minute);
        await BaseFx.Eventually(() => capture.Runs("test_job", "error") == 1, "the failed run is counted");
        clock.Advance(Minute);
        await BaseFx.Eventually(() => capture.Runs("test_job", "ok") == 1, "the next cycle ran and succeeded");

        Assert.Equal(2, job.Runs.Count);
        Assert.False(job.ExecuteTask!.IsCompleted, "the loop must still be alive");
        var line = Assert.Single(logs.Lines, l => l.Contains("test_job"));
        Assert.Contains("InvalidOperationException", line);
        Assert.DoesNotContain("SECRET", string.Join("\n", logs.Lines));
        Assert.DoesNotContain("demo-brief", string.Join("\n", logs.Lines));
        await StopQuickly(job);
    }

    [Fact]
    public async Task A_cancellation_exception_that_is_not_a_shutdown_is_an_error_run_not_the_end_of_the_loop()
    {
        var job = Job((n, _) => n == 1 ? throw new OperationCanceledException() : Task.CompletedTask);
        await Start(job);
        clock.Advance(Minute);
        await BaseFx.Eventually(() => capture.Runs("test_job", "error") == 1, "counted as an error");
        clock.Advance(Minute);
        await BaseFx.Eventually(() => job.Runs.Count == 2, "next cycle");
        await StopQuickly(job);
    }

    [Fact]
    public async Task Metrics_use_the_closed_names_and_only_the_allowed_labels()
    {
        var job = Job((n, _) => n == 2 ? throw new InvalidOperationException("x") : Task.CompletedTask);
        await Start(job);
        for (var i = 1; i <= 3; i++)
        {
            clock.Advance(Minute);
            var expected = i;
            await BaseFx.Eventually(() => capture.Of("gabinety_job_runs_total").Count == expected, $"run {expected} counted");
        }

        var runs = capture.Of("gabinety_job_runs_total");
        Assert.All(runs, p => Assert.Equal(["job", "outcome"], p.Tags.Keys.Order()));
        Assert.All(runs, p => Assert.Equal("test_job", p.Tags["job"]));
        Assert.Equal(["error", "ok", "ok"], runs.Select(p => (string)p.Tags["outcome"]!).Order());
        Assert.All(runs, p => Assert.Equal(1, p.Value));

        var lag = capture.Of("gabinety_job_lag_seconds");
        Assert.Equal(3, lag.Count);
        Assert.All(lag, p => Assert.Equal(["job"], p.Tags.Keys));
        Assert.All(lag, p => Assert.Equal(0, p.Value)); // each tick was handled the moment it was due
        Assert.Empty(capture.Points.Select(p => p.Instrument).Except(["gabinety_job_runs_total", "gabinety_job_lag_seconds"]));
        await StopQuickly(job);
    }

    [Fact]
    public async Task A_late_tick_records_how_late_the_run_started_against_when_it_was_due()
    {
        var job = Job();
        await Start(job);
        clock.Advance(TimeSpan.FromSeconds(67)); // due at +60, handled at +67
        await BaseFx.Eventually(() => capture.Of("gabinety_job_lag_seconds").Count == 1, "lag recorded");
        Assert.Equal(7, capture.Of("gabinety_job_lag_seconds")[0].Value);

        clock.Advance(TimeSpan.FromSeconds(53)); // the next due time is +120, on time
        await BaseFx.Eventually(() => capture.Of("gabinety_job_lag_seconds").Count == 2, "second lag recorded");
        Assert.Equal(0, capture.Of("gabinety_job_lag_seconds")[1].Value);
        await StopQuickly(job);
    }

    [Fact]
    public async Task RunCycle_called_directly_measures_lag_from_the_due_time_and_never_reports_a_negative_one()
    {
        var job = Job();
        Assert.True(await job.RunCycleAsync(clock.GetUtcNow().AddSeconds(-5), default));
        Assert.True(await job.RunCycleAsync(clock.GetUtcNow().AddSeconds(30), default));
        Assert.Equal([5.0, 0.0], capture.Of("gabinety_job_lag_seconds").Select(p => p.Value));
        Assert.Equal(2, capture.Runs("test_job", "ok"));
    }

    [Fact]
    public async Task A_job_started_and_stopped_at_once_shuts_down_cleanly_without_running()
    {
        var job = Job();
        await job.StartAsync(default);
        await StopQuickly(job);
        Assert.True(job.ExecuteTask!.Status is TaskStatus.RanToCompletion or TaskStatus.Canceled, "status " + job.ExecuteTask.Status); // Canceled when the stop came before the host began ExecuteAsync
        Assert.Empty(job.Runs);
        Assert.Equal(0, clock.TimerCount); // the timer is disposed
        Assert.Empty(logs.Lines);
        Assert.Empty(capture.Points);
    }

    [Fact]
    public async Task Stopping_in_the_middle_of_a_run_cancels_it_and_is_neither_an_error_nor_a_counted_run()
    {
        var job = Job((_, ct) => Task.Delay(Timeout.Infinite, ct));
        await Start(job);
        clock.Advance(Minute);
        await BaseFx.Eventually(() => job.Runs.Count == 1, "the run is in flight");

        await StopQuickly(job);

        Assert.Equal(TaskStatus.RanToCompletion, job.ExecuteTask!.Status); // the run was in flight, so the loop had started: it ends by itself, not by cancellation
        Assert.Empty(capture.Of("gabinety_job_runs_total"));
        Assert.Empty(logs.Lines);
    }

    [Fact]
    public async Task Jobs_Enabled_false_starts_no_timer_and_runs_nothing()
    {
        var job = Job(null, ("Jobs:Enabled", "false"));
        await job.StartAsync(default);
        await job.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10)); // the loop returned at once
        Assert.Equal(0, clock.TimerCount);
        clock.Advance(10 * Minute);
        await StopQuickly(job);
        Assert.Empty(job.Runs);
    }
}
