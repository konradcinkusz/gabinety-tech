using Microsoft.Extensions.Options;

namespace Gabinety.Llm;

/// <summary>
/// Checks the <c>Llm</c> section when the host starts, so a bad entry stops startup with a message that names the setting.
/// Messages never quote an API key or a base URL (COPILOT-SDK R13).
/// </summary>
internal sealed class LlmOptionsValidator : IValidateOptions<LlmOptions>
{
    private static readonly string[] Providers = ["copilot", "fake"];
    private static readonly string[] ProviderTypes = ["anthropic", "openai", "azure"];

    public ValidateOptionsResult Validate(string? name, LlmOptions options)
    {
        var errors = new List<string>();

        if (!Providers.Contains(options.Provider ?? "", StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("Llm:Provider must be 'Copilot' or 'Fake'.");
        }

        if (options.FakeLatencyMs < 0)
        {
            errors.Add("Llm:FakeLatencyMs must be zero or more.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < options.CustomModels.Count; i++)
        {
            var model = options.CustomModels[i];
            var at = $"Llm:CustomModels[{i}]";

            if (string.IsNullOrWhiteSpace(model.Id))
            {
                errors.Add($"{at}.Id is required.");
            }
            else
            {
                if (model.Id.Contains('@'))
                {
                    errors.Add($"{at}.Id '{model.Id}' must not contain '@'");
                }

                if (model.Id.Any(char.IsWhiteSpace))
                {
                    errors.Add($"{at}.Id '{model.Id}' must not contain spaces.");
                }

                if (!ids.Add(model.Id))
                {
                    errors.Add($"{at}.Id '{model.Id}' is already used by another custom model.");
                }
            }

            var anthropic = string.Equals(model.ProviderType, "anthropic", StringComparison.OrdinalIgnoreCase);
            if (!ProviderTypes.Contains(model.ProviderType ?? "", StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"{at}.ProviderType is not supported. Use 'anthropic', 'openai' or 'azure'.");
            }

            if (!Uri.TryCreate(model.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                errors.Add($"{at}.BaseUrl must be an absolute http or https URL.");
            }

            if (string.IsNullOrWhiteSpace(model.Model))
            {
                errors.Add($"{at}.Model is required.");
            }

            if (!anthropic && string.IsNullOrWhiteSpace(model.WireApi))
            {
                errors.Add($"{at}.WireApi is required for openai and azure.");
            }

            if (model.UsdPerMillionInput.HasValue != model.UsdPerMillionOutput.HasValue)
            {
                errors.Add($"{at}: set UsdPerMillionInput and UsdPerMillionOutput together, or neither.");
            }

            CheckPrice(errors, at, nameof(model.UsdPerMillionInput), model.UsdPerMillionInput);
            CheckPrice(errors, at, nameof(model.UsdPerMillionOutput), model.UsdPerMillionOutput);
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void CheckPrice(List<string> errors, string at, string name, double? price)
    {
        if (price is { } value && (!double.IsFinite(value) || value < 0))
        {
            errors.Add($"{at}.{name} must be a finite number of zero or more.");
        }
    }
}
