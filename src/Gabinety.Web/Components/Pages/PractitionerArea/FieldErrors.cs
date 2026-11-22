using System.Text.RegularExpressions;
using Gabinety.Web.Api;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.PractitionerArea;

/// <summary>
/// Where a failed save puts its message. The service says WHICH fields (<see cref="ApiFailure.Fields"/>) and we say what to do about each, in Polish, from the field name; a server message is never shown.
/// The anti-leakage rule (CONTRACTS 3.5) answers <c>contact_details_not_allowed</c> WITHOUT naming a field: <see cref="LooksLikeContact"/> only decides which of the free-text fields to put that error on.
/// It never blocks a save and is not a security control.
/// </summary>
public static partial class FieldErrors
{
    [GeneratedRegex(@"[^\s@]+@[^\s@]+")] private static partial Regex Email();
    [GeneratedRegex(@"(?:\d[\s().-]?){9,}")] private static partial Regex Phone();
    [GeneratedRegex(@"\b(?:telegram|whatsapp|signal|discord|messenger|skype)\b", RegexOptions.IgnoreCase)] private static partial Regex Messenger();
    [GeneratedRegex(@"(?:^|\s)@\w{3,}")] private static partial Regex Handle();

    public static bool LooksLikeContact(string? text) => !string.IsNullOrEmpty(text) && (Email().IsMatch(text) || Phone().IsMatch(text) || Messenger().IsMatch(text) || Handle().IsMatch(text));

    public static string ContactDetails => PlPractitioner.Errors.ContactDetails;

    static readonly Dictionary<string, string> ByField = PlPractitioner.Errors.ByField.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    public static string Unknown => PlPractitioner.Errors.Unknown;

    /// <summary>Field name to message. <paramref name="freeText"/> maps the free-text fields the contact rule covers to their current values.</summary>
    public static Dictionary<string, string> From(ApiFailure failure, IReadOnlyDictionary<string, string?> freeText)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in failure.Fields ?? []) errors[f] = ByField.GetValueOrDefault(f, Unknown);
        if (failure.Code == "contact_details_not_allowed")
        {
            var hits = freeText.Where(kv => LooksLikeContact(kv.Value)).Select(kv => kv.Key).ToList();
            foreach (var name in hits.Count > 0 ? hits : [.. freeText.Keys]) errors[name] = ContactDetails;
        }
        return errors;
    }

    /// <summary>The line for a failure that belongs to no field.</summary>
    public static string General(ApiFailure failure, string fallback) => failure switch
    {
        { Status: 0 } => PlPractitioner.Common.Network,
        { Status: 403 } => PlPractitioner.Common.Forbidden,
        { Status: 503 } => PlPractitioner.Common.Unavailable,
        _ => fallback,
    };
}
