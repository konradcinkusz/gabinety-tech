using System.Text.Json;
using Gabinety.Llm;

namespace Gabinety.Llm.Tests;

public class FakeLlmGatewayTests
{
    private const string Brief = "You write briefs. Prompt id: brief/v1.";
    private const string Triage = "You triage messages. Prompt id: triage/v1.";
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    private static async Task<LlmReply> Ask(string system, string prompt, FakeLlmGateway? gateway = null, string model = "fake-a")
    {
        gateway ??= new FakeLlmGateway();
        await using var conversation = await gateway.StartConversationAsync(new LlmConversationOptions(model, system, Generous), CancellationToken.None);
        return await conversation.AskAsync(prompt, CancellationToken.None);
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    [Theory]
    [InlineData("Potrzebujemy migracji systemu")]
    [InlineData("Mamy LEGACY kod")]
    [InlineData("aplikacja na .NET Framework 4.8")]
    public async Task BriefAboutMigrationOrLegacyGivesTheMigrationBrief(string prompt)
    {
        var json = Json((await Ask(Brief, prompt)).Text);

        Assert.Equal("migracja-legacy-dotnet", json.GetProperty("problemSlug").GetString());
        Assert.Equal(["dotnet", "csharp"], json.GetProperty("techSlugs").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("high", json.GetProperty("confidence").GetString());
        AssertBriefShape(json);
    }

    [Theory]
    [InlineData("Chcemy dodać AI do obsługi klienta")]
    [InlineData("integracja z LLM")]
    [InlineData("potrzebny GPT w aplikacji")]
    [InlineData("zbudować Agent do raportów")]
    public async Task BriefAboutAiGivesTheAiBrief(string prompt)
    {
        var json = Json((await Ask(Brief, prompt)).Text);

        Assert.Equal("integracja-ai", json.GetProperty("problemSlug").GetString());
        Assert.Equal(["llm"], json.GetProperty("techSlugs").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("medium", json.GetProperty("confidence").GetString());
        AssertBriefShape(json);
    }

    [Fact]
    public async Task BriefWithoutKeywordsGivesTheCodeAudit()
    {
        var json = Json((await Ask(Brief, "Chcemy przejrzeć nasz mail i kod")).Text);

        Assert.Equal("audyt-kodu", json.GetProperty("problemSlug").GetString());
        Assert.Empty(json.GetProperty("techSlugs").EnumerateArray());
        Assert.Equal("low", json.GetProperty("confidence").GetString());
        AssertBriefShape(json);
    }

    [Fact]
    public async Task MigrationKeywordsWinOverAiKeywords()
    {
        var json = Json((await Ask(Brief, "migracja do AI")).Text);

        Assert.Equal("migracja-legacy-dotnet", json.GetProperty("problemSlug").GetString());
    }

    [Fact]
    public async Task InvalidMarkerGivesTextThatIsNotJson()
    {
        var text = (await Ask(Brief, "migracja [[invalid]]")).Text;

        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(text));
    }

    [Fact]
    public async Task SlowMarkerEndsInTheCallersTimeoutAndIsCancelled()
    {
        var gateway = new FakeLlmGateway();
        await using var conversation = await gateway.StartConversationAsync(
            new LlmConversationOptions("fake-a", Brief, TimeSpan.FromMilliseconds(100)), CancellationToken.None);

        var ask = conversation.AskAsync("[[slow]]", CancellationToken.None);
        Assert.False(ask.IsCompleted); // it waits for the timeout instead of answering or failing at once

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ask.WaitAsync(Generous));
    }

    [Fact]
    public async Task SlowMarkerObservesTheCallersCancellation()
    {
        var gateway = new FakeLlmGateway();
        await using var conversation = await gateway.StartConversationAsync(
            new LlmConversationOptions("fake-a", Brief, Generous), CancellationToken.None);
        using var cts = new CancellationTokenSource();

        var ask = conversation.AskAsync("[[slow]]", cts.Token);
        Assert.False(ask.IsCompleted);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ask.WaitAsync(Generous));
    }

    [Fact]
    public async Task UnavailableMarkerThrowsAtAskNotAtStart()
    {
        var gateway = new FakeLlmGateway();
        await using var conversation = await gateway.StartConversationAsync(
            new LlmConversationOptions("fake-a", Brief, Generous), CancellationToken.None);

        await Assert.ThrowsAsync<LlmUnavailableException>(() => conversation.AskAsync("[[unavailable]]", CancellationToken.None));
    }

