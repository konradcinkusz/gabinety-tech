namespace Gabinety.Llm.Tests;

public class CopilotSessionSettingsTests
{
    private const string Prompt = "Play as White.";

    private static LlmConversationOptions Options(string? effort = null) =>
        new("model-a", Prompt, TimeSpan.FromSeconds(5), effort);

    [Fact]
    public async Task Session_has_no_tools_and_rejects_permissions()
    {
        var config = CopilotSessionSettings.ForCopilotModel(Options());

        Assert.NotNull(config.AvailableTools);
        Assert.Empty(config.AvailableTools);
        Assert.False(config.Streaming);
        Assert.NotNull(config.OnPermissionRequest);
        Assert.True(await CopilotSessionSettings.RejectsPermissionsAsync(config));
    }

    [Fact]
    public void Session_replaces_the_system_message()
    {
        var config = CopilotSessionSettings.ForCopilotModel(Options());

        Assert.Equal("Replace", config.SystemMessage!.Mode.ToString());
        Assert.Equal(Prompt, config.SystemMessage.Content);
    }

    [Fact]
    public void Model_and_reasoning_effort_are_passed_through()
    {
        var config = CopilotSessionSettings.ForCopilotModel(Options("medium"));

        Assert.Equal("model-a", config.Model);
        Assert.Equal("medium", config.ReasoningEffort);
        Assert.Null(CopilotSessionSettings.ForCopilotModel(Options()).ReasoningEffort);
    }

    [Fact]
    public void Listed_efforts_keep_order_and_none_is_null()
    {
        Assert.Equal(["low", "medium", "high"], CopilotSessionSettings.ListedEfforts(["low", "medium", "high"]));
        Assert.Null(CopilotSessionSettings.ListedEfforts([]));
        Assert.Null(CopilotSessionSettings.ListedEfforts(null));
    }
}
