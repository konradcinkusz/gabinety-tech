using System.Diagnostics;
using GitHub.Copilot;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gabinety.Llm;

/// <summary>
/// <see cref="ILlmGateway"/> on the GitHub Copilot SDK. The SDK client is started by <see cref="StartAsync"/> (called from the
/// hosted service) and never throws out of it (R4): a runtime that cannot start or is not signed in is reported through
/// <see cref="Status"/>. No prompt, reply or reasoning text goes on a span, a log or a metric.
/// </summary>
public sealed class CopilotLlmGateway(
    IOptions<CopilotOptions> options, ILogger<CopilotLlmGateway> logger, IOptions<LlmOptions>? llmOptions = null) : ILlmGateway
{
    private static readonly ActivitySource ActivitySource = new("Gabinety.Llm");

    private readonly CopilotOptions _options = options.Value;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private volatile LlmGatewayStatus _status = new(LlmGatewayState.Unavailable, "Copilot runtime not started");
    private readonly IReadOnlyList<CustomModelOptions> _configuredCustomModels = llmOptions?.Value.CustomModels ?? [];
    private IReadOnlyList<CustomModelOptions> _customModels = [];
    private CopilotClient? _client;

    public LlmGatewayStatus Status => _status;

    /// <summary>Starts the runtime, checks sign-in and lists models. Records the outcome in <see cref="Status"/>; never throws.</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await _lifecycle.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (_client is not null)
            {
                return;
            }

            // One linked cancellation around start, sign-in check and model listing together (§5).
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_options.StartupTimeout);

            try
            {
                _client = new CopilotClient(BuildClientOptions());
                await _client.StartAsync(timeout.Token);

                var auth = await _client.GetAuthStatusAsync(timeout.Token);
                if (!auth.IsAuthenticated)
                {
                    await FailAsync(LlmGatewayState.NotAuthenticated, auth.StatusMessage ?? "Not signed in to GitHub Copilot.");
                    return;
                }

                var models = await _client.ListModelsAsync(timeout.Token);
                _customModels = ActiveCustomModels(_configuredCustomModels, models.Select(m => m.Id), logger);
                _status = new LlmGatewayStatus(LlmGatewayState.Ready, $"Signed in as {auth.Login}; {models.Count} models.");
                logger.LogInformation("Copilot runtime ready with {ModelCount} models", models.Count);
            }
            catch (Exception ex)
            {
                // Only the exception type is logged as a field; the message is kept out of the log (it may echo configuration).
                logger.LogWarning("Copilot runtime unavailable ({ExceptionType})", ex.GetType().Name);
                await FailAsync(LlmGatewayState.Unavailable, $"Copilot runtime unavailable: {ex.GetType().Name}: {Redact(ex.Message)}");
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Closes the runtime. Safe to call when it never started.</summary>
    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            await DisposeClientAsync();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

#pragma warning disable GHCP001 // The SDK marks the token price unit experimental and CachePrice deprecated; both are read in this method and its helper only.
    public async Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct)
    {
        var client = RequireReady();
        var models = await client.ListModelsAsync(ct);
        var priceUnits = await ListPriceUnitsAsync(client, ct);

        var listed = models
            .Select(m => new LlmModelInfo(m.Id, m.Name, !IsDisabled(m.Policy),
                TokenPriceMapping.Map(
                    m.Billing?.TokenPrices?.BatchSize,
                    m.Billing?.TokenPrices?.InputPrice,
                    m.Billing?.TokenPrices?.OutputPrice,
                    m.Billing?.TokenPrices?.CacheReadPrice,
                    m.Billing?.TokenPrices?.CachePrice,
                    priceUnits.TryGetValue(m.Id, out var nanoAiu) ? nanoAiu : null),
                CopilotSessionSettings.ListedEfforts(m.SupportedReasoningEfforts),
                m.DefaultReasoningEffort))
            .ToList();

        listed.AddRange(_customModels.Select(CustomModelPricing.ToModelInfo));
        return listed;
    }

    /// <summary>
    /// The unit each model's token prices are quoted in, by model id (<c>models.listTokenPriceUnits</c>): true for nano-AIU.
    /// A failure leaves the models listed without prices (R11); it does not make the gateway unavailable.
    /// </summary>
    private async Task<Dictionary<string, bool>> ListPriceUnitsAsync(CopilotClient client, CancellationToken ct)
    {
        var nanoAiuByModel = new Dictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            var units = await client.Rpc.Models.ListTokenPriceUnitsAsync(ct);
            foreach (var entry in units.Units)
            {
                if (entry.Unit == GitHub.Copilot.Rpc.TokenPriceUnit.AiCredits || entry.Unit == GitHub.Copilot.Rpc.TokenPriceUnit.NanoAiu)
                {
                    nanoAiuByModel[entry.ModelId] = entry.Unit == GitHub.Copilot.Rpc.TokenPriceUnit.NanoAiu;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Only the exception type: the message may echo what the runtime sent.
            logger.LogWarning("Could not list token price units; models are listed without prices ({ExceptionType})", ex.GetType().Name);
        }

        return nanoAiuByModel;
    }
#pragma warning restore GHCP001

    /// <summary>
    /// The custom models that may be listed. A custom id that a Copilot model already uses is refused and an error naming
    /// the id (nothing else of the entry) is logged: the Copilot id would otherwise be ambiguous.
    /// </summary>
    internal static IReadOnlyList<CustomModelOptions> ActiveCustomModels(
        IEnumerable<CustomModelOptions> configured, IEnumerable<string> copilotIds, ILogger logger)
    {
        var taken = copilotIds.ToHashSet(StringComparer.Ordinal);
        var active = new List<CustomModelOptions>();
        foreach (var custom in configured)
        {
            if (taken.Contains(custom.Id))
            {
                logger.LogError("Custom model {ModelId} is not listed: a Copilot model has the same id. Rename it under Llm:CustomModels.", custom.Id);
            }
            else
            {
                active.Add(custom);
            }
        }

        logger.LogInformation("{CustomModelCount} custom models listed", active.Count);
        return active;
    }

    public async Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct)
    {
        var client = RequireReady();
        var custom = _customModels.FirstOrDefault(c => string.Equals(c.Id, options.ModelId, StringComparison.Ordinal));
        var config = custom is null
            ? CopilotSessionSettings.ForCopilotModel(options)
            : CopilotSessionSettings.ForCustomModel(options, custom);

        var session = await client.CreateSessionAsync(config, ct);
        return new CopilotConversation(session, options, logger);
    }

    // The wire value of Rpc.ModelPolicyState.Disabled; comparing the string keeps the experimental type (GHCP001) out of this file.
    private static bool IsDisabled(ModelPolicy? policy) =>
        string.Equals(policy?.State, "disabled", StringComparison.OrdinalIgnoreCase);

    private CopilotClient RequireReady() =>
        _client is { } client && _status.State == LlmGatewayState.Ready
            ? client
            : throw new LlmUnavailableException(_status.Detail);

    private CopilotClientOptions BuildClientOptions() => new()
    {
        Connection = _options switch
        {
            { CliUrl: not null } => RuntimeConnection.ForUri(_options.CliUrl, null),
            { CliPath: not null } => RuntimeConnection.ForStdio(_options.CliPath, []),
            _ => null,
        },
        GitHubToken = _options.GitHubToken,
    };

    private string Redact(string message) =>
        string.IsNullOrEmpty(_options.GitHubToken) ? message : message.Replace(_options.GitHubToken, "***", StringComparison.Ordinal);

    private async Task FailAsync(LlmGatewayState state, string detail)
    {
        _status = new LlmGatewayStatus(state, detail);
        await DisposeClientAsync();
    }

    private async Task DisposeClientAsync()
    {
        if (_client is not { } client)
        {
            return;
        }

        _client = null;
        try
        {
            await client.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning("Copilot runtime did not stop cleanly ({ExceptionType})", ex.GetType().Name);
        }
    }

    /// <summary>One SDK session = one conversation. Replies are serialised by the caller (R9).</summary>
    private sealed class CopilotConversation : ILlmConversation
    {
        private readonly CopilotSession _session;
        private readonly LlmConversationOptions _options;
        private readonly ILogger _logger;
        // The usage and reasoning events of the reply in flight. Replies are serialised, so one reply at a time.
        private readonly object _replyGate = new();
        private readonly IDisposable _usageSubscription;
        private readonly IDisposable _reasoningSubscription;
        private readonly List<string> _reasoningBlocks = [];
        private UsageTally _usage = new();

        public CopilotConversation(CopilotSession session, LlmConversationOptions options, ILogger logger)
        {
            _session = session;
            _options = options;
            _logger = logger;
            _usageSubscription = session.On<AssistantUsageEvent>(AddUsage);
            _reasoningSubscription = session.On<AssistantReasoningEvent>(AddReasoning);
        }

        public string ModelId => _options.ModelId;

        public async Task<LlmReply> AskAsync(string prompt, CancellationToken ct)
        {
            using var activity = ActivitySource.StartActivity($"chat {ModelId}", ActivityKind.Client);
            activity?.SetTag("gen_ai.request.model", ModelId);

            ResetReply();
            var stopwatch = Stopwatch.StartNew();

            AssistantMessageEvent? final;
            try
            {
                // R8: the caller's timeout is applied twice, as the SDK timeout and as a linked cancellation.
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(_options.Timeout);
                final = await _session.SendAndWaitAsync(new MessageOptions { Prompt = prompt }, _options.Timeout, linked.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException && !ct.IsCancellationRequested)
            {
                await AbortQuietlyAsync();
                throw new TimeoutException($"Model {ModelId} did not finish its reply within {_options.Timeout}.", ex);
            }
            catch (OperationCanceledException)
            {
                await AbortQuietlyAsync();
                throw;
            }

            stopwatch.Stop();
            var text = final?.Data.Content
                ?? throw new InvalidOperationException($"Model {ModelId} finished without an assistant message.");

            ReplyUsage usage;
            string? reasoning;
            lock (_replyGate)
            {
                usage = _usage.Snapshot();
                reasoning = ReplyReasoning.Choose(final.Data.ReasoningText, _reasoningBlocks);
            }

            if (usage.InputTokens is { } input)
            {
                activity?.SetTag("gen_ai.usage.input_tokens", input);
            }

            if (usage.OutputTokens is { } output)
            {
                activity?.SetTag("gen_ai.usage.output_tokens", output);
            }

            _logger.LogDebug("Model {ModelId} replied in {LatencyMs} ms", ModelId, stopwatch.ElapsedMilliseconds);
            return new LlmReply(text, stopwatch.Elapsed, usage.InputTokens, usage.OutputTokens,
                reasoning, usage.ReasoningTokens, usage.CachedInputTokens, usage.AiCredits);
        }

        public async ValueTask DisposeAsync()
        {
            _usageSubscription.Dispose();
            _reasoningSubscription.Dispose();
            await _session.DisposeAsync();
        }

        private void AddUsage(AssistantUsageEvent e)
        {
            lock (_replyGate)
            {
                _usage.Add(
                    (long?)e.Data.InputTokens,
                    (long?)e.Data.OutputTokens,
                    (long?)e.Data.ReasoningTokens,
                    (long?)e.Data.CacheReadTokens,
                    e.Data.CopilotUsage?.TotalNanoAiu,
                    e.Data.AiCreditsStatus == AiCreditsStatus.Unavailable);
            }
        }

        private void AddReasoning(AssistantReasoningEvent e)
        {
            lock (_replyGate)
            {
                _reasoningBlocks.Add(e.Data.Content);
            }
        }

        private void ResetReply()
        {
            lock (_replyGate)
            {
                _usage = new UsageTally();
                _reasoningBlocks.Clear();
            }
        }

        private async Task AbortQuietlyAsync()
        {
            try
            {
                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _session.AbortAsync(cancel.Token);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogWarning("Could not abort the in-flight reply of model {ModelId} ({ExceptionType})", ModelId, ex.GetType().Name);
            }
        }
    }
}
