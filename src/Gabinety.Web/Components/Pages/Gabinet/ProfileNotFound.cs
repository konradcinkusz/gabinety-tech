namespace Gabinety.Web.Components.Pages.Gabinet;

/// <summary>Which not-found page a path deserves: a profile address says "programmer", everything else says "page" (F-09). Plain C#.</summary>
public static class ProfileNotFound
{
    public static bool Matches(string? path) => path is not null && path.StartsWith("/gabinet/", StringComparison.OrdinalIgnoreCase) && path.Length > "/gabinet/".Length;
}
