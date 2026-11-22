using System.Threading.Channels;
using Gabinety.Reputation.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Reputation.Jobs.SummaryPush;

/// <summary>
/// One cycle of the summary push (CONTRACTS 5.3 step 3, SPEC 5.1): sends every rating summary whose <c>version</c> is above its <c>pushed_version</c> and, once
/// Directory accepts, records the version that was SENT as <c>pushed_version</c> (never lower than before, so a change made while the push was in flight is
/// pushed on the next cycle). Idempotent, and safe across instances: Directory applies only a higher version. A Directory outage only leaves rows unpushed;
/// they go again on the next cycle. A practitioner Directory does not know (404) is left alone for <see cref="UnknownPause"/> unless its summary changes.
/// </summary>
public sealed class SummaryPushJob(IServiceScopeFactory scopes, IDirectoryReputationClient client, TimeProvider time, ILogger<SummaryPushJob> log)
{
    /// <summary>Rows per cycle; the rest wait for the next one.</summary>
    public const int BatchSize = 500;
    public static readonly TimeSpan UnknownPause = TimeSpan.FromMinutes(10);
    readonly Dictionary<Guid, (long Version, DateTimeOffset RetryAt)> _unknown = new();
    readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Returns the number of pushes Directory accepted. Only cancellation escapes.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _gate.WaitAsync(ct);
        try
        {
            List<RatingSummary> due;
            try
            {
                using var scope = scopes.CreateScope();
                due = await scope.ServiceProvider.GetRequiredService<ReputationDbContext>().RatingSummaries.AsNoTracking()
                    .Where(s => s.Version > s.PushedVersion).OrderBy(s => s.PractitionerId).Take(BatchSize).ToListAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError("SummaryPush could not read the summaries: {ExceptionType}", ex.GetType().Name);
                return 0;
            }

            var pushed = 0;
            foreach (var s in due)
            {
                ct.ThrowIfCancellationRequested();
                var now = time.GetUtcNow();
                if (_unknown.TryGetValue(s.PractitionerId, out var u) && u.Version == s.Version && now < u.RetryAt) continue;
                try
                {
                    var avg = s.Count == 0 ? 0m : Math.Round((decimal)s.Sum / s.Count, 2);
                    switch (await client.PushAsync(s.PractitionerId, new ReputationPush(s.Count, avg, s.Version), ct))
                    {
                        case PushResult.Accepted:
                            _unknown.Remove(s.PractitionerId);
                            await MarkPushedAsync(s.PractitionerId, s.Version, ct);
                            pushed++;
                            break;
                        case PushResult.NotFound:
                            _unknown[s.PractitionerId] = (s.Version, now + UnknownPause);
                            log.LogWarning("SummaryPush: Directory does not know practitioner {PractitionerId}; paused", s.PractitionerId);
                            break;
                        default:
                            log.LogWarning("SummaryPush gave up on practitioner {PractitionerId} until the next cycle", s.PractitionerId);
                            break;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogError("SummaryPush failed for practitioner {PractitionerId}: {ExceptionType}", s.PractitionerId, ex.GetType().Name);
                }
            }
            if (pushed > 0) log.LogInformation("SummaryPush pushed {Count} of {Total} summaries", pushed, due.Count);
            return pushed;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Touches only <c>pushed_version</c>, and only upwards, so a concurrent change of count, sum or version is never overwritten.</summary>
    async Task MarkPushedAsync(Guid practitionerId, long version, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReputationDbContext>();
        var row = await db.RatingSummaries.SingleOrDefaultAsync(x => x.PractitionerId == practitionerId, ct);
        if (row is null || row.PushedVersion >= version) return;
        row.PushedVersion = version;
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Tells the push loop that a rating summary changed, so it pushes now instead of at the next 60 s tick. Without it the lag from a review to the Directory
/// is anywhere from 0 to 60 s plus the retries, decided by where the review falls on the timer's grid; a review committed just after a tick waits a whole
/// interval. Signals coalesce (one pending is enough: a cycle pushes every due summary) and a missed one costs nothing, the timer is still the safety net.
/// </summary>
public sealed class SummaryPushSignal
{
    readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    /// <summary>Call after the change is committed (a cycle started earlier would not see it).</summary>
    public void Notify() => _channel.Writer.TryWrite(true);

    internal async Task WaitAsync(CancellationToken ct) => await _channel.Reader.ReadAsync(ct);
}

/// <summary>Runs the job every 60 s (CONTRACTS 5.3), and at once when <see cref="SummaryPushSignal"/> fires. The first timed cycle runs one interval after start.</summary>
public sealed class SummaryPushHostedService(SummaryPushJob job, SummaryPushSignal signal, TimeProvider time, ILogger<SummaryPushHostedService> log) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        try
        {
            var tick = timer.WaitForNextTickAsync(stoppingToken).AsTask(); // one wait at a time on the timer; it is renewed only after it completes
            var nudge = signal.WaitAsync(stoppingToken);
            while (true)
            {
                var done = await Task.WhenAny(tick, nudge);
                if (done == tick)
                {
                    if (!await tick) break;
                    tick = timer.WaitForNextTickAsync(stoppingToken).AsTask();
                }
                else
                {
                    await nudge;
                    nudge = signal.WaitAsync(stoppingToken);
                }
                try { await job.RunOnceAsync(stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError("SummaryPush cycle failed: {ExceptionType}", ex.GetType().Name); }
            }
        }
        catch (OperationCanceledException) { }
    }
}

public sealed class SummaryPushFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.AddDirectoryReputationClient();
        builder.Services.AddSingleton<SummaryPushSignal>();
        builder.Services.AddSingleton<SummaryPushJob>();
        builder.Services.AddHostedService<SummaryPushHostedService>();
    }

    public void Map(IEndpointRouteBuilder endpoints) { }
}
