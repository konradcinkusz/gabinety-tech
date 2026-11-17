using System.Net;
using System.Text.Json;
using Gabinety.Contracts.Assistant;
using Gabinety.Reputation.Clients;

namespace Gabinety.Reputation.Tests.Triage;

/// <summary>Handler standing in for the Assistant. Records what the client sent; the answer is whatever the test sets.</summary>
sealed class FakeAssistantHandler : HttpMessageHandler
{
    public const string Key = "k-assistant-DO-NOT-LEAK-9012", BaseUrl = "http://assistant.demo.invalid";
    public const string Text = "Synthetyczna opinia TRIAGE-TEKST-NIE-DO-LOGU, rzeczowo i uprzejmie.";
    public Func<HttpRequestMessage, int, Task<HttpResponseMessage>> Answer = (_, _) => Task.FromResult(Json("""{"flag":"none","reasons":[],"modelId":"demo-model"}"""));
    public int Calls;
    public bool Hang; // never answers; ends only when the client's token is cancelled
    public string? LastKeyHeader, LastPath, LastBody, LastMethod;

    public static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
    {
        var n = Interlocked.Increment(ref Calls);
        LastKeyHeader = r.Headers.TryGetValues("X-Internal-Key", out var v) ? v.Single() : null;
        LastPath = r.RequestUri!.AbsolutePath;
        LastMethod = r.Method.Method;
        LastBody = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);
        if (Hang) await Task.Delay(TimeSpan.FromSeconds(30), ct);
        return await Answer(r, n);
    }
}

public class AssistantClientTests
{
    static (AssistantClient Client, FakeAssistantHandler Handler) Make(TimeSpan? timeout = null)
    {
        var h = new FakeAssistantHandler();
        return (new AssistantClient(new HttpClient(h) { BaseAddress = new Uri(FakeAssistantHandler.BaseUrl + "/") }, FakeAssistantHandler.Key, timeout), h);
    }

