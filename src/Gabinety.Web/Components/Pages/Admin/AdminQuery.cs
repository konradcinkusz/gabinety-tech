using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace Gabinety.Web.Components.Pages.Admin;

/// <summary>The query of an admin page: a value is accepted from a closed set or a strict shape, and anything else is dropped (never forwarded to a backend, never rendered).</summary>
public static partial class AdminQuery
{
    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,200}$")] private static partial Regex CursorShape();

    /// <summary>Cursors are opaque base64url; anything else is dropped rather than forwarded.</summary>
    public static string? Cursor(string? raw) => raw is not null && CursorShape().IsMatch(raw) ? raw : null;

    public static string Choice(string? raw, IReadOnlyCollection<string> allowed, string fallback) => raw is not null && allowed.Contains(raw) ? raw : fallback;

    /// <summary>The count on a status chip, from the first page the page already loaded: exact when there is no next page, "N+" when there is one; null (nothing shown) on a later page or when the load failed.</summary>
    public static string? ChipCount(bool loaded, string? cursor, string? next, int count) =>
        !loaded || cursor is not null ? null : next is null ? count.ToString() : count + "+";

    /// <summary>A path with the given parameters (empty ones left out): the address a page returns to after an action, with the flash key that says what happened.</summary>
    public static string Url(string path, params (string Name, string? Value)[] parameters) =>
        parameters.Where(p => !string.IsNullOrEmpty(p.Value)).Aggregate(path, (url, p) => QueryHelpers.AddQueryString(url, p.Name, p.Value!));
}