    [Fact]
    public async Task MarkersWinOverKeywordRows()
    {
        await Assert.ThrowsAsync<LlmUnavailableException>(() => Ask(Brief, "migracja [[unavailable]]"));
        await Assert.ThrowsAsync<LlmUnavailableException>(() => Ask(Triage, "spam [[unavailable]]"));
    }

    [Theory]
    [InlineData("[[hold]]")]
    [InlineData("To jest OSZUST")]
    [InlineData("kup tanio, spam")]
    public async Task TriageWithHoldWordsGivesHold(string prompt)
    {
        var reply = await Ask(Triage, prompt);

        Assert.Equal("""{"flag":"hold","reasons":["spam"]}""", reply.Text);
    }

    [Fact]
    public async Task TriageOtherwiseGivesNone()
    {
        var reply = await Ask(Triage, "Dzień dobry, kiedy możemy się spotkać?");

        Assert.Equal("""{"flag":"none","reasons":[]}""", reply.Text);
    }

    [Fact]
    public async Task RepliesDependOnlyOnPromptAndSeed()
    {
        var a = (await Ask(Brief, "audyt", new FakeLlmGateway(seed: 7))).Text;
        var b = (await Ask(Brief, "audyt", new FakeLlmGateway(seed: 7))).Text;
        var c = (await Ask(Brief, "audyt", new FakeLlmGateway(seed: 7))).Text;

        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public async Task UsageIsFourCharactersPerToken()
    {
        var reply = await Ask(Triage, new string('x', 400));

        Assert.Equal(100, reply.InputTokens);
        Assert.Equal(reply.Text.Length / 4, reply.OutputTokens);
    }

    [Fact]
    public async Task ListsTwoPricedFakeModelsAndTheCustomModels()
    {
        var custom = new CustomModelOptions
        {
            Id = "mine", DisplayName = "Mine", ProviderType = "openai", BaseUrl = "https://example.test", Model = "m",
            UsdPerMillionInput = 2, UsdPerMillionOutput = 6,
        };
        var gateway = new FakeLlmGateway(customModels: [custom]);

        var models = await gateway.ListModelsAsync(CancellationToken.None);

        Assert.Equal(["fake-a", "fake-b", "mine"], models.Select(m => m.Id));
        Assert.All(models.Take(2), m =>
        {
            Assert.True(m.Enabled);
            Assert.False(m.Custom);
            Assert.Equal(new LlmTokenPrices(1_000_000, 1.0, 4.0, null), m.Pricing);
            Assert.Equal(["low", "medium", "high"], m.ReasoningEfforts);
        });
        Assert.True(models[2].Custom);
        Assert.Equal(new LlmTokenPrices(1_000_000, 2, 6, null), models[2].Pricing);
        Assert.Equal(LlmGatewayState.Fake, gateway.Status.State);
        Assert.Equal("Fake gateway", gateway.Status.Detail);
        Assert.Equal("Fake gateway", gateway.Status.Detail);
    }

    [Fact]
    public async Task ACustomModelIsServedOffline()
    {
        var custom = new CustomModelOptions { Id = "mine", ProviderType = "openai", BaseUrl = "https://example.test", Model = "m" };

        var reply = await Ask(Triage, "spam", new FakeLlmGateway(customModels: [custom]), model: "mine");

        Assert.Contains("hold", reply.Text);
    }

    [Fact]
    public async Task AnUnknownModelThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Ask(Brief, "x", model: "nope"));
    }

    [Fact]
    public async Task TheReasoningEffortIsPassedThroughWithoutValidation()
    {
        var gateway = new FakeLlmGateway();

        await using var conversation = await gateway.StartConversationAsync(
            new LlmConversationOptions("fake-a", Brief, Generous, ReasoningEffort: "whatever"), CancellationToken.None);

        Assert.Equal("fake-a", conversation.ModelId);
    }

    private static void AssertBriefShape(JsonElement json)
    {
        Assert.True(json.GetProperty("summary").GetString()!.Length is > 0 and <= 300);
        Assert.Equal(JsonValueKind.Array, json.GetProperty("clarifyingQuestions").ValueKind);
        Assert.Contains(json.GetProperty("suggestedDurationMinutes").GetInt32(), new[] { 30, 60 });
    }
}
