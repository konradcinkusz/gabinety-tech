using System.Collections.Concurrent;
using Gabinety.ServiceDefaults;

namespace Gabinety.Booking.Jobs.StatsPush;

/// <summary>
/// One cycle of the stats push (CONTRACTS 3.6, 4.2, 8.1): computes the stats of every practitioner and pushes those whose four values differ from the last
/// successful push of this process. The version is unix milliseconds of <see cref="TimeProvider"/>, bumped to stay strictly increasing, so it needs no table and
/// survives a restart (after one, each practitioner is pushed once more; Directory ignores equal data and takes the higher version). Idempotent.
/// </summary>
public sealed class StatsPushJob(IServiceScopeFactory scopes, IDirectoryStatsClient client, TimeProvider time, ILogger<StatsPushJob> log)
{
    readonly ConcurrentDictionary<Guid, BookingStats> _lastPushed = new();
    readonly SemaphoreSlim _gate = new(1, 1);
    long _lastVersion;

    /// <summary>Returns the number of pushes Directory accepted. Only cancellation escapes.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _gate.WaitAsync(ct);
        try
        {
            Dictionary<Guid, BookingStats> all;
            try
            {
                using var scope = scopes.CreateScope();
                all = await scope.ServiceProvider.GetRequiredService<StatsCalculator>().ComputeAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError("StatsPush could not compute the stats: {ExceptionType}", ex.GetType().Name);
                return 0;
            }

            var pushed = 0;
            foreach (var (id, stats) in all)
            {
                ct.ThrowIfCancellationRequested();
                if (_lastPushed.TryGetValue(id, out var last) && last == stats) continue;
                var version = _lastVersion = Math.Max(time.GetUtcNow().ToUnixTimeMilliseconds(), _lastVersion + 1);
                try
                {
                    if (!await client.PushAsync(id, new BookingStatsPush(stats.CompletedCount, stats.PractitionerCancelCount, stats.NoShowUpheldCount, stats.OpenSlots14d, version), ct))
                    {
                        log.LogWarning("StatsPush gave up on practitioner {PractitionerId} until the next cycle", id);
                        continue;
                    }
                    _lastPushed[id] = stats;
                    pushed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogError("StatsPush failed for practitioner {PractitionerId}: {ExceptionType}", id, ex.GetType().Name);
                }
            }
            if (pushed > 0) log.LogInformation("StatsPush pushed {Count} of {Total} practitioners", pushed, all.Count);
            return pushed;
        }
        finally { _gate.Release(); }
    }
}

/// <summary>Runs the job every 60 s (CONTRACTS 8.1). The first cycle runs one interval after start. Self-contained: a shared job base may replace it later.</summary>
public sealed class StatsPushHostedService(StatsPushJob job, TimeProvider time, ILogger<StatsPushHostedService> log) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await job.RunOnceAsync(stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError("StatsPush cycle failed: {ExceptionType}", ex.GetType().Name); }
            }
        }
        catch (OperationCanceledException) { }
    }
}

public sealed class StatsPushFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.AddScoped<StatsCalculator>();
        builder.Services.AddDirectoryStatsClient();
        builder.Services.AddSingleton<StatsPushJob>();
        builder.Services.AddHostedService<StatsPushHostedService>();
    }

    public void Map(IEndpointRouteBuilder endpoints) { }
}
