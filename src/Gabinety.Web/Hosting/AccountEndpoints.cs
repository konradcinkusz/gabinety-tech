using Gabinety.Web.Api;
using Gabinety.Web.Auth;

namespace Gabinety.Web.Hosting;

/// <summary>
/// <c>GET /konto/export</c> (port of <c>web/src/app/(auth)/konto/export/route.ts</c>): the user's own data as a file. Authenticated (the deny-by-default policy: a visitor is sent to sign-in), always
/// <c>no-store</c>, the identity service's export passed through. It is not a component, so it has no authentication state: it mints the access token from the session itself.
/// </summary>
public static class AccountEndpoints
{
    public const string ExportPath = "/konto/export", FileName = "gabinety-tech-moje-dane.json";

    public static void MapAccountEndpoints(this IEndpointRouteBuilder app) => app.MapGet(ExportPath, Export).RequireAuthorization();

    static async Task<IResult> Export(HttpContext http, AccountClient account, UserTokens tokens, ISessionSeed seed)
    {
        http.Response.Headers.CacheControl = "no-store";
        var token = http.User.FindFirst(SessionCookie.SessionClaim)?.Value is { } sid ? await tokens.ForSessionAsync(sid, await seed.RefreshTokenAsync(), http.RequestAborted) : null;
        if (token is null) return Results.StatusCode(StatusCodes.Status401Unauthorized);
        var result = await account.ExportAsync(token, http.RequestAborted);
        if (!result.IsOk) return Results.StatusCode(result.Failure!.Status is 401 or 503 ? result.Failure.Status : StatusCodes.Status502BadGateway);
        http.Response.Headers.ContentDisposition = $"attachment; filename=\"{FileName}\"";
        return Results.File(result.Value!, "application/json; charset=utf-8");
    }
}
