namespace Gabinety.Llm.Tests;

public class CustomModelSessionTests
{
    private const string Prompt = "System prompt.";
    private const string Secret = "sk-test-DO-NOT-LEAK-12345";

    [Fact]
    public void ForCustomModelLeavesTheModelIdNullAndSetsProviderTypeAndWireModel()
    {
        var custom = new CustomModelOptions
        {
            Id = "tiny", ProviderType = "openai", BaseUrl = "http://127.0.0.1:8000/v1", Model = "tiny-7b-finetune", ApiKey = Secret,
        };

        var config = CopilotSessionSettings.ForCustomModel(new LlmConversationOptions("tiny", Prompt, TimeSpan.FromSeconds(5), "high"), custom);

        Assert.Null(config.Model);
        Assert.Equal("high", config.ReasoningEffort);
        Assert.Equal("openai", config.Provider!.Type);
        Assert.Equal("tiny-7b-finetune", config.Provider.WireModel);
        Assert.Equal("http://127.0.0.1:8000/v1", config.Provider.BaseUrl);
        Assert.Equal("chat-completions", config.Provider.WireApi);
        Assert.Equal(Secret, config.Provider.ApiKey);
    }

    [Fact]
    public void AnAnthropicProviderSendsNoWireApi()
    {
        var custom = new CustomModelOptions
        {
            Id = "claude", ProviderType = "anthropic", BaseUrl = "https://api.anthropic.com", Model = "claude-x", WireApi = "chat-completions",
        };

        var config = CopilotSessionSettings.ForCustomModel(new LlmConversationOptions("claude", Prompt, TimeSpan.FromSeconds(5)), custom);

        Assert.Null(config.Provider!.WireApi);
        Assert.Equal("anthropic", config.Provider.ModelProvider);
    }

    [Theory]
    [InlineData("anthropic", "anthropic", null)]
    [InlineData("openai", "openai", "chat-completions")]
    [InlineData("azure", "azure_openai", "chat-completions")]
    [InlineData("Azure", "azure_openai", "chat-completions")]
    public void EachProviderTypeGetsItsTelemetryProviderAndWireApi(string type, string modelProvider, string? wireApi)
    {
        var custom = new CustomModelOptions { Id = "h", ProviderType = type, BaseUrl = "https://x.example", Model = "m" };

        var config = CopilotSessionSettings.ForCustomModel(new LlmConversationOptions("h", Prompt, TimeSpan.FromSeconds(5)), custom);

        Assert.Equal(type.ToLowerInvariant(), config.Provider!.Type);
        Assert.Equal(modelProvider, config.Provider.ModelProvider);
        Assert.Equal(wireApi, config.Provider.WireApi);
    }

    [Fact]
    public async Task ACustomSessionHasNoToolsAndTheReplacedSystemPromptAndRejectsPermissions()
    {
        var config = CopilotSessionSettings.ForCustomModel(new LlmConversationOptions("t", Prompt, TimeSpan.FromSeconds(5)),
            new CustomModelOptions { Id = "t", BaseUrl = "http://127.0.0.1:1", Model = "m" });

        Assert.Empty(config.AvailableTools!);
        Assert.False(config.Streaming);
        Assert.Equal(Prompt, config.SystemMessage!.Content);
        Assert.True(await CopilotSessionSettings.RejectsPermissionsAsync(config));
    }

    [Fact]
    public void AnUnknownProviderTypeThrowsWithoutTheApiKeyOrTheUrl()
    {
        var custom = new CustomModelOptions { Id = "t", ProviderType = "mystery", BaseUrl = "https://secret-host.example", Model = "m", ApiKey = Secret };

        var ex = Assert.ThrowsAny<Exception>(() =>
            CopilotSessionSettings.ForCustomModel(new LlmConversationOptions("t", Prompt, TimeSpan.FromSeconds(5)), custom));

        Assert.DoesNotContain(Secret, ex.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-host", ex.ToString(), StringComparison.Ordinal);
    }
}
