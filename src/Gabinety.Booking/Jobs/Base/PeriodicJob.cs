using System.Globalization;
using Gabinety.ServiceDefaults;

namespace Gabinety.Booking.Jobs.Base;

/// <summary>Thrown by a job's run after it has handled every item, when some items failed: the cycle is counted as an error. The message holds a count only.</summary>
public sealed class JobFailedException(int failed) : Exception($"{failed} item(s) failed");

/// <summary>
/// Base of the periodic jobs that move bookings (CONTRACTS 8.1): a hosted service that calls <see cref="RunOnceAsync"/> every
/// <c>Jobs:IntervalSeconds</c> (default 60) of the injected <see cref="TimeProvider"/>, so a test with a controllable clock needs no real sleep.
/// <list type="bullet">
/// <item>Nothing but shutdown leaves the loop: a run that throws is logged (job name and exception TYPE only, never the message, which may hold a brief) and counted
/// <c>error</c>; the next cycle runs as usual.</item>
/// <item>Shutdown cancels the token the run got; that is not a run outcome and is not logged.</item>
/// <item>Each cycle records <c>gabinety_job_lag_seconds{job}</c> (how late it started against when it was due) and <c>gabinety_job_runs_total{job,outcome}</c>.</item>
/// <item><c>Jobs:Enabled=false</c> makes the service return at start without a timer.</item>
/// </list>
/// A job must be idempotent and safe to run in two processes at once: the loop gives no mutual exclusion across instances (the row lock of
/// <c>BookingTransitions</c> does). The first run comes one interval after start, like the stats push.
/// </summary>
public abstract class PeriodicJob(TimeProvider time, IConfiguration config, JobMetrics metrics, ILogger log) : BackgroundService
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);

    /// <summary>The configuration the job was built with, for its own settings.</summary>
    protected IConfiguration Configuration => config;

    /// <summary>The <c>job</c> label: a constant per job class (<c>hold_expiry</c>), never built from data.</summary>
    public abstract string Name { get; }

    /// <summary>One run of the work. <paramref name="now"/> is read once by the loop. Throw to report a failure; only <paramref name="ct"/> may cancel it.</summary>
    public abstract Task RunOnceAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// One cycle as the loop runs it: lag, the run, the outcome metric. Returns false when the run failed. A shutdown while running propagates as
    /// <see cref="OperationCanceledException"/>. Public so a test can drive a cycle without a timer.
    /// </summary>
    public async Task<bool> RunCycleAsync(DateTimeOffset due, CancellationToken ct)
    {
        var started = time.GetUtcNow();
        metrics.Lag(Name, Math.Max(0, (started - due).TotalSeconds));
        try
        {
            await RunOnceAsync(started, ct);
            metrics.RunFinished(Name, JobOutcome.Ok);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            log.LogError("Job {Job} run failed: {ExceptionType}", Name, ex.GetType().Name);
            metrics.RunFinished(Name, JobOutcome.Error);
            return false;
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => LoopAsync(stoppingToken);

    async Task LoopAsync(CancellationToken stop)
    {
        if (!ReadBool(config, "Jobs:Enabled", true)) return;
        var interval = ReadInterval(config);
        using var timer = new PeriodicTimer(interval, time);
        var due = time.GetUtcNow() + interval;
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                await RunCycleAsync(due, stop);
                due = NextDue(due, interval, time.GetUtcNow());
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    /// <summary>The next due time is one interval after the last, so a slow run shows as lag; ticks the timer coalesced while the run was busy are skipped.</summary>
    static DateTimeOffset NextDue(DateTimeOffset due, TimeSpan interval, DateTimeOffset now)
    {
        var next = due + interval;
        while (next + interval <= now) next += interval;
        return next;
    }

    static TimeSpan ReadInterval(IConfiguration config) =>
        double.TryParse(config["Jobs:IntervalSeconds"], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0 && s < 86_400
            ? TimeSpan.FromSeconds(s) : DefaultInterval;

    static bool ReadBool(IConfiguration config, string key, bool fallback) => bool.TryParse(config[key], out var v) ? v : fallback;

    /// <summary>A positive integer setting, or <paramref name="fallback"/> when it is absent, unreadable or not positive.</summary>
    protected static int ReadPositiveInt(IConfiguration config, string key, int fallback) =>
        int.TryParse(config[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : fallback;
}

/// <summary>Registers what every periodic job shares. A job registers itself in its own feature as a singleton and as a hosted service.</summary>
public sealed class JobsBaseFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) => builder.Services.AddSingleton<JobMetrics>();
    public void Map(IEndpointRouteBuilder endpoints) { }
}
