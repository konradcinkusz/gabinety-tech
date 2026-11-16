namespace Gabinety.Llm;

/// <summary>
/// One entry of <c>Llm:CustomModels</c>: a bring-your-own-key model. The API key is a platform secret (COPILOT-SDK R13):
/// it is never logged, never in an exception message and never in <see cref="ToString"/>.
/// </summary>
public sealed class CustomModelOptions
{
    /// <summary>The model id. No <c>@</c>, no spaces, unique ignoring case.</summary>
    public string Id { get; set; } = "";

    /// <summary>The name shown in the UI. Defaults to <see cref="Id"/>.</summary>
    public string? DisplayName { get; set; }

    /// <summary><c>anthropic</c>, <c>openai</c> (any OpenAI-compatible endpoint) or <c>azure</c>.</summary>
    public string ProviderType { get; set; } = "openai";

    /// <summary>Absolute http or https URL of the provider's API.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>The model name the endpoint expects (sent later as <c>ProviderConfig.WireModel</c>).</summary>
    public string Model { get; set; } = "";

    /// <summary>The wire API format; needed for <c>openai</c> and <c>azure</c>, not read for <c>anthropic</c>.</summary>
    public string WireApi { get; set; } = "chat-completions";

    /// <summary>The endpoint's API key. Optional for an endpoint that takes none.</summary>
    public string? ApiKey { get; set; }

    /// <summary>US dollars per million input tokens. Set with <see cref="UsdPerMillionOutput"/>, or not at all.</summary>
    public double? UsdPerMillionInput { get; set; }

    /// <summary>US dollars per million output tokens. Set with <see cref="UsdPerMillionInput"/>, or not at all.</summary>
    public double? UsdPerMillionOutput { get; set; }

    public override string ToString() => $"CustomModel {Id} ({ProviderType}, model {Model})";
}
