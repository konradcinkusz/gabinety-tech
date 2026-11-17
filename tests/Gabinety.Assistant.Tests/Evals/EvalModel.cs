using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gabinety.Assistant.Tests.Evals;

/// <summary>What the scenario author intends. Brief: <c>problemSlug</c>. Triage: <c>flag</c> and <c>reasons</c> (all of them must be present).</summary>
public sealed record EvalExpect(string? ProblemSlug = null, string? Flag = null, string[]? Reasons = null);

/// <summary>
/// One synthetic scenario (data file). <c>Forbidden</c> strings must never appear in a brief suggestion (an injected instruction, a
/// leaked prompt, personal data from the text). <c>Deterministic</c> marks an intent that the fake gateway reproduces (COPILOT-SDK 10):
/// CI gates on it; against a live model every intent is only reported, never a silent gate.
/// </summary>
public sealed record EvalScenario(string Id, string[] Tags, string Text, int? Repeat = null, int Rating = 3, string[]? Forbidden = null,
    EvalExpect? Expect = null, bool Deterministic = false)
{
    /// <summary>The text sent: <c>Text</c>, repeated <c>Repeat</c> times with a space (for the very long scenarios), at most 2000 characters (the endpoint's limit).</summary>
    public string Body => Repeat is > 1 ? string.Join(" ", Enumerable.Repeat(Text, Repeat.Value))[..Math.Min(2000, (Text.Length + 1) * Repeat.Value - 1)] : Text;
}

public sealed record EvalFile(string Prompt, bool Synthetic, EvalScenario[] Scenarios);

public static class EvalFiles
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "evals", "brief.v1.scenarios.json"))) return Path.Combine(d.FullName, "evals");
        throw new DirectoryNotFoundException("evals/ with the scenario files was not found above the test binaries.");
    }

    public static EvalFile Load(string name) =>
        JsonSerializer.Deserialize<EvalFile>(File.ReadAllText(Path.Combine(Root(), name)), Json) ?? throw new InvalidDataException(name);

    public static EvalFile Brief() => Load("brief.v1.scenarios.json");
    public static EvalFile Triage() => Load("triage.v1.scenarios.json");
}

public enum EvalMode { Fake, Live }

public sealed class EvalRefusedException(string message) : Exception(message);

/// <summary>EVAL_LIVE=1 and not under CI runs against the configured gateway; EVAL_LIVE=1 under CI is refused (T-06.04).</summary>
public static class EvalEnvironment
{
    public const string RefusedInCi = "EVAL_LIVE=1 is refused when CI is set: a live evaluation spends real credits and runs only on the owner's machine.";
    public const string RefusedFake = "EVAL_LIVE=1 needs a real model, but Llm:Provider is Fake.";

    public static EvalMode Resolve(Func<string, string?> getEnv)
    {
        if (getEnv("EVAL_LIVE") != "1") return EvalMode.Fake;
        if (!string.IsNullOrEmpty(getEnv("CI"))) throw new EvalRefusedException(RefusedInCi);
        return EvalMode.Live;
    }

    public static EvalMode Resolve() => Resolve(Environment.GetEnvironmentVariable);
}

/// <summary>Skipped (reported as skipped, never silently passed) unless EVAL_LIVE=1 and CI is unset. Tagged tier=live.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("EVAL_LIVE") != "1") Skip = "Live evaluation: set EVAL_LIVE=1 on the owner's machine (not under CI).";
        else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))) Skip = EvalEnvironment.RefusedInCi;
    }
}
