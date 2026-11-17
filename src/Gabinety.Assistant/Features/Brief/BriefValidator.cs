using System.Text.Json;
using Gabinety.Contracts.Assistant;
using Gabinety.Contracts.Directory;

namespace Gabinety.Assistant.Features.Brief;

public sealed record BriefDraft(string ProblemSlug, IReadOnlyList<string> TechSlugs, string Summary,
    IReadOnlyList<string> ClarifyingQuestions, int SuggestedDurationMinutes, Confidence Confidence);

/// <summary>Validates a model reply against the BriefSuggestion schema and the live taxonomy (CONTRACTS 6). The model's text is data: nothing is trusted.</summary>
public static class BriefValidator
{
    /// <summary>
    /// A draft, or a fixed error text. The error text is sent back to the model for the repair attempt, so it never echoes
    /// the reply (which may carry the user's words) and never names a slug the model produced.
    /// </summary>
    public static (BriefDraft? Draft, string? Error) Validate(string reply, TaxonomyDto taxonomy)
    {
        var json = Unfence(reply);
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return (null, "Odpowiedź nie jest poprawnym JSON-em. Zwróć jeden obiekt JSON."); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, "Odpowiedź musi być jednym obiektem JSON.");

            if (Str(root, "problemSlug") is not { } problem) return (null, "Pole problemSlug musi być tekstem.");
            if (!taxonomy.Problems.Any(c => c.Slug == problem)) return (null, "Pole problemSlug nie jest slugiem z listy kategorii problemu.");

            if (Strings(root, "techSlugs") is not { } tech) return (null, "Pole techSlugs musi być tablicą tekstów.");
            if (tech.Count > 4) return (null, "Pole techSlugs może mieć najwyżej 4 elementy.");
            if (tech.Distinct().Count() != tech.Count) return (null, "Pole techSlugs nie może powtarzać slugów.");
            if (tech.Any(t => taxonomy.Techs.All(c => c.Slug != t))) return (null, "Pole techSlugs zawiera slug spoza listy technologii.");

            if (Str(root, "summary") is not { Length: > 0 and <= 300 } summary || string.IsNullOrWhiteSpace(summary))
                return (null, "Pole summary musi mieć od 1 do 300 znaków.");

            if (Strings(root, "clarifyingQuestions") is not { } questions) return (null, "Pole clarifyingQuestions musi być tablicą tekstów.");
            if (questions.Count > 3 || questions.Any(q => q.Length is 0 or > 140 || string.IsNullOrWhiteSpace(q)))
                return (null, "Pole clarifyingQuestions: najwyżej 3 pytania, każde od 1 do 140 znaków.");

            if (!root.TryGetProperty("suggestedDurationMinutes", out var m) || m.ValueKind != JsonValueKind.Number || !m.TryGetInt32(out var minutes) || minutes is not (30 or 60))
                return (null, "Pole suggestedDurationMinutes musi mieć wartość 30 albo 60.");

            var confidence = Str(root, "confidence") switch { "low" => Confidence.Low, "medium" => Confidence.Medium, "high" => Confidence.High, _ => (Confidence?)null };
            if (confidence is null) return (null, "Pole confidence musi mieć wartość low, medium albo high.");

            return (new BriefDraft(problem, tech, summary, questions, minutes, confidence.Value), null);
        }
    }

    static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static List<string>? Strings(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return null;
        var list = new List<string>();
        foreach (var e in v.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.String) return null;
            list.Add(e.GetString()!);
        }
        return list;
    }

    /// <summary>Models often wrap JSON in a markdown fence; that wrapper alone is tolerated.</summary>
    static string Unfence(string reply)
    {
        var t = reply.Trim();
        if (!t.StartsWith("```", StringComparison.Ordinal) || !t.EndsWith("```", StringComparison.Ordinal) || t.Length < 6) return t;
        var inner = t[3..^3];
        var nl = inner.IndexOf('\n');
        return (nl >= 0 && !inner[..nl].Contains('{') ? inner[(nl + 1)..] : inner).Trim();
    }
}
