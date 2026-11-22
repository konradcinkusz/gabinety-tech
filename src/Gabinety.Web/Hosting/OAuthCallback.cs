using Gabinety.Web.Auth;

namespace Gabinety.Web.Hosting;

/// <summary>
/// <c>/oauth/callback</c> (PR #118): where authservice sends the browser back after the GitHub leg. The route is ANONYMOUS and is not caught by the sign-in redirect (the visitor is not signed in
/// yet, and a bounce through <c>/logowanie</c> would lose the <c>code</c> and <c>state</c> query), so the route table and the guard are ready for it. The exchange itself (state bound to the browser by a
/// cookie, <c>POST /api/v1/external-auth/exchange</c>, the session) is ported by a later task; until then every call ends where a failed GitHub sign-in ends in the Next.js app:
/// <c>/logowanie?error=github_failed</c>, with the state discarded. It reads no parameter and sets no cookie.
/// </summary>
public static class OAuthCallbackEndpoint
{
    public static IEndpointConventionBuilder MapOAuthCallback(this IEndpointRouteBuilder app) =>
        app.MapGet(ReturnTarget.OAuthCallbackPath, () => Results.Redirect($"{ReturnTarget.LoginPath}?error=github_failed")).AllowAnonymous();
}
