using System.Security.Claims;

namespace Gabinety.Web.Auth;

/// <summary>
/// The ONE place that reads the identity service's access-token claims (CONTRACTS 1, docs/spikes/authservice.md): <c>sub</c>, <c>email</c> and the role claim, whose name is the long
/// WS-Federation URI (a user with one role carries a string, with several an array/repeated claim). Both the stand-in (DevIdentity) and the real authservice are read through it; when the real
/// Admin shape turns out different, this class and its tests change and nothing else.
/// </summary>
public static class ClaimMapping
{
    public const string RoleClaimUri = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
    public const string AdminRole = "Admin", SuperAdminRole = "SuperAdmin";

    public static string? Subject(IEnumerable<Claim> claims) => First(claims, "sub", ClaimTypes.NameIdentifier);

    public static string? Email(IEnumerable<Claim> claims) => First(claims, "email", ClaimTypes.Email);

    /// <summary>Every role, whatever claim name carries it, once each, in token order.</summary>
    public static IReadOnlyList<string> Roles(IEnumerable<Claim> claims) =>
        [.. claims.Where(c => c.Type is RoleClaimUri or ClaimTypes.Role or "role" or "roles").Select(c => c.Value).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal)];

    public static bool IsAdmin(IEnumerable<string> roles) => roles.Any(r => r is AdminRole or SuperAdminRole);

    public static bool IsAdmin(ClaimsPrincipal user) => IsAdmin(user.FindAll(ClaimTypes.Role).Select(c => c.Value));

    static string? First(IEnumerable<Claim> claims, params string[] types) => types.Select(t => claims.FirstOrDefault(c => c.Type == t)?.Value).FirstOrDefault(v => !string.IsNullOrEmpty(v));
}
