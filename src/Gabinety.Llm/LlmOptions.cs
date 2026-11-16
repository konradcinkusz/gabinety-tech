namespace Gabinety.Llm;

/// <summary>Configuration section <c>Llm</c> (COPILOT-SDK §6). The <c>Copilot:*</c> keys belong to a later task.</summary>
public sealed class LlmOptions
{
    /// <summary><c>Copilot</c> (default) or <c>Fake</c>.</summary>
    public string Provider { get; set; } = "Copilot";

    /// <summary>Seed for the fake gateway.</summary>
    public int Seed { get; set; } = 1;

    /// <summary>Simulated delay of the fake gateway, in milliseconds.</summary>
    public int FakeLatencyMs { get; set; }

    /// <summary>Bring-your-own-key models, listed next to the others.</summary>
    public List<CustomModelOptions> CustomModels { get; set; } = [];

    public override string ToString() =>
        $"Llm (Provider {Provider}, Seed {Seed}, FakeLatencyMs {FakeLatencyMs}, {CustomModels.Count} custom model(s): {string.Join(", ", CustomModels)})";
}
