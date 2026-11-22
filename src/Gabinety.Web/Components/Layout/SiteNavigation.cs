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

/// <summary>
/// What the header shows for one visitor. <see cref="Links"/> is the primary navigation; the account area is the two buttons of a visitor or, for a signed-in user, the account menu
/// (<see cref="AccountMenu"/>: bookings, account, the role links, sign-out). The phone menu (<see cref="Flat"/>) holds the same links in one list.
/// </summary>
public sealed record HeaderModel(IReadOnlyList<NavItem> Links, NavItem? SignIn, NavItem? Register, IReadOnlyList<NavItem> AccountMenu, string? Email, string CurrentPath)
{
    public bool SignedIn => SignIn is null;

    /// <summary>The two letters of the account's avatar; empty when there is no e-mail to take them from.</summary>
    public string Initials => AccountInitials.From(Email);

    /// <summary>Everything in one flat list: what the phone menu (<c>&lt;details&gt;</c>) holds.</summary>
    public IReadOnlyList<NavItem> Flat => [.. Links, .. (SignIn is null ? [] : new[] { SignIn }), .. (Register is null ? [] : new[] { Register }), .. AccountMenu];
}

/// <summary>What the footer shows: the product links, one call to action for programmers (the practitioner panel or the way to become one) and the legal links.</summary>
public sealed record FooterModel(IReadOnlyList<NavItem> Product, NavItem Practitioner, IReadOnlyList<NavItem> Legal);

/// <summary>
/// The navigation of the header and the footer (DESIGN-CONTRACT 5.1). The ORDER of the primary links is a product decision pinned by a test: <c>Programiści</c>, <c>Jak to działa</c>,
/// <c>Cennik</c>. <c>Zasady rankingu</c> lives in the footer. The role links are in the account menu, and <c>Panel programisty</c> only for an account that has a practitioner profile
/// (D-2); everybody else signed in is offered <c>Zostań programistą</c>. The role comes from the verified claims (<see cref="ClaimMapping"/>); the practitioner flag is the caller's
/// (see <c>PractitionerProfileCheck</c>), because no claim carries it. The page is the boundary anyway.
/// </summary>
public static class SiteNavigation
{
    public static readonly NavItem Practitioners = new("/programisci", Pl.Nav.Search);
    public static readonly NavItem HowItWorks = new("/jak-to-dziala", Pl.Nav.HowItWorks);
    public static readonly NavItem Pricing = new("/cennik", Pl.Nav.Pricing);
    public static readonly NavItem RankingRules = new("/zasady-rankingu", Pl.Nav.RankingRules);

    /// <summary>The primary navigation of everybody, in order.</summary>
    public static IReadOnlyList<NavItem> Primary { get; } = [Practitioners, HowItWorks, Pricing];

    /// <summary>The footer's product column: the primary links and the ranking rules.</summary>
    public static IReadOnlyList<NavItem> Product { get; } = [Practitioners, HowItWorks, Pricing, RankingRules];

    public static readonly NavItem SignIn = new(ReturnTarget.LoginPath, Pl.Nav.SignIn), Register = new(ReturnTarget.RegisterPath, Pl.Nav.Register);
    public static readonly NavItem MyBookings = new("/moje/rezerwacje", Pl.Nav.MyBookings), Account = new("/konto", Pl.Nav.Account), SignOut = new(ReturnTarget.LogoutPath, Pl.Nav.SignOut);
    public static readonly NavItem PractitionerArea = new("/gabinet-programisty", Pl.Nav.PractitionerArea), BecomePractitioner = new("/gabinet-programisty", Pl.Nav.BecomePractitioner);
    public static readonly NavItem Admin = new("/admin/weryfikacja", Pl.Nav.Admin, "/admin");
    public static readonly NavItem VisitorBecomePractitioner = new("/rejestracja", Pl.Nav.BecomePractitioner);
    public static readonly NavItem Terms = new("/regulamin", Pl.Site.Terms), Privacy = new("/polityka-prywatnosci", Pl.Site.Privacy);

    /// <param name="back">The page the visitor is on, already filtered by <see cref="ReturnTarget.Safe"/>: the sign-in and registration links bring them back to it.</param>
    /// <param name="practitioner">The signed-in account has a practitioner profile (it decides between the practitioner panel and the way to become one).</param>
    public static HeaderModel For(ClaimsPrincipal? user, string currentPath, string back = "/", bool practitioner = false)
    {
        if (user?.Identity?.IsAuthenticated != true) return new HeaderModel(Primary, SignIn with { Href = ReturnTarget.LoginUrl(back) }, Register with { Href = ReturnTarget.RegisterUrl(back) }, [], null, currentPath);
        List<NavItem> menu = [MyBookings, Account, practitioner ? PractitionerArea : BecomePractitioner];
        if (ClaimMapping.IsAdmin(user)) menu.Add(Admin);
        menu.Add(SignOut);
        return new HeaderModel(Primary, null, null, menu, ClaimMapping.Email(user.Claims), currentPath);
    }

    /// <summary>The footer of the same visitor: a visitor is sent to registration, a client to the way to become a programmer, a practitioner to the panel (D-2).</summary>
    public static FooterModel Footer(ClaimsPrincipal? user, bool practitioner)
    {
        var practitionerLink = user?.Identity?.IsAuthenticated != true ? VisitorBecomePractitioner : practitioner ? PractitionerArea : BecomePractitioner;
        return new FooterModel(Product, practitionerLink, [Terms, Privacy]);
    }
}

/// <summary>
/// The letters of the account's avatar, from the e-mail alone (no name is in the claims): the first letters of the first two parts of the local part, split at <c>.</c>, <c>-</c>,
/// <c>_</c> or <c>+</c> (<c>demo-client@…</c> gives <c>DC</c>), or the first two letters of a single part. Empty when there is nothing to take them from.
/// </summary>
public static class AccountInitials
{
    static readonly char[] Separators = ['.', '-', '_', '+'];

    public static string From(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "";
        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (at == 0) return "";
        var local = at > 0 ? email[..at] : email;
        var parts = local.Split(Separators, StringSplitOptions.RemoveEmptyEntries).Select(p => new string([.. p.Where(char.IsLetterOrDigit)])).Where(p => p.Length > 0).ToList();
        return parts.Count switch
        {
            0 => "",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant(),
        };
    }
}
