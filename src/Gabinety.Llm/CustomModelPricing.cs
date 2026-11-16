namespace Gabinety.Llm;

/// <summary>
/// The listing and prices of a custom model (COPILOT-SDK §6, §8). A custom model is priced by a COMPLETE pair, input and
/// output, in US dollars per million tokens, as <see cref="LlmTokenPrices"/> documents for a custom model; nothing is
/// converted to AI credits here (the Assistant estimates the cost). A model with no complete pair has no price: "price
/// unknown", never zero (R11).
/// </summary>
internal static class CustomModelPricing
{
    private const long MillionTokens = 1_000_000;

    /// <returns>US dollars per million input and output tokens, or null when the model has no complete pair.</returns>
    public static LlmTokenPrices? TokenPrices(CustomModelOptions model) =>
        model.UsdPerMillionInput is { } usdIn && model.UsdPerMillionOutput is { } usdOut
            ? new LlmTokenPrices(MillionTokens, usdIn, usdOut, null)
            : null;

    /// <summary>The catalog entry of a custom model. Never carries the API key or the URL.</summary>
    public static LlmModelInfo ToModelInfo(CustomModelOptions model) =>
        new(model.Id, model.DisplayName ?? model.Id, Enabled: true, TokenPrices(model), Custom: true);
}
