using Gabinety.Assistant.Clients;
using Gabinety.Assistant.Features.Brief;
using Gabinety.Assistant.Features.Triage;
using Gabinety.Assistant.Persistence;
using Gabinety.Assistant.Policy;
using Gabinety.Assistant.Tests.Brief;
using Gabinety.Assistant.Usage;
using Gabinety.Contracts.Assistant;
using Gabinety.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Gabinety.Assistant.Tests.Evals;

/// <summary>One deterministic assertion. <c>Gate</c> is false for an intent that is only reported (not reproducible by this gateway).</summary>
public sealed record EvalAssertion(string Name, bool Passed, bool Gate = true);

public sealed record ScenarioResult(string Id, string Outcome, int LatencyMs, int? InputTokens, int? OutputTokens, decimal? AiCredits,
    IReadOnlyList<EvalAssertion> Assertions)
{
    public bool GatesPassed => Assertions.All(a => a.Passed || !a.Gate);
}

public sealed record EvalRun(string Prompt, string Model, IReadOnlyList<ScenarioResult> Results)
{
    public IEnumerable<(string Id, string Assertion)> Failures =>
        Results.SelectMany(r => r.Assertions.Where(a => !a.Passed && a.Gate).Select(a => (r.Id, a.Name)));
}

/// <summary>An owner-run policy for the evaluation: any enabled model, the preferred one first. Never registered in a web host.</summary>
sealed class EvalPolicy(ILlmGateway gateway, IOptions<AssistantOptions> options) : ITrafficPolicy
{
    public bool IsAllowed(LlmGatewayState gateway, LlmModelInfo model) => true;
    public async Task<LlmModelInfo?> SelectModelAsync(LlmGatewayState gatewayState, CancellationToken ct)
    {
        var models = (await gateway.ListModelsAsync(ct)).Where(m => m.Enabled).ToList();
        return models.FirstOrDefault(m => options.Value.Model is not null && m.Id == options.Value.Model) ?? models.FirstOrDefault();
    }
}

/// <summary>
/// Runs scenarios through the REAL Assistant services (prompt, validator, repair attempt) against any <see cref="ILlmGateway"/>, then applies
/// deterministic assertions. Usage goes to a throw-away in-memory database. Nothing from a scenario or a reply is kept in a result.
/// </summary>
public sealed class EvalRunner : IAsyncDisposable
{
    readonly ServiceProvider _provider;
    readonly ILlmGateway _gateway;
    readonly bool _gateIntent;

    /// <param name="gateIntent">True: a <c>deterministic</c> intent is a gate (the fake gateway, CI). False: every intent is only reported.</param>
    public EvalRunner(ILlmGateway gateway, bool gateIntent, string? preferredModel = null)
    {
        _gateway = gateway;
        _gateIntent = gateIntent;
        var s = new ServiceCollection();
        s.AddSingleton(gateway);
        s.AddSingleton(TimeProvider.System);
        s.AddDbContext<AssistantDbContext>(o => o.UseInMemoryDatabase("eval-" + Guid.NewGuid()));
        s.AddSingleton<ITaxonomyProvider, StubTaxonomy>();
        s.AddSingleton<ITrafficPolicy, EvalPolicy>();
        s.AddSingleton<UserHasher>();
        s.AddScoped<UsageRecorder>();
        s.AddScoped<BudgetGuard>();
        s.AddScoped<BriefService>();
        s.AddScoped<TriageService>();
        s.AddOptions<AssistantOptions>().Configure(o => { o.DailyBudgetUsd = decimal.MaxValue; o.Model = preferredModel; });
        s.AddOptions<BriefOptions>();
        s.AddOptions<TriageOptions>();
        _provider = s.BuildServiceProvider();
    }

