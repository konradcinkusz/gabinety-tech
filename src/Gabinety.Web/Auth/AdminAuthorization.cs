using Microsoft.AspNetCore.Authorization;

namespace Gabinety.Web.Auth;

/// <summary>
/// The administrator area (<c>/admin/*</c>): one named policy, so a page says <c>[Authorize(Policy = AdminAuthorization.Policy)]</c> and the route audit shows it. A signed-in user who is not an administrator is
/// REFUSED (403 with the "Brak dostępu" page, F-10), not redirected; a visitor is sent to sign-in with the page to come back to (the cookie's challenge). The role is read by <see cref="ClaimMapping"/>,
/// the one place that knows the claim shape. This is UX and defence in depth: every service checks the role again with the user's own token.
/// </summary>
public static class AdminAuthorization
{
    public const string Policy = "Admin";

    public static AuthorizationBuilder AddAdminPolicy(this AuthorizationBuilder builder) =>
        builder.AddPolicy(Policy, p => p.RequireAuthenticatedUser().RequireAssertion(context => ClaimMapping.IsAdmin(context.User)));
}
