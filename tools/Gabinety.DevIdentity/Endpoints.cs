using System.Net.Mail;

namespace Gabinety.DevIdentity;

public sealed record RegisterRequest(string? Email, string? Password, string? AcceptedTermsVersion, string? AcceptedPrivacyVersion, string? Locale);
public sealed record LoginRequest(string? Email, string? Password);
public sealed record RefreshRequest(string? RefreshToken);

public static class Endpoints
{
    private const string Version = "dev-1"; // the real service has configured consent versions; unknown, so one fixed string

    public static void MapDevIdentityEndpoints(this WebApplication app, string? publicBaseUrl)
    {
        var store = app.Services.GetRequiredService<IdentityStore>();
        var tokens = app.Services.GetRequiredService<TokenIssuer>();

        object TokenResponse(DevUser user, string refresh) => new
        {
            accessToken = tokens.CreateAccessToken(user),
            refreshToken = refresh,
            expiresIn = (int)TokenIssuer.AccessLifetime.TotalSeconds,
            tokenType = "Bearer",
        };

        async Task<DevUser?> CallerAsync(HttpRequest request)
        {
            var header = request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
            var sub = await tokens.ValidateAsync(header["Bearer ".Length..].Trim());
            return sub is null ? null : store.Find(sub);
        }

        // Both the versioned and the unversioned alias exist in authservice.
        foreach (var prefix in new[] { "/api/v1/auth", "/api/auth" })
        {
            app.MapGet($"{prefix}/consents/versions", () => Results.Ok(new { terms = Version, privacy = Version, cookies = Version }));

            app.MapPost($"{prefix}/register", (RegisterRequest r) =>
            {
                var errors = new List<string>();
                if (!IsEmail(r.Email)) errors.Add("A valid email is required.");
                if (r.Password is not { Length: >= 8 and <= 100 }) errors.Add("The password must be 8 to 100 characters.");
                if (string.IsNullOrWhiteSpace(r.AcceptedTermsVersion) || string.IsNullOrWhiteSpace(r.AcceptedPrivacyVersion)) errors.Add("The terms and privacy versions must be accepted.");
                if (errors.Count > 0) return Results.BadRequest(new { errors });
                if (!store.TryAdd(r.Email!.Trim(), r.Password!, [], null, out var user))
                    return Results.BadRequest(new { errors = new[] { "The email is already registered." } }); // the real wording and shape for this case are unknown
                return Results.Ok(TokenResponse(user, store.IssueRefresh(user))); // no email verification step, so 200 and never 202
            });

            app.MapPost($"{prefix}/login", (LoginRequest r) =>
                r.Email is null || r.Password is null || store.Authenticate(r.Email.Trim(), r.Password) is not { } user
                    ? Results.Json(new { message = "Invalid email or password." }, statusCode: StatusCodes.Status401Unauthorized)
                    : Results.Ok(TokenResponse(user, store.IssueRefresh(user)))); // never the two-factor shape, never 403

            app.MapPost($"{prefix}/refresh", (RefreshRequest r) =>
                r.RefreshToken is not null && store.Rotate(r.RefreshToken) is var rotated and not null
                    ? Results.Ok(TokenResponse(rotated.Value.User, rotated.Value.RefreshToken))
                    : Results.Json(new { message = "Invalid refresh token." }, statusCode: StatusCodes.Status401Unauthorized));

            app.MapPost($"{prefix}/logout", async (HttpRequest request) =>
            {
                if (await CallerAsync(request) is not { } user) return Results.Unauthorized();
                store.RevokeAll(user.Id);
                return Results.Ok(new { message = "Logged out." });
            });

            app.MapGet($"{prefix}/me", async (HttpRequest request) =>
                await CallerAsync(request) is not { } u
                    ? Results.Unauthorized()
                    : Results.Ok(new
                    {
                        id = u.Id,
                        email = u.Email,
                        userName = u.Email, // unknown in the real service; the e-mail is the minimal choice
                        profileImageUrl = (string?)null,
                        createdAt = u.CreatedAt,
                        lastLoginAt = u.LastLoginAt,
                        organizations = Array.Empty<object>(),
                        hasPassword = true,
                        requiresConsent = false,
                        emailConfirmed = true,
                        twoFactorEnabled = false,
                    }));
        }

        app.MapGet("/.well-known/jwks.json", (HttpResponse response) =>
        {
            response.Headers.CacheControl = "public, max-age=300";
            return Results.Content(tokens.JwksJson(), "application/json");
        });

        app.MapGet("/.well-known/openid-configuration", (HttpRequest request) =>
        {
            var origin = publicBaseUrl?.TrimEnd('/') ?? $"{request.Scheme}://{request.Host}";
            return Results.Ok(new { issuer = tokens.Issuer, jwks_uri = origin + "/.well-known/jwks.json" }); // the two fields the spike names
        });
    }

    private static bool IsEmail(string? s) => !string.IsNullOrWhiteSpace(s) && MailAddress.TryCreate(s.Trim(), out var a) && a.Address == s.Trim();
}
