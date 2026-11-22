namespace Gabinety.Web.Auth;

/// <summary>
/// The <c>?redirect=</c> parameter (the ONE return parameter of the Next.js app too, <c>web/src/components/auth/returnTo.ts</c>): a return target is accepted only as a same-site page path.
/// Anything else, and the sign-in pages themselves (a loop), become "/".
/// </summary>
public static class ReturnTarget
{
    public const string Parameter = "redirect", LoginPath = "/logowanie", LogoutPath = "/wyloguj", OAuthCallbackPath = "/oauth/callback", RegisterPath = "/rejestracja", TwoFactorPath = "/logowanie/kod", CheckInboxPath = "/rejestracja/sprawdz-skrzynke";
    static readonly string[] AuthPages = [LoginPath, LogoutPath, OAuthCallbackPath, RegisterPath];

    public static string Safe(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || !raw.StartsWith('/') || raw.StartsWith("//") || raw.StartsWith("/\\")) return "/";
        if (raw.Any(c => c < 0x20 || c == '\\' || c == 0x7f)) return "/";
        if (!Uri.TryCreate(new Uri("http://x.invalid"), raw, out var uri) || uri.GetLeftPart(UriPartial.Authority) != "http://x.invalid") return "/";
        var path = uri.AbsolutePath;
        if (path == "/api" || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) return "/";
        if (AuthPages.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase) || path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase))) return "/";
        return raw;
    }

    /// <summary><c>/logowanie</c> with the return target attached; "/" needs no parameter.</summary>
    public static string LoginUrl(string back) => back == "/" ? LoginPath : $"{LoginPath}?{Parameter}={Uri.EscapeDataString(back)}";

    /// <summary><c>/rejestracja</c> with the return target attached.</summary>
    public static string RegisterUrl(string back) => back == "/" ? RegisterPath : $"{RegisterPath}?{Parameter}={Uri.EscapeDataString(back)}";

    /// <summary>The page that asks for the two-factor code; the target itself travels in the sealed challenge, but a link may carry it too.</summary>
    public static string TwoFactorUrl(string back) => back == "/" ? TwoFactorPath : $"{TwoFactorPath}?{Parameter}={Uri.EscapeDataString(back)}";
}
