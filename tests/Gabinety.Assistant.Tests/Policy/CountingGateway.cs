using Gabinety.Llm;

namespace Gabinety.Assistant.Tests.Policy;

/// <summary>ILlmGateway test double: reports the state and models it is given and counts the calls that matter (a conversation = a session).</summary>
public sealed class CountingGateway(LlmGatewayState state, IReadOnlyList<LlmModelInfo> models) : ILlmGateway
{
    int _starts, _lists;
    public int StartConversationCalls => _starts;
    public int ListModelsCalls => _lists;
    public Func<CancellationToken, Task>? OnList { get; init; }
    public LlmGatewayStatus Status { get; } = new(state, "counting double");

    public async Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _lists);
        if (OnList is not null) await OnList(ct);
        return models;
    }

    public Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct)
    {
        Interlocked.Increment(ref _starts);
        throw new InvalidOperationException("No session may be created in these tests.");
    }

    public static LlmModelInfo Copilot(string id) => new(id, id, true, new LlmTokenPrices(1, 1, 1, null));
    public static LlmModelInfo Custom(string id, bool enabled = true) => new(id, id, enabled, new LlmTokenPrices(1_000_000, 3, 15, null), Custom: true);
}
