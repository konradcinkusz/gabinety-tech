using System.Security.Cryptography;
using System.Text;
using Gabinety.Assistant.Persistence;
using Gabinety.Assistant.Policy;
using Gabinety.Llm;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gabinety.Assistant.Usage;

public enum AssistantFeature { Brief, ReviewTriage }
public enum ProviderKind { Copilot, Custom, Fake }
public enum UsageOutcome { Ok, InvalidOutput, Timeout, Unavailable, OverBudget, PolicyRefused }
public enum BudgetDecision { Allowed, OverBudget }

/// <summary>SHA-256 of salt + ":" + userId, hex, first 16 characters. The id itself is never stored.</summary>
public sealed class UserHasher(IOptions<AssistantOptions> options)
{
    // Unset salt: random per process, so hashes differ across restarts (acceptable for a usage log).
    readonly string _salt = string.IsNullOrEmpty(options.Value.UserHashSalt)
        ? Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))
        : options.Value.UserHashSalt;

    public string Hash(string userId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(_salt + ":" + userId)))[..16];
}

/// <summary>Cost estimate (COPILOT-SDK 8). Cost, tokens and latency are never an input to ranking, ratings, verification or fees.</summary>
public static class UsdEstimator
{
    public static ProviderKind KindOf(LlmGatewayState gateway, LlmModelInfo model) =>
        model.Custom ? ProviderKind.Custom : gateway == LlmGatewayState.Fake ? ProviderKind.Fake : ProviderKind.Copilot;

    /// <summary>Custom: input/1e6*priceIn + output/1e6*priceOut (prices are USD per million tokens). Fake: 0. Anything else, or any unknown input: null (unknown, never zero).</summary>
    public static decimal? Estimate(ProviderKind kind, LlmTokenPrices? prices, int? inputTokens, int? outputTokens) => kind switch
    {
        ProviderKind.Fake => 0.00m,
        ProviderKind.Custom when prices is { InputCredits: { } pin, OutputCredits: { } pout } && inputTokens is { } i && outputTokens is { } o =>
            i / 1_000_000m * (decimal)pin + o / 1_000_000m * (decimal)pout,
        _ => null,
    };
}

/// <summary>Writes ONE assistant_usage row per call. Takes plain values: no prompt, no reply, no user id.</summary>
public sealed class UsageRecorder(AssistantDbContext db, UserHasher hasher, TimeProvider time)
{
    public async Task RecordAsync(AssistantFeature feature, string modelId, ProviderKind kind, int? inputTokens, int? outputTokens,
        decimal? aiCredits, decimal? usdEstimate, int latencyMs, UsageOutcome outcome, string? userId, CancellationToken ct = default)
    {
        db.Usage.Add(new UsageRow
        {
            Id = Guid.NewGuid(),
            At = time.GetUtcNow(),
            Feature = feature switch { AssistantFeature.Brief => "brief", _ => "review-triage" },
            ModelId = modelId.Length > 80 ? modelId[..80] : modelId,
            ProviderKind = kind.ToString().ToLowerInvariant(),
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            AiCredits = aiCredits,
            UsdEstimate = usdEstimate,
            LatencyMs = latencyMs,
            Outcome = outcome switch
            {
                UsageOutcome.Ok => "ok", UsageOutcome.InvalidOutput => "invalid_output", UsageOutcome.Timeout => "timeout",
                UsageOutcome.Unavailable => "unavailable", UsageOutcome.OverBudget => "over_budget", _ => "policy_refused",
            },
            UserHash = string.IsNullOrEmpty(userId) ? null : hasher.Hash(userId),
        });
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Refuses new work when the UTC day's sum of usd_estimate reaches Assistant:DailyBudgetUsd (>=). Unpriced rows (null) add nothing.</summary>
public sealed class BudgetGuard(AssistantDbContext db, IOptions<AssistantOptions> options, TimeProvider time)
{
    public async Task<BudgetDecision> CheckAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var start = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var spent = await db.Usage.Where(u => u.At >= start && u.At < start.AddDays(1)).SumAsync(u => u.UsdEstimate ?? 0m, ct);
        return spent >= options.Value.DailyBudgetUsd ? BudgetDecision.OverBudget : BudgetDecision.Allowed;
    }
}

public sealed class UsageFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<UserHasher>(); // one salt per process
        builder.Services.AddScoped<UsageRecorder>();
        builder.Services.AddScoped<BudgetGuard>();
    }

    public void Map(IEndpointRouteBuilder endpoints) { }
}
