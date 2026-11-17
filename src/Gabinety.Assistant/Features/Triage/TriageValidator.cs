using System.Text.Json;
using Gabinety.Contracts.Assistant;

namespace Gabinety.Assistant.Features.Triage;

public sealed record TriageDraft(TriageVerdict Flag, IReadOnlyList<TriageReason> Reasons);

/// <summary>
/// Validates a model reply against the triage schema of CONTRACTS 6: <c>flag</c> none|hold, <c>reasons</c> from the closed set
/// spam|abuse|personal_data|off_topic|threat. The model's text is data: nothing is trusted, and the wire values are exact.
/// </summary>
public static class TriageValidator
{
    public static readonly IReadOnlyDictionary<string, TriageReason> Reasons = new Dictionary<string, TriageReason>
    {
        ["spam"] = TriageReason.Spam, ["abuse"] = TriageReason.Abuse, ["personal_data"] = TriageReason.PersonalData,
        ["off_topic"] = TriageReason.OffTopic, ["threat"] = TriageReason.Threat,
    };

    /// <summary>A draft, or a fixed error text sent back to the model for the repair attempt. It never echoes the reply or a value the model produced.</summary>
    public static (TriageDraft? Draft, string? Error) Validate(string reply)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(Unfence(reply)); }
        catch (JsonException) { return (null, "Odpowiedź nie jest poprawnym JSON-em. Zwróć jeden obiekt JSON."); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, "Odpowiedź musi być jednym obiektem JSON.");

            var flag = root.TryGetProperty("flag", out var f) && f.ValueKind == JsonValueKind.String
                ? f.GetString() switch { "none" => TriageVerdict.None, "hold" => TriageVerdict.Hold, _ => (TriageVerdict?)null }
                : null;
            if (flag is null) return (null, "Pole flag musi mieć wartość none albo hold.");

            if (!root.TryGetProperty("reasons", out var r) || r.ValueKind != JsonValueKind.Array) return (null, "Pole reasons musi być tablicą tekstów.");
            var reasons = new List<TriageReason>();
            foreach (var e in r.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.String) return (null, "Pole reasons musi być tablicą tekstów.");
                if (!Reasons.TryGetValue(e.GetString()!, out var reason)) return (null, "Pole reasons zawiera wartość spoza listy: spam, abuse, personal_data, off_topic, threat.");
                if (reasons.Contains(reason)) return (null, "Pole reasons nie może powtarzać powodów.");
                reasons.Add(reason);
            }

            if (flag == TriageVerdict.None && reasons.Count > 0) return (null, "Gdy flag to none, reasons musi być pustą tablicą.");
            if (flag == TriageVerdict.Hold && reasons.Count == 0) return (null, "Gdy flag to hold, reasons musi mieć co najmniej jeden powód.");
            return (new TriageDraft(flag.Value, reasons), null);
        }
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
