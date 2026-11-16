using Microsoft.Extensions.Logging;

namespace Gabinety.Llm.Tests;

public class CustomModelListingTests
{
    private const string Secret = "sk-test-DO-NOT-LEAK-12345";

    [Fact]
    public void ACustomIdEqualToACopilotIdIsNotListedAndAnErrorNamesIt()
    {
        var logger = new RecordingLogger();
        var clash = CustomModelPricingTests.Model(c => { c.Id = "gpt-x"; c.ApiKey = Secret; c.BaseUrl = "https://secret-host.example"; });
        var fine = CustomModelPricingTests.Model(c => c.Id = "tiny");

        var active = CopilotLlmGateway.ActiveCustomModels([clash, fine], ["gpt-x", "other"], logger);

        Assert.Equal(["tiny"], active.Select(c => c.Id));
        var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("gpt-x", error.Text, StringComparison.Ordinal);
        Assert.All(logger.Entries, e =>
        {
            Assert.DoesNotContain(Secret, e.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-host", e.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void NoClashListsEveryCustomModelWithoutAnError()
    {
        var logger = new RecordingLogger();

        var active = CopilotLlmGateway.ActiveCustomModels([CustomModelPricingTests.Model()], ["a"], logger);

        Assert.Single(active);
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Text)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception) + exception));
    }
}
