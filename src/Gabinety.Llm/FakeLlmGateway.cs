using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gabinety.Llm;

/// <summary>
/// Deterministic, offline stand-in for the Copilot gateway (COPILOT-SDK §10). The reply depends only on the prompt text and
/// the seed. Two models (<c>fake-a</c>, <c>fake-b</c>) plus the configured custom models.
/// </summary>
/// <param name="seed">Seed (<c>Llm:Seed</c>); picks between equivalent brief variants.</param>
/// <param name="latency">Simulated delay before every reply (<c>Llm:FakeLatencyMs</c>).</param>
/// <param name="customModels">Custom models to list and serve next to the two fake models.</param>
public sealed partial class FakeLlmGateway(int seed = 1, TimeSpan? latency = null, IEnumerable<CustomModelOptions>? customModels = null) : ILlmGateway
{
    private const string FakeAId = "fake-a";
    private const string FakeBId = "fake-b";
    private static readonly IReadOnlyList<string> FakeEfforts = ["low", "medium", "high"];
    private static readonly LlmTokenPrices FakePrices = new(1_000_000, 1.0, 4.0, null);
    private static readonly JsonSerializerOptions Camel = new(JsonSerializerDefaults.Web);

    private readonly TimeSpan _latency = latency ?? TimeSpan.Zero;
    private readonly IReadOnlyList<CustomModelOptions> _custom = [.. customModels ?? []];

    public LlmGatewayStatus Status { get; } = new(LlmGatewayState.Fake, "Fake gateway");

    public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct)
    {
        IReadOnlyList<LlmModelInfo> models =
        [
            new LlmModelInfo(FakeAId, "Fake A", true, FakePrices, FakeEfforts, "medium"),
            new LlmModelInfo(FakeBId, "Fake B", true, FakePrices, FakeEfforts, "medium"),
            .. _custom.Select(c => new LlmModelInfo(c.Id, c.DisplayName ?? c.Id, true,
                c is { UsdPerMillionInput: { } input, UsdPerMillionOutput: { } output } ? new LlmTokenPrices(1_000_000, input, output, null) : null,
                Custom: true)),
        ];
        return Task.FromResult(models);
    }

    public Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct)
    {
        var known = options.ModelId is FakeAId or FakeBId || _custom.Any(c => c.Id == options.ModelId);
        if (!known)
        {
            throw new ArgumentException("Unknown model id.", nameof(options));
        }

        return Task.FromResult<ILlmConversation>(new FakeConversation(this, options));
    }

    [GeneratedRegex(@"\bai\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AiWord();

    private static bool Has(string text, params string[] words) =>
        words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    private string Reply(string systemPrompt, string prompt)
    {
        if (Has(prompt, "[[invalid]]"))
        {
            return "this is not json {";
        }

        if (Has(prompt, "[[unavailable]]"))
        {
            throw new LlmUnavailableException("Fake gateway: unavailable.");
        }

        if (Has(systemPrompt, "brief/v1"))
        {
            if (Has(prompt, "migrac", "legacy", ".net framework"))
            {
                return Brief("migracja-legacy-dotnet", ["dotnet", "csharp"], "Migracja starszej aplikacji .NET na nowszą platformę.", "high", prompt);
            }

            if (AiWord().IsMatch(prompt) || Has(prompt, "llm", "gpt", "agent"))
            {
                return Brief("integracja-ai", ["llm"], "Integracja funkcji AI z istniejącym systemem.", "medium", prompt);
            }

            return Brief("audyt-kodu", [], "Przegląd jakości kodu i architektury.", "low", prompt);
        }

        if (Has(systemPrompt, "triage/v1"))
        {
            return Has(prompt, "[[hold]]", "oszust", "spam")
                ? """{"flag":"hold","reasons":["spam"]}"""
                : """{"flag":"none","reasons":[]}""";
        }

        return "ok";
    }

    private string Brief(string problem, string[] tech, string summary, string confidence, string prompt)
    {
        // The only use of the seed: a stable pick between the two session lengths.
        var minutes = (seed + prompt.Sum(c => c)) % 2 == 0 ? 30 : 60;
        return JsonSerializer.Serialize(new
        {
            problemSlug = problem,
            techSlugs = tech,
            summary,
            clarifyingQuestions = confidence == "high" ? Array.Empty<string>() : ["Jaki jest oczekiwany termin?"],
            suggestedDurationMinutes = minutes,
            confidence,
        }, Camel);
    }

    private sealed class FakeConversation(FakeLlmGateway gateway, LlmConversationOptions options) : ILlmConversation
    {
        public string ModelId { get; } = options.ModelId;

        public async Task<LlmReply> AskAsync(string prompt, CancellationToken ct)
        {
            var clock = Stopwatch.StartNew();
            ct.ThrowIfCancellationRequested();

            if (Has(prompt, "[[slow]]"))
            {
                // Waits for the caller's timeout, then reports the abort; the caller's token ends it early.
                await Task.Delay(options.Timeout, ct);
                throw new OperationCanceledException("Fake gateway: [[slow]] ran out of time.");
            }

            var text = gateway.Reply(options.SystemPrompt, prompt);
            if (gateway._latency > TimeSpan.Zero)
            {
                await Task.Delay(gateway._latency, ct);
            }

            var input = prompt.Length / 4;
            var output = text.Length / 4;
            var credits = (input * FakePrices.InputCredits + output * FakePrices.OutputCredits) / FakePrices.BatchSize;
            return new LlmReply(text, clock.Elapsed, input, output, AiCredits: credits);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
