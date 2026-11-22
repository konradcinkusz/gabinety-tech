namespace Gabinety.Web.Components.Pages.Admin;

/// <summary>
/// The name of a form the page rendered per row: <c>verb-id</c> (<c>approve-0b9e...</c>). Blazor dispatches a posted form by its name, so the name carries what the handler needs, and the
/// handler reads the rest from the posted fields. An id is only ever a well-formed GUID: anything else is not a form of ours.
/// </summary>
public static class PostedForm
{
    public static string Name(string verb, Guid id) => $"{verb}-{id:D}";

    public static (string Verb, Guid Id)? Parse(string? name) =>
        name is { Length: > 37 } && name[^37] == '-' && Guid.TryParseExact(name[^36..], "D", out var id) ? (name[..^37], id) : null;
}
