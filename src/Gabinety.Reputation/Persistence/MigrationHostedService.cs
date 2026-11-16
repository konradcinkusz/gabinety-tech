using Microsoft.EntityFrameworkCore;

namespace Gabinety.Reputation.Persistence;

public enum MigrationStatus { Pending, Migrating, Done, Failed }

/// <summary>Where schema work stands; read by the health check and by tests.</summary>
public sealed class MigrationState
{
    readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    volatile int _status;
    public MigrationStatus Status => (MigrationStatus)_status;

    public void Set(MigrationStatus s)
    {
        _status = (int)s;
        if (s is MigrationStatus.Done or MigrationStatus.Failed) _finished.TrySetResult();
    }

    /// <summary>Waits until the state is Done or Failed; throws TimeoutException after <paramref name="limit"/>.</summary>
    public Task WaitForCompletionAsync(TimeSpan limit) => _finished.Task.WaitAsync(limit);
}

/// <summary>Runs MigrateAsync after Kestrel has started, so probes answer while schema work is in flight (P4). Never throws out of the host.</summary>
public sealed class MigrationHostedService(IServiceProvider services, MigrationState state, IHostApplicationLifetime lifetime,
    TimeProvider time, ILogger<MigrationHostedService> log) : BackgroundService
{
    const int Attempts = 3;
    static readonly TimeSpan Backoff = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var _ = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
            await started.Task.WaitAsync(stoppingToken);

            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReputationDbContext>();
            if (!db.Database.IsRelational()) { state.Set(MigrationStatus.Done); return; } // InMemory: nothing to migrate

            state.Set(MigrationStatus.Migrating);
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await db.Database.MigrateAsync(stoppingToken);
                    state.Set(MigrationStatus.Done);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning(ex, "Reputation migration attempt {Attempt} of {Attempts} failed", attempt, Attempts);
                    if (attempt == Attempts) { state.Set(MigrationStatus.Failed); return; }
                    await Task.Delay(Backoff, time, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            log.LogError(ex, "Reputation migration service failed");
            state.Set(MigrationStatus.Failed);
        }
    }
}