    [Fact]
    public async Task It_posts_rating_and_text_only_to_the_triage_path_with_the_internal_key()
    {
        var (client, h) = Make();

        await client.TriageAsync(4, FakeAssistantHandler.Text, default);

        Assert.Equal("POST", h.LastMethod);
        Assert.Equal("/internal/v1/assistant/review-triage", h.LastPath);
        Assert.Equal(FakeAssistantHandler.Key, h.LastKeyHeader);
        using var body = JsonDocument.Parse(h.LastBody!);
        Assert.Equal(["rating", "text"], body.RootElement.EnumerateObject().Select(p => p.Name.ToLowerInvariant()).Order().ToArray());
        Assert.Equal(4, body.RootElement.GetProperty("rating").GetInt32());
        Assert.Equal(FakeAssistantHandler.Text, body.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task The_real_response_shape_of_the_assistant_is_read_with_the_CONTRACTS_strings()
    {
        // The shape TriageFeature.Wire writes: camelCase property names, lower-case flag, snake_case reasons.
        var (client, h) = Make();
        h.Answer = (_, _) => Task.FromResult(FakeAssistantHandler.Json("""{"flag":"hold","reasons":["personal_data","off_topic"],"modelId":"demo-model-7"}"""));

        var result = await client.TriageAsync(1, "x", default);

        var v = Assert.IsType<AssistantTriageResult.Verdict>(result);
        Assert.Equal(TriageVerdict.Hold, v.Flag);
        Assert.Equal([TriageReason.PersonalData, TriageReason.OffTopic], v.Reasons);
        Assert.Equal("demo-model-7", v.ModelId);
    }

    [Theory]
    [InlineData("""{"flag":"none","reasons":[],"modelId":"m"}""", TriageVerdict.None)]
    [InlineData("""{"flag":"hold","reasons":["spam"],"modelId":"m"}""", TriageVerdict.Hold)]
    public void The_flag_strings_none_and_hold_parse(string json, TriageVerdict expected) =>
        Assert.Equal(expected, Assert.IsType<AssistantTriageResult.Verdict>(TriageWire.Parse(json)).Flag);

    [Theory]
    [InlineData("spam", TriageReason.Spam)]
    [InlineData("abuse", TriageReason.Abuse)]
    [InlineData("personal_data", TriageReason.PersonalData)]
    [InlineData("off_topic", TriageReason.OffTopic)]
    [InlineData("threat", TriageReason.Threat)]
    public void Each_CONTRACTS_reason_string_parses_and_round_trips(string code, TriageReason expected)
    {
        var v = Assert.IsType<AssistantTriageResult.Verdict>(TriageWire.Parse($$"""{"flag":"hold","reasons":["{{code}}"],"modelId":"m"}"""));
        Assert.Equal([expected], v.Reasons);
        Assert.Equal(code, TriageWire.Code(expected));
    }

    [Theory]
    [InlineData("""{"flag":"maybe","reasons":[],"modelId":"m"}""")]        // unknown flag is never "none"
    [InlineData("""{"flag":"None","reasons":[],"modelId":"m"}""")]         // the DTO's PascalCase is not the contract
    [InlineData("""{"flag":"Hold","reasons":["Spam"],"modelId":"m"}""")]
    [InlineData("""{"flag":"hold","reasons":["PersonalData"],"modelId":"m"}""")]
    [InlineData("""{"flag":"hold","reasons":["gossip"],"modelId":"m"}""")]
    [InlineData("""{"flag":"hold","reasons":[1],"modelId":"m"}""")]
    [InlineData("""{"flag":"none","reasons":[]}""")]                       // no modelId
    [InlineData("""{"flag":"none","reasons":[],"modelId":""}""")]
    [InlineData("""{"reasons":[],"modelId":"m"}""")]
    [InlineData("""{"flag":"none","modelId":"m"}""")]
    [InlineData("""["none"]""")]
    [InlineData("not json")]
    [InlineData("")]
    public void Anything_that_is_not_the_contract_is_unavailable_never_none(string json) =>
        Assert.IsType<AssistantUnavailable>(TriageWire.Parse(json));

    [Fact]
    public async Task A_503_is_unavailable_and_is_not_retried()
    {
        var (client, h) = Make();
        h.Answer = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        Assert.IsType<AssistantUnavailable>(await client.TriageAsync(5, "x", default));
        Assert.Equal(1, h.Calls); // SPEC 5.1: Reputation -> Assistant triage, no retry
    }

    [Fact]
    public async Task A_401_and_a_garbled_200_are_unavailable()
    {
        var (client, h) = Make();
        h.Answer = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        Assert.IsType<AssistantUnavailable>(await client.TriageAsync(5, "x", default));
        h.Answer = (_, _) => Task.FromResult(FakeAssistantHandler.Json("<html>"));
        Assert.IsType<AssistantUnavailable>(await client.TriageAsync(5, "x", default));
    }

    [Fact]
    public async Task A_connection_failure_is_unavailable_and_nothing_secret_is_in_the_result()
    {
        var (client, h) = Make();
        h.Answer = (_, _) => throw new HttpRequestException($"refused {FakeAssistantHandler.BaseUrl} key {FakeAssistantHandler.Key} text {FakeAssistantHandler.Text}");

        var result = await client.TriageAsync(5, FakeAssistantHandler.Text, default);

        Assert.IsType<AssistantUnavailable>(result);
        var shown = result.ToString()!;
        Assert.DoesNotContain(FakeAssistantHandler.Key, shown);
        Assert.DoesNotContain("demo.invalid", shown);
        Assert.DoesNotContain("TRIAGE-TEKST", shown);
    }

    [Fact]
    public async Task A_slow_assistant_is_unavailable_after_the_budget_and_the_caller_token_still_cancels()
    {
        var (client, h) = Make(TimeSpan.FromMilliseconds(80));
        h.Hang = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var result = await client.TriageAsync(5, "x", default);

        Assert.IsType<AssistantUnavailable>(result);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(25), AssistantClient.CallTimeout); // SPEC 5.1
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.TriageAsync(5, "x", cts.Token));
    }
}
