using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Contracts.Assistant;
using Gabinety.ServiceDefaults;

namespace Gabinety.Reputation.Clients;

public abstract record AssistantTriageResult
{
    /// <summary>The Assistant answered with a valid triage suggestion (CONTRACTS 6).</summary>
    public sealed record Verdict(TriageVerdict Flag, IReadOnlyList<TriageReason> Reasons, string ModelId) : AssistantTriageResult;
}

/// <summary>
/// The Assistant gave no usable suggestion: outage, timeout, 4xx/5xx, or an answer that is not the CONTRACTS 6 shape. <paramref name="Cause"/> is one of a few
/// fixed codes (<c>timeout</c>, <c>transport</c>, <c>http_status</c>, <c>invalid_response</c>, <c>not_configured</c>): it never carries a URL, the key or review text.
/// An unusable answer is never read as <c>none</c>.
/// </summary>
public sealed record AssistantUnavailable(string Cause) : AssistantTriageResult;

public interface IAssistantClient
{
    /// <summary>POST /internal/v1/assistant/review-triage (CONTRACTS 6). Never throws except when <paramref name="ct"/> is cancelled.</summary>
    Task<AssistantTriageResult> TriageAsync(int rating, string text, CancellationToken ct);
}

/// <summary>
/// CONTRACTS 6 writes the triage answer as <c>flag: "none"|"hold"</c> and reasons in snake_case (<c>personal_data</c>, <c>off_topic</c>), written by hand by the Assistant.
/// <c>Gabinety.Contracts.Assistant</c> enums serialize as PascalCase, so this is parsed explicitly and strictly: the exact lower-case strings or nothing.
/// </summary>
public static class TriageWire
{
    static readonly Dictionary<string, TriageReason> Reasons = new(StringComparer.Ordinal)
    {
        ["spam"] = TriageReason.Spam, ["abuse"] = TriageReason.Abuse, ["personal_data"] = TriageReason.PersonalData,
        ["off_topic"] = TriageReason.OffTopic, ["threat"] = TriageReason.Threat,
    };
    const int MaxModelIdLength = 100;

    public static string Code(TriageReason reason) => Reasons.First(p => p.Value == reason).Key;

    public static AssistantTriageResult Parse(string json)
    {
        var invalid = new AssistantUnavailable("invalid_response");
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return invalid;
            var root = doc.RootElement;
            if (!root.TryGetProperty("flag", out var f) || f.ValueKind != JsonValueKind.String) return invalid;
            var flag = f.GetString() switch { "none" => TriageVerdict.None, "hold" => TriageVerdict.Hold, _ => (TriageVerdict?)null };
            if (flag is null) return invalid;
            if (!root.TryGetProperty("reasons", out var r) || r.ValueKind != JsonValueKind.Array) return invalid;
            var reasons = new List<TriageReason>();
            foreach (var e in r.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.String || !Reasons.TryGetValue(e.GetString()!, out var reason)) return invalid;
                if (!reasons.Contains(reason)) reasons.Add(reason);
            }
            if (!root.TryGetProperty("modelId", out var m) || m.ValueKind != JsonValueKind.String || m.GetString() is not { Length: > 0 and <= MaxModelIdLength } modelId) return invalid;
            return new AssistantTriageResult.Verdict(flag.Value, reasons, modelId);
        }
        catch (JsonException) { return invalid; }
    }
}

/// <summary>
/// Typed client for the Assistant triage (ADR-0007, SPEC 5.1 Reputation -> Assistant: 25 s, no retry). Built outside the shared resilience pipeline, like
/// <see cref="BookingClient"/>, so the budget is exactly this one. No retry here: SPEC 5.1 says none, and the job's own bounded attempts with backoff
/// (a later run) are the retry. The review text travels only in the request body; no message, result or log carries it, the URL or the key.
/// </summary>
public sealed class AssistantClient(HttpClient http, string? internalKey, TimeSpan? timeout = null) : IAssistantClient
{
    /// <summary>SPEC 5.1: 25 s, the same as the Assistant's own <c>Assistant:TriageTimeoutSeconds</c> default.</summary>
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(25);
    const int MaxResponseBytes = 16 * 1024;
    readonly TimeSpan _timeout = timeout ?? CallTimeout;

    public async Task<AssistantTriageResult> TriageAsync(int rating, string text, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (http.BaseAddress is null) return new AssistantUnavailable("not_configured");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "internal/v1/assistant/review-triage") { Content = JsonContent.Create(new { rating, text }) };
            if (!string.IsNullOrEmpty(internalKey)) request.Headers.Add("X-Internal-Key", internalKey);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode) return new AssistantUnavailable("http_status"); // 503 is the Assistant's "no suggestion"; 401 is a configuration fault
            if (response.Content.Headers.ContentLength > MaxResponseBytes) return new AssistantUnavailable("invalid_response");
            return TriageWire.Parse(await response.Content.ReadAsStringAsync(cts.Token));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new AssistantUnavailable("timeout"); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { return new AssistantUnavailable("transport"); } // the message may echo the URL or the key: dropped
    }
}

/// <summary>Registers the client from Backend:Assistant and Internal:Keys:Assistant (CONTRACTS 10.3). The key is read here and never logged.</summary>
public sealed class AssistantClientFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) =>
        builder.Services.AddSingleton<IAssistantClient>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var url = config["Backend:Assistant"]?.TrimEnd('/');
            var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2), ConnectTimeout = TimeSpan.FromSeconds(5) })
            { BaseAddress = string.IsNullOrEmpty(url) ? null : new Uri(url + "/"), Timeout = Timeout.InfiniteTimeSpan };
            return new AssistantClient(http, config["Internal:Keys:Assistant"]);
        });

    public void Map(IEndpointRouteBuilder endpoints) { }
}
