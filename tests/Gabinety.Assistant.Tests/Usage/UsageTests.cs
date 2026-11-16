using System.Text.RegularExpressions;
using Gabinety.Assistant.Persistence;
using Gabinety.Assistant.Policy;
using Gabinety.Assistant.Usage;
using Gabinety.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gabinety.Assistant.Tests.Usage;

public class UsageTests
{
    const string Salt = "demo-salt-not-a-secret";
    static readonly DateTimeOffset Noon = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    sealed class Rig
    {
        public FakeClock Clock = new(Noon);
        public AssistantDbContext Db = new(new DbContextOptionsBuilder<AssistantDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public UsageRecorder Recorder;
        public BudgetGuard Guard;
        public Rig(string? budget = null)
        {
            var o = Options.Create(new AssistantOptions { UserHashSalt = Salt, DailyBudgetUsd = budget is null ? 5m : decimal.Parse(budget) });
            Recorder = new UsageRecorder(Db, new UserHasher(o), Clock);
            Guard = new BudgetGuard(Db, o, Clock);
        }
        public Task Add(decimal? usd, string user = "demo-user-1", string model = "demo-custom") =>
            Recorder.RecordAsync(AssistantFeature.Brief, model, ProviderKind.Custom, 100, 50, null, usd, 120, UsageOutcome.Ok, user);
    }

    [Fact]
    public async Task One_row_per_call_with_the_contract_values()
    {
        var r = new Rig();
        await r.Recorder.RecordAsync(AssistantFeature.ReviewTriage, "demo-custom", ProviderKind.Custom, 1000, 2000, 1.5m, 0.25m, 321, UsageOutcome.InvalidOutput, "demo-user-1");
        await r.Add(0.1m);
        var rows = await r.Db.Usage.OrderBy(x => x.LatencyMs).ToListAsync();
        Assert.Equal(2, rows.Count);
        var row = rows.Single(x => x.LatencyMs == 321);
        Assert.Equal(("review-triage", "demo-custom", "custom", "invalid_output"), (row.Feature, row.ModelId, row.ProviderKind, row.Outcome));
        Assert.Equal((1000, 2000, 1.5m, 0.25m, Noon), (row.InputTokens, row.OutputTokens, row.AiCredits, row.UsdEstimate, row.At));
    }

    [Fact]
    public void A_usage_row_has_no_field_that_can_hold_text()
    {
        using var db = new Rig().Db;
        var entity = db.Model.FindEntityType(typeof(UsageRow))!;
        var strings = entity.GetProperties().Where(p => p.ClrType == typeof(string)).Select(p => p.GetColumnName()).Order().ToArray();
        Assert.Equal(["feature", "model_id", "outcome", "provider_kind", "user_hash"], strings);
        var clr = typeof(UsageRow).GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => p.Name).Order().ToArray();
        Assert.Equal(["Feature", "ModelId", "Outcome", "ProviderKind", "UserHash"], clr);
        Assert.DoesNotContain(typeof(UsageRow).GetProperties(), p => p.PropertyType == typeof(byte[]) || p.PropertyType == typeof(object));
        Assert.Equal(80, entity.FindProperty(nameof(UsageRow.ModelId))!.GetMaxLength());
        Assert.Equal(16, entity.FindProperty(nameof(UsageRow.UserHash))!.GetMaxLength());
    }

