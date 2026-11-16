using GitHub.Copilot;

namespace Gabinety.Llm;

/// <summary>
/// The SDK session configuration of one conversation (COPILOT-SDK §4, R6, R7). Internal, so the tests can check it
/// without a runtime; no SDK type leaves this assembly.
/// </summary>
internal static class CopilotSessionSettings
{
    /// <summary>A Copilot model: its id, and the effort of the variant (null = the model's default effort).</summary>
    public static SessionConfig ForCopilotModel(LlmConversationOptions options) => new()
    {
        Model = options.ModelId,
        ReasoningEffort = options.ReasoningEffort,
        // R7: the system prompt replaces the CLI's own.
        SystemMessage = new SystemMessageConfig { Mode = SystemMessageMode.Replace, Content = options.SystemPrompt },
        // R6: no tools: an empty allow-list, and every permission request is rejected as a second guard.
        AvailableTools = [],
        Streaming = false,
        OnPermissionRequest = RejectAllPermissions,
    };

    /// <summary>The reasoning efforts a model lists, or null when it lists none.</summary>
    public static IReadOnlyList<string>? ListedEfforts(IEnumerable<string>? efforts) =>
        efforts?.ToList() is { Count: > 0 } list ? list : null;

    /// <summary>Test seam: runs the session's permission handler on an empty request and says whether it rejected.</summary>
    public static async Task<bool> RejectsPermissionsAsync(SessionConfig config)
    {
        var decision = await InvokeAsync(config);
        return IsRejection(decision);
    }

#pragma warning disable GHCP001 // The SDK marks its permission decision type experimental; it is the only way to reject a request.
    private static Task<GitHub.Copilot.Rpc.PermissionDecision> RejectAllPermissions(PermissionRequest request, PermissionInvocation invocation) =>
        Task.FromResult(GitHub.Copilot.Rpc.PermissionDecision.Reject("Gabinety: this session has no tools."));

    private static Task<GitHub.Copilot.Rpc.PermissionDecision> InvokeAsync(SessionConfig config) =>
        config.OnPermissionRequest!(new PermissionRequest(), new PermissionInvocation());

    private static bool IsRejection(GitHub.Copilot.Rpc.PermissionDecision decision) =>
        decision is GitHub.Copilot.Rpc.PermissionDecisionReject;
#pragma warning restore GHCP001
}
