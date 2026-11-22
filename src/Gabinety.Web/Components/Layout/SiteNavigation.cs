using Gabinety.Web.Auth;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Layout;

public sealed record NavItem(string Href, string Label);

/// <summary>
/// The main navigation, in ORDER: a product decision, pinned by a test (BLAZOR-WEB-APP section 4). The practitioner list is first, as in the Next.js header; the account links follow.
/// The entries of pages that are not ported yet (<c>/jak-to-dziala</c>, <c>/zasady-rankingu</c>, <c>/cennik</c>, registration) are added with their pages, in the position they have in
/// <c>web/src/components/layout/Header.tsx</c>, so no link in this host leads to a 404.
/// </summary>
public static class SiteNavigation
{
    public static readonly NavItem Practitioners = new("/programisci", Pl.Nav.Search);

    public static IReadOnlyList<NavItem> Items(bool signedIn) => signedIn
        ? [Practitioners, new NavItem("/moje/rezerwacje", Pl.Nav.MyBookings), new NavItem(ReturnTarget.LogoutPath, Pl.Nav.SignOut)]
        : [Practitioners, new NavItem(ReturnTarget.LoginPath, Pl.Nav.SignIn)];
}
