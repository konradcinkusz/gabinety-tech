using Gabinety.Assistant.Features.OwnerSmoke;
using Gabinety.Llm;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Assistant.Tests.OwnerSmoke;

/// <summary>A Ready gateway with ONE non-custom (Copilot-kind) model: the owner policy must use it, the public policy would not.</summary>
sealed class OwnerGateway(Func<string> reply, double? credits = 1.5) : ILlmGateway
{
    public int Sessions;
    public LlmGatewayStatus Status => new(LlmGatewayState.Ready, "owner-test");
    public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LlmModelInfo>>([new LlmModelInfo("demo-copilot-model", "Demo", true)]);
    public Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct)
    {
        Interlocked.Increment(ref Sessions);
        return Task.FromResult<ILlmConversation>(new Conv(options.ModelId, reply, credits));
    }
    sealed class Conv(string model, Func<string> reply, double? credits) : ILlmConversation
    {
        public string ModelId => model;
        public Task<LlmReply> AskAsync(string prompt, CancellationToken ct) =>
            Task.FromResult(new LlmReply(reply(), TimeSpan.FromMilliseconds(5), 111, 22, AiCredits: credits));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public class SmokeRunnerTests
{
    const string Summary = "SUMMARY-MARKER-NOT-TO-BE-PRINTED";
    const string Question = "QUESTION-MARKER-NOT-TO-BE-PRINTED";
    static string Valid() => "{\"problemSlug\":\"migracja-legacy-dotnet\",\"techSlugs\":[\"dotnet\",\"csharp\"],\"summary\":\"" + Summary +
        "\",\"clarifyingQuestions\":[\"" + Question + "\"],\"suggestedDurationMinutes\":60,\"confidence\":\"high\"}";

    [Fact]
    public async Task Smoke_prints_model_latency_tokens_credits_and_only_slugs_of_the_suggestion()
    {
        var gw = new OwnerGateway(Valid);
        var o = new StringWriter();

        var code = await SmokeRunner.RunAsync(gw, TimeProvider.System, o, CancellationToken.None);

        var text = o.ToString();
        Assert.Equal(0, code);
        Assert.Equal(1, gw.Sessions); // a Copilot-kind model is allowed for the owner's own run
        Assert.Contains("model: demo-copilot-model", text);
        Assert.Contains("latency_ms: ", text);
        Assert.Contains("input_tokens: 111", text);
        Assert.Contains("output_tokens: 22", text);
        Assert.Contains("ai_credits: 1.5", text);
        Assert.Contains("problem: migracja-legacy-dotnet", text);
        Assert.Contains("tech: dotnet,csharp", text);
        // no brief text and no reply text beyond the slugs
        Assert.DoesNotContain(Summary, text);
        Assert.DoesNotContain(Question, text);
        Assert.DoesNotContain(SmokeRunner.BriefText, text);
    }

    [Fact]
    public async Task Smoke_reports_unknown_credits_as_unknown_not_zero()
    {
        var o = new StringWriter();
        var code = await SmokeRunner.RunAsync(new OwnerGateway(Valid, credits: null), TimeProvider.System, o, CancellationToken.None);
        Assert.Equal(0, code);
        Assert.Contains("ai_credits: unknown", o.ToString());
    }

    [Fact]
    public async Task Smoke_exits_1_without_printing_the_reply_when_the_model_answers_invalid_output()
    {
        var o = new StringWriter();
        var code = await SmokeRunner.RunAsync(new OwnerGateway(() => "not json " + Summary), TimeProvider.System, o, CancellationToken.None);
        Assert.Equal(1, code);
        Assert.Contains("outcome: invalid_output", o.ToString());
        Assert.DoesNotContain(Summary, o.ToString());
    }

    [Fact]
    public async Task The_verb_refuses_under_CI_with_exit_2_and_creates_no_host()
    {
        var o = new StringWriter();
        var code = await SmokeCommand.RunAsync(["smoke"], o, name => name == "CI" ? "1" : null);
        Assert.Equal(2, code);
        Assert.Contains(SmokeCommand.RefusedInCi, o.ToString());
    }

    [Theory]
    [InlineData("--Llm:Provider=Fake")]
    [InlineData("--Llm:Provider=fake")]
    public async Task The_verb_refuses_the_Fake_provider_with_exit_2(string arg)
    {
        var o = new StringWriter();
        var code = await SmokeCommand.RunAsync(["smoke", arg], o, _ => null);
        Assert.Equal(2, code);
        Assert.Contains(SmokeCommand.RefusedFake, o.ToString());
    }

    [Fact]
    public void The_verb_host_has_no_web_server()
    {
        using var host = SmokeCommand.BuildHost(["smoke"]);
        Assert.Null(host.Services.GetService<IServer>());
    }
}
