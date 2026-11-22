using Gabinety.Web.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Gabinety.Web.Components.Layout;

/// <summary>The flash messages the shell can show, by key (<c>?flash=signed-out</c>). An unknown key shows nothing: text from a query string is never rendered.</summary>
public static class FlashKey
{
    public const string Parameter = "flash", SignedOut = "signed-out";
    static readonly Dictionary<string, string> Messages = new() { [SignedOut] = Pl.Flash.SignedOut };

    public static string? Resolve(string uri)
    {
        var query = new Uri(uri, UriKind.RelativeOrAbsolute) is { IsAbsoluteUri: true } u ? u.Query : "";
        return QueryHelpers.ParseQuery(query).TryGetValue(Parameter, out var v) && Messages.TryGetValue(v.ToString(), out var text) ? text : null;
    }
}
