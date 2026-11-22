using Microsoft.AspNetCore.Http;

namespace Gabinety.Web.Components.Pages.PractitionerArea;

/// <summary>
/// What a posted form said, read by field name. The static pages of this area read the posted fields themselves (<c>HttpContext.Request.Form</c>) because a list of checkboxes or a draft with a
/// variable number of rows does not map to one model; this keeps that in one small, unit-tested place. Values are trimmed here and nowhere else; a missing field is null.
/// </summary>
public sealed class FormValues(IFormCollection form)
{
    public static FormValues Empty { get; } = new(FormCollection.Empty);

    /// <summary>The field, trimmed; null when it was not posted.</summary>
    public string? Get(string name) => form.TryGetValue(name, out var v) ? v.ToString().Trim() : null;

    /// <summary>The field, trimmed, or "" when it was not posted.</summary>
    public string Text(string name) => Get(name) ?? "";

    /// <summary>Every value posted under a name (a checkbox group), without blanks or duplicates, at most <paramref name="max"/>.</summary>
    public IReadOnlyList<string> All(string name, int max = 32) => form.TryGetValue(name, out var v) ? [.. v.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).Take(max)] : [];

    public bool Has(string name) => form.ContainsKey(name);
}
