using System.Security.Claims;
using Gabinety.Web.Auth;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Layout;

/// <summary>A link of the header. <see cref="Section"/> is the path prefix that makes it the current section (<c>aria-current="page"</c>); the href itself when empty.</summary>
public sealed record NavItem(string Href, string Label, string? Section = null)
{
    public bool IsCurrent(string path)
    {
        var section = Section ?? Href;
        var current = path.Length > 1 ? path.TrimEnd('/') : path;
        return current.Equals(section, StringComparison.OrdinalIgnoreCase) || current.StartsWith(section + "/", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>What the header shows for one visitor. The two buttons of a visitor and the account menu of a signed-in user are the "account area"; <see cref="Links"/> are the main navigation.</summary>
public sealed record HeaderModel(IReadOnlyList<NavItem> Links, NavItem? SignIn, NavItem? Register, IReadOnlyList<NavItem> AccountMenu, string? Email, string CurrentPath)
{
    public bool SignedIn => SignIn is null;

    /// <summary>Everything in one flat list: what the phone menu (<c>&lt;details&gt;</c>) holds.</summary>
    public IReadOnlyList<NavItem> Flat => [.. Links, .. (SignIn is null ? [] : new[] { SignIn }), .. (Register is null ? [] : new[] { Register }), .. AccountMenu];
}

/// <summary>
/// The main navigation, in ORDER: a product decision, pinned by a test (DESIGN-CONTRACT 5.1). The practitioner list is first; the other public pages (<c>/jak-to-dziala</c>,
/// <c>/zasady-rankingu</c>, <c>/cennik</c>) are added to <see cref="Primary"/> by the sessions that port them, in the position they have in the contract, so no link in this host leads to a 404.
/// The account area is by session: a visitor has "Zaloguj się" and "Załóż konto"; a signed-in user has "Moje rezerwacje", the practitioner panel and the account menu ("Konto", "Wyloguj się");
/// an administrator also "Administracja". The role comes from the verified claims of the session (<see cref="ClaimMapping"/>), the page is the boundary anyway.
/// </summary>
public static class SiteNavigation
{
    public static readonly NavItem Practitioners = new("/programisci", Pl.Nav.Search);

    /// <summary>Public links of everybody, in order. Ported pages append their entry here (additive: one line each).</summary>
    public static IReadOnlyList<NavItem> Primary { get; } = [Practitioners];

    public static readonly NavItem SignIn = new(ReturnTarget.LoginPath, Pl.Nav.SignIn), Register = new(ReturnTarget.RegisterPath, Pl.Nav.Register);
    public static readonly NavItem MyBookings = new("/moje/rezerwacje", Pl.Nav.MyBookings), Account = new("/konto", Pl.Nav.Account), SignOut = new(ReturnTarget.LogoutPath, Pl.Nav.SignOut);
    public static readonly NavItem PractitionerArea = new("/gabinet-programisty", Pl.Nav.PractitionerArea), Admin = new("/admin/weryfikacja", Pl.Nav.Admin, "/admin");

    public static HeaderModel For(ClaimsPrincipal? user, string currentPath)
    {
        if (user?.Identity?.IsAuthenticated != true) return new HeaderModel(Primary, SignIn, Register, [], null, currentPath);
        List<NavItem> links = [.. Primary, MyBookings, PractitionerArea];
        if (ClaimMapping.IsAdmin(user)) links.Add(Admin);
        return new HeaderModel(links, null, null, [Account, SignOut], ClaimMapping.Email(user.Claims), currentPath);
    }
}