    public async Task<EvalRun> RunBriefAsync(IEnumerable<EvalScenario> scenarios, CancellationToken ct = default)
    {
        var results = new List<ScenarioResult>();
        foreach (var sc in scenarios)
        {
            await using var scope = _provider.CreateAsyncScope();
            var r = await scope.ServiceProvider.GetRequiredService<BriefService>().SuggestAsync("demo-eval", sc.Body, ct);
            var usage = await TakeUsage(scope, ct);
            var tax = StubTaxonomy.Full;
            var a = new List<EvalAssertion>();
            var s = (r as BriefResult.Suggestion)?.Value;
            a.Add(new("schema_valid", s is not null));
            if (s is not null)
            {
                a.Add(new("slugs_in_taxonomy", tax.Problems.Any(c => c.Slug == s.ProblemSlug) && s.TechSlugs.All(t => tax.Techs.Any(c => c.Slug == t))));
                a.Add(new("length_limits", s.Summary.Length is > 0 and <= 300 && s.ClarifyingQuestions.Count <= 3 && s.ClarifyingQuestions.All(q => q.Length is > 0 and <= 140)
                    && s.TechSlugs.Count <= 4 && s.SuggestedDurationMinutes is 30 or 60));
                var all = string.Join("\n", [s.ProblemSlug, .. s.TechSlugs, s.Summary, .. s.ClarifyingQuestions]);
                a.Add(new("no_echo", !(sc.Forbidden ?? []).Any(f => all.Contains(f, StringComparison.OrdinalIgnoreCase))));
                if (sc.Expect?.ProblemSlug is { } want) a.Add(new("intent", s.ProblemSlug == want, _gateIntent && sc.Deterministic));
            }
            results.Add(new(sc.Id, usage.Outcome, usage.LatencyMs, usage.InputTokens, usage.OutputTokens, usage.AiCredits, a));
        }
        return new("brief/v1", await ModelId(ct), results);
    }

    public async Task<EvalRun> RunTriageAsync(IEnumerable<EvalScenario> scenarios, CancellationToken ct = default)
    {
        var results = new List<ScenarioResult>();
        foreach (var sc in scenarios)
        {
            await using var scope = _provider.CreateAsyncScope();
            var r = await scope.ServiceProvider.GetRequiredService<TriageService>().TriageAsync(sc.Rating, sc.Body, ct);
            var usage = await TakeUsage(scope, ct);
            var a = new List<EvalAssertion>();
            var v = (r as TriageResult.Verdict)?.Value;
            a.Add(new("schema_valid", v is not null));
            if (v is not null)
            {
                a.Add(new("reasons_in_closed_set", v.Reasons.All(Enum.IsDefined) && v.Reasons.Distinct().Count() == v.Reasons.Count && v.Reasons.Count <= 5));
                a.Add(new("flag_consistent", v.Flag == TriageVerdict.None ? v.Reasons.Count == 0 : v.Reasons.Count >= 1));
                if (sc.Expect?.Flag is { } flag)
                {
                    var wantReasons = (sc.Expect.Reasons ?? []).Select(Reason).ToList();
                    var ok = string.Equals(v.Flag.ToString(), flag, StringComparison.OrdinalIgnoreCase) && wantReasons.All(v.Reasons.Contains);
                    a.Add(new("intent", ok, _gateIntent && sc.Deterministic));
                }
            }
            results.Add(new(sc.Id, usage.Outcome, usage.LatencyMs, usage.InputTokens, usage.OutputTokens, usage.AiCredits, a));
        }
        return new("triage/v1", await ModelId(ct), results);
    }

    static TriageReason Reason(string wire) => TriageValidator.Reasons[wire];

    static async Task<UsageRow> TakeUsage(AsyncServiceScope scope, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<AssistantDbContext>();
        var row = await db.Usage.AsNoTracking().OrderByDescending(u => u.At).FirstAsync(ct);
        db.Usage.RemoveRange(db.Usage);
        await db.SaveChangesAsync(ct);
        return row;
    }

    async Task<string> ModelId(CancellationToken ct) =>
        (await new EvalPolicy(_gateway, _provider.GetRequiredService<IOptions<AssistantOptions>>()).SelectModelAsync(_gateway.Status.State, ct))?.Id ?? "none";

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