    [Fact]
    public async Task User_hash_is_16_hex_characters_differs_between_users_and_never_contains_the_id()
    {
        var r = new Rig();
        await r.Add(null, "demo-user-1");
        await r.Add(null, "demo-user-2");
        await r.Recorder.RecordAsync(AssistantFeature.Brief, "demo-custom", ProviderKind.Custom, null, null, null, null, 1, UsageOutcome.Unavailable, null);
        var hashes = await r.Db.Usage.Select(x => x.UserHash).ToListAsync();
        Assert.Single(hashes, h => h is null);
        var set = hashes.Where(h => h is not null).Select(h => h!).ToList();
        Assert.Equal(2, set.Distinct().Count());
        Assert.All(set, h => { Assert.Matches("^[0-9a-f]{16}$", h); Assert.DoesNotContain("demo-user", h); });
        var expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Salt + ":demo-user-1")))[..16];
        Assert.Contains(expected, set);
    }

    [Fact]
    public void Without_a_configured_salt_a_random_per_process_salt_is_used_and_still_yields_16_hex()
    {
        var a = new UserHasher(Options.Create(new AssistantOptions()));
        var b = new UserHasher(Options.Create(new AssistantOptions()));
        Assert.Matches("^[0-9a-f]{16}$", a.Hash("demo-user-1"));
        Assert.Equal(a.Hash("demo-user-1"), a.Hash("demo-user-1"));
        Assert.NotEqual(a.Hash("demo-user-1"), b.Hash("demo-user-1"));
    }

    [Fact]
    public async Task Reaching_the_daily_budget_gives_OverBudget_and_exactly_the_limit_blocks()
    {
        var r = new Rig("0.01");
        Assert.Equal(BudgetDecision.Allowed, await r.Guard.CheckAsync());
        await r.Add(0.004m);
        Assert.Equal(BudgetDecision.Allowed, await r.Guard.CheckAsync());
        await r.Add(0.006m); // 0.010 == limit
        Assert.Equal(BudgetDecision.OverBudget, await r.Guard.CheckAsync());
    }

    [Fact]
    public async Task The_default_budget_is_five_dollars()
    {
        var r = new Rig();
        await r.Add(4.99m);
        Assert.Equal(BudgetDecision.Allowed, await r.Guard.CheckAsync());
        await r.Add(0.01m);
        Assert.Equal(BudgetDecision.OverBudget, await r.Guard.CheckAsync());
    }

    [Fact]
    public async Task A_row_of_the_previous_UTC_day_does_not_count_and_midnight_starts_a_new_day()
    {
        var r = new Rig("0.01");
        r.Clock.Now = new DateTimeOffset(2026, 11, 15, 23, 59, 59, TimeSpan.Zero);
        await r.Add(9m);
        r.Clock.Now = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(BudgetDecision.Allowed, await r.Guard.CheckAsync());
        await r.Add(0.01m);
        Assert.Equal(BudgetDecision.OverBudget, await r.Guard.CheckAsync());
        r.Clock.Now = new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(BudgetDecision.Allowed, await r.Guard.CheckAsync());
    }

    [Fact]
    public async Task A_model_with_no_price_adds_nothing_to_the_sum_but_its_row_exists()
    {
        var r = new Rig("0.01");
        await r.Add(null, model: "demo-unpriced");
        Assert.Equal(BudgetDecision.Allowed, await r.Guard.CheckAsync());
        var row = await r.Db.Usage.SingleAsync();
        Assert.Null(row.UsdEstimate);
    }

    [Fact]
    public void Custom_model_usd_follows_the_contract_formula()
    {
        var prices = new LlmTokenPrices(1_000_000, 3.0, 15.0, null);
        Assert.Equal(0.0105m, UsdEstimator.Estimate(ProviderKind.Custom, prices, 1000, 500)); // 1000/1e6*3 + 500/1e6*15
        Assert.Equal(0m, UsdEstimator.Estimate(ProviderKind.Custom, prices, 0, 0));
        Assert.Null(UsdEstimator.Estimate(ProviderKind.Custom, null, 1000, 500));      // no price: unknown, never zero
        Assert.Null(UsdEstimator.Estimate(ProviderKind.Custom, prices, null, 500));    // tokens unknown
        Assert.Null(UsdEstimator.Estimate(ProviderKind.Copilot, new LlmTokenPrices(1, 1, 1, null), 10, 10));
        Assert.Equal(0.00m, UsdEstimator.Estimate(ProviderKind.Fake, new LlmTokenPrices(1_000_000, 1, 4, null), 10, 10));
    }

    [Fact]
    public void Provider_kind_is_fake_for_the_fake_gateways_own_models_custom_for_custom_and_copilot_otherwise()
    {
        Assert.Equal(ProviderKind.Fake, UsdEstimator.KindOf(LlmGatewayState.Fake, new LlmModelInfo("fake-a", "Fake A", true)));
        Assert.Equal(ProviderKind.Custom, UsdEstimator.KindOf(LlmGatewayState.Fake, new LlmModelInfo("c", "c", true, Custom: true)));
        Assert.Equal(ProviderKind.Custom, UsdEstimator.KindOf(LlmGatewayState.Ready, new LlmModelInfo("c", "c", true, Custom: true)));
        Assert.Equal(ProviderKind.Copilot, UsdEstimator.KindOf(LlmGatewayState.Ready, new LlmModelInfo("m", "m", true)));
    }
}
