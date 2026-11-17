using Gabinety.Contracts.Assistant;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Clients;
using Gabinety.Reputation.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Reputation.Jobs;

/// <summary>
/// Asks the Assistant for a triage SUGGESTION on held reviews and stores it for the moderator (CONTRACTS 5.1 <c>triage_flag</c>, <c>triage_model_id</c>).
/// It never changes <c>status</c>, <c>published_at</c> or the rating summary: the deterministic rules and the moderator decide (CONTRACTS 5.3, SPEC 5 rule 6).
/// <list type="bullet">
/// <item>Pending = <c>Held</c> with no <c>triage_model_id</c>. A model id means a real answer was stored, so a review is never asked twice after success.</item>
/// <item>No migration: an attempt is a <c>moderation_events</c> row with actor <c>triage</c> (CONTRACTS 5.1 names that actor). Its count bounds the attempts and its time drives the backoff.</item>
/// <item>An outage stores <c>triage_flag = unavailable</c> and one more attempt row; up to <see cref="MaxAttempts"/> attempts, waiting <see cref="Backoffs"/> after the 1st and 2nd.</item>
/// <item>A <c>hold</c> answer adds its reasons (codes of the closed CONTRACTS list) to <c>hold_reasons</c>, after the rule reasons.</item>
/// <item>The review text goes only into the call to the Assistant. Logs carry counts, ids and fixed cause codes.</item>
/// </list>
/// </summary>
public sealed class TriageJob(IServiceScopeFactory scopes, IAssistantClient assistant, TimeProvider time, ILogger<TriageJob> log)
{
    public const int BatchSize = 20, MaxAttempts = 3;
    public const string Actor = "triage";
    /// <summary>Wait after the 1st and after the 2nd failed attempt. The task block gives no numbers: 3 attempts with 2 min then 10 min (a choice, stated in the PR).</summary>
    public static readonly TimeSpan[] Backoffs = [TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)];

    sealed record Pending(Guid Id, int Rating, string Text);

    /// <summary>One pass over at most <see cref="BatchSize"/> reviews, oldest first. Returns how many were asked. Never throws except on cancellation.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReputationDbContext>();
        var now = time.GetUtcNow();
        var wait1 = now - Backoffs[0];
        var wait2 = now - Backoffs[1];
        var attempts = db.ModerationEvents.Where(e => e.Actor == Actor);

        var batch = await db.Reviews.AsNoTracking()
            .Where(r => r.Status == ReviewStatus.Held && r.TriageModelId == null && (
                !attempts.Any(e => e.ReviewId == r.Id)
                || (attempts.Count(e => e.ReviewId == r.Id) == 1 && attempts.Where(e => e.ReviewId == r.Id).Max(e => e.At) <= wait1)
                || (attempts.Count(e => e.ReviewId == r.Id) == 2 && attempts.Where(e => e.ReviewId == r.Id).Max(e => e.At) <= wait2)))
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Select(r => new Pending(r.Id, r.Rating, r.Text))
            .Take(BatchSize)
            .ToListAsync(ct);

        int stored = 0, unavailable = 0;
        foreach (var item in batch)
        {
            ct.ThrowIfCancellationRequested();
            var result = await AskAsync(item, ct);
            if (!await StoreAsync(db, item.Id, result, ct)) continue;
            if (result is AssistantTriageResult.Verdict) stored++; else unavailable++;
        }
        if (batch.Count > 0) log.LogInformation("Triage run: {Asked} asked, {Stored} stored, {Unavailable} unavailable", batch.Count, stored, unavailable);
        return batch.Count;
    }

    async Task<AssistantTriageResult> AskAsync(Pending item, CancellationToken ct)
    {
        try { return await assistant.TriageAsync(item.Rating, item.Text, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Only the type is logged: a message may carry the URL, the key or the text.
            log.LogWarning("Triage call for review {ReviewId} failed with {ExceptionType}", item.Id, ex.GetType().Name);
            return new AssistantUnavailable("transport");
        }
    }

    async Task<bool> StoreAsync(ReputationDbContext db, Guid id, AssistantTriageResult result, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var review = await db.Reviews.SingleOrDefaultAsync(r => r.Id == id, ct);
        // Moderated or triaged while the call was in flight: the suggestion is stale, drop it.
        if (review is null || review.Status != ReviewStatus.Held || review.TriageModelId is not null) return false;

        string note;
        switch (result)
        {
            case AssistantTriageResult.Verdict v:
                review.TriageFlag = v.Flag == TriageVerdict.Hold ? TriageFlag.Hold : TriageFlag.None;
                review.TriageModelId = v.ModelId;
                if (v.Flag == TriageVerdict.Hold)
                    review.HoldReasons = [.. review.HoldReasons, .. v.Reasons.Select(TriageWire.Code).Where(c => !review.HoldReasons.Contains(c))];
                note = review.TriageFlag == TriageFlag.Hold ? "triage:hold" : "triage:none";
                break;
            default:
                review.TriageFlag = TriageFlag.Unavailable;
                note = "triage:unavailable";
                break;
        }
        db.ModerationEvents.Add(new ModerationEvent { Id = Guid.CreateVersion7(), ReviewId = id, Kind = ModerationEventKind.Held, Actor = Actor, Note = note, At = time.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>Runs <see cref="TriageJob"/> every <c>Reviews:TriageIntervalSeconds</c> (default 60). Idle without <c>Backend:Assistant</c>, and while the schema is not migrated.</summary>
public sealed class TriageHostedService(TriageJob job, IConfiguration config, MigrationState migrations, TimeProvider time, ILogger<TriageHostedService> log) : BackgroundService
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = double.TryParse(config["Reviews:TriageIntervalSeconds"], System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 0 ? s : DefaultInterval.TotalSeconds;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds), time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (string.IsNullOrWhiteSpace(config["Backend:Assistant"]) || migrations.Status != MigrationStatus.Done) continue;
                try { await job.RunOnceAsync(stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning("Triage run failed with {ExceptionType}", ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }
}

public sealed class TriageJobFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<TriageJob>();
        builder.Services.AddHostedService<TriageHostedService>();
    }

    public void Map(IEndpointRouteBuilder endpoints) { }
}
