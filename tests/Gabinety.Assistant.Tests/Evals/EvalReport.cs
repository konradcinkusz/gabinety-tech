using System.Text.Json;

namespace Gabinety.Assistant.Tests.Evals;

/// <summary>
/// A report (evals/reports/, ignored by git): pass rate per assertion, latency p95, tokens and credits, failures as scenario ids.
/// No scenario text and no reply text is ever written (T-06.05 records only ids).
/// </summary>
public static class EvalReport
{
    public static object Summarize(EvalRun run, EvalMode mode, DateTimeOffset at)
    {
        var rates = run.Results.SelectMany(r => r.Assertions).GroupBy(a => a.Name).OrderBy(g => g.Key).ToDictionary(
            g => g.Key, g => new { passed = g.Count(a => a.Passed), total = g.Count(), rate = Math.Round(g.Count(a => a.Passed) / (double)g.Count(), 4), gate = g.Any(a => a.Gate) });
        var latencies = run.Results.Select(r => r.LatencyMs).Order().ToList();
        var p95 = latencies.Count == 0 ? 0 : latencies[Math.Max(0, (int)Math.Ceiling(0.95 * latencies.Count) - 1)];
        return new
        {
            prompt = run.Prompt, model = run.Model, mode = mode.ToString().ToLowerInvariant(), at = at.ToString("O"),
            scenarios = run.Results.Count, outcomes = run.Results.GroupBy(r => r.Outcome).ToDictionary(g => g.Key, g => g.Count()),
            assertions = rates, latencyP95Ms = p95,
            inputTokens = run.Results.Sum(r => r.InputTokens ?? 0), outputTokens = run.Results.Sum(r => r.OutputTokens ?? 0),
            aiCredits = run.Results.All(r => r.AiCredits is null) ? (decimal?)null : run.Results.Sum(r => r.AiCredits ?? 0m),
            failures = run.Results.SelectMany(r => r.Assertions.Where(a => !a.Passed).Select(a => new { scenario = r.Id, assertion = a.Name, gate = a.Gate })).ToList(),
        };
    }

    /// <summary>Writes <c>{dir}/{prompt}-{UTC time}.json</c> and returns its path.</summary>
    public static string Write(string dir, EvalRun run, EvalMode mode, DateTimeOffset at)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{run.Prompt.Replace('/', '-')}-{at:yyyyMMddTHHmmssfffZ}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(Summarize(run, mode, at), new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }
}
