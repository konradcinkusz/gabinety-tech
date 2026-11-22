using System.Net.Mail;

namespace Gabinety.DevIdentity;

public sealed record RegisterRequest(string? Email, string? Password, string? AcceptedTermsVersion, string? AcceptedPrivacyVersion, string? Locale);
public sealed record LoginRequest(string? Email, string? Password);
public sealed record RefreshRequest(string? RefreshToken);
public sealed record RecordConsentRequest(bool? AcceptedTerms, bool? AcceptedPrivacy);
public sealed record ExchangeRequest(string? Code);
public sealed record DeleteAccountRequest(string? Password, string? Confirmation);

public static class Endpoints
{
    private const string Version = "2026-01-01"; // authservice's default ConsentVersions (appsettings.json), the value the integration job's real image answers

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
                else errors.AddRange(PasswordRules(r.Password));
                if (errors.Count > 0) return Results.BadRequest(new { errors });
                // authservice: the registration must accept exactly the versions it requires.
                if (r.AcceptedTermsVersion != Version || r.AcceptedPrivacyVersion != Version)
                    return Results.BadRequest(new { errors = new[] { "You must accept the current Terms of Use and Privacy Policy to register." } });
                if (!store.TryAdd(r.Email!.Trim(), r.Password!, [], null, out var user))
                    return Results.BadRequest(new { errors = new[] { $"Email '{r.Email!.Trim()}' is already taken." } }); // the real wording
                store.Accept(user, Version, Version);
                return Results.Ok(TokenResponse(user, store.IssueRefresh(user))); // no email verification step, so 200 and never 202
            });

            app.MapPost($"{prefix}/login", (LoginRequest r) =>
            {
                if (r.Email is null || r.Password is null) return Results.BadRequest(new { title = "One or more validation errors occurred.", status = 400 });
                return store.Authenticate(r.Email.Trim(), r.Password) switch
                {
                    (SignInStatus.Succeeded, { } user) => Results.Ok(TokenResponse(user, store.IssueRefresh(user))), // never the two-factor shape, never 403
                    (SignInStatus.LockedOut, { } user) => Results.Json(new { error = "Account is temporarily locked after too many failed sign-in attempts.", lockedOut = true, lockoutEnd = user.LockoutEnd }, statusCode: StatusCodes.Status401Unauthorized),
                    _ => Results.Json(new { error = "Invalid email or password" }, statusCode: StatusCodes.Status401Unauthorized),
                };
            });

            app.MapPost($"{prefix}/refresh", (RefreshRequest r) =>
                string.IsNullOrWhiteSpace(r.RefreshToken) ? Results.BadRequest(new { error = "Refresh token is required" })
                : store.Rotate(r.RefreshToken) is { } rotated ? Results.Ok(TokenResponse(rotated.User, rotated.RefreshToken))
                : Results.Json(new { error = "Invalid or expired refresh token" }, statusCode: StatusCodes.Status401Unauthorized));

            app.MapPost($"{prefix}/logout", async (HttpRequest request) =>
            {
                if (await CallerAsync(request) is not { } user) return Results.Unauthorized();
                store.RevokeAll(user.Id);
                return Results.Ok(new { message = "Logged out successfully" });
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

            // What the account page (/konto) reads and does. Shapes are those read in authservice's AuthController (docs/spikes/authservice.md section 3).
            object Item(DevUser u, (string Version, DateTimeOffset At)? accepted) =>
                new { requiredVersion = Version, acceptedVersion = accepted?.Version, acceptedAt = accepted?.At, accepted = accepted?.Version == Version };

            object ConsentStatus(DevUser u) => new
            {
                terms = Item(u, u.Terms),
                privacy = Item(u, u.Privacy),
                cookies = new { requiredVersion = Version, acceptedVersion = (string?)null, acceptedAt = (DateTimeOffset?)null, accepted = false },
                requiresConsent = u.Terms?.Version != Version || u.Privacy?.Version != Version,
            };

            app.MapGet($"{prefix}/consents", async (HttpRequest request) =>
                await CallerAsync(request) is not { } u ? Results.Unauthorized() : Results.Ok(ConsentStatus(u)));

            app.MapPost($"{prefix}/consents", async (HttpRequest request, RecordConsentRequest r) =>
            {
                if (await CallerAsync(request) is not { } u) return Results.Unauthorized();
                store.Accept(u, r.AcceptedTerms == true ? Version : null, r.AcceptedPrivacy == true ? Version : null);
                return Results.Ok(ConsentStatus(u));
            });

            app.MapGet($"{prefix}/export", async (HttpRequest request) =>
                await CallerAsync(request) is not { } u
                    ? Results.Unauthorized()
                    : Results.Ok(new
                    {
                        exportedAt = DateTimeOffset.UtcNow,
                        profile = new { id = u.Id, email = u.Email, userName = u.Email, createdAt = u.CreatedAt, lastLoginAt = u.LastLoginAt, emailConfirmed = true, twoFactorEnabled = false, hasPassword = true, isDeleted = false, roles = u.Roles },
                        externalLogins = Array.Empty<object>(),
                        consents = new[] { u.Terms is { } t ? new { type = "Terms", version = t.Version, accepted = true, acceptedAt = t.At } : null, u.Privacy is { } p ? new { type = "Privacy", version = p.Version, accepted = true, acceptedAt = p.At } : null }.Where(c => c is not null),
                        organizations = Array.Empty<object>(),
                        invitationsReceived = Array.Empty<object>(),
                        invitationsSent = Array.Empty<object>(),
                        sessions = Array.Empty<object>(),
                    }));

            // A soft delete: the account can no longer sign in; the data stays for a retention period (authservice: ApplicationUser.DefaultRetentionDays).
            app.MapDelete($"{prefix}/account", async (HttpRequest request, [Microsoft.AspNetCore.Mvc.FromBody] DeleteAccountRequest r) =>
            {
                if (await CallerAsync(request) is not { } u) return Results.Unauthorized();
                if (r.Confirmation != "DELETE") return Results.BadRequest(new { error = "Confirmation text must be 'DELETE'" });
                if (string.IsNullOrEmpty(r.Password)) return Results.BadRequest(new { error = "Password is required" });
                if (!store.PasswordMatches(u, r.Password)) return Results.BadRequest(new { error = "Invalid password" });
                store.SoftDelete(u);
                return Results.Ok(new { message = "Account deleted successfully" });
            });
        }

        // No provider is configured in the stand-in, as in authservice without OAuth credentials.
        foreach (var prefix in new[] { "/api/v1/external-auth", "/api/external-auth" })
        {
            app.MapGet($"{prefix}/providers", () => Results.Ok(new { providers = Array.Empty<object>() }));
            // Start of a provider sign-in: authservice answers 400 when the provider is not configured (docs/spikes/authservice.md section 3).
            app.MapGet($"{prefix}/login", () => Results.BadRequest(new { error = "Provider is not configured." }));
            // Exchange of the single-use code: no code is ever issued here, so every code is a bad one (401). The web sends `{code}`.
            app.MapPost($"{prefix}/exchange", (ExchangeRequest r) =>
                string.IsNullOrWhiteSpace(r.Code) ? Results.BadRequest(new { error = "Code is required" })
                : Results.Json(new { error = "Invalid or expired code" }, statusCode: StatusCodes.Status401Unauthorized));
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

    // ASP.NET Identity options of authservice (Program.cs): a digit, a lower-case and an upper-case letter and a non-alphanumeric character. Messages are Identity's.
    private static IEnumerable<string> PasswordRules(string p)
    {
        if (!p.Any(c => !char.IsAsciiLetterOrDigit(c))) yield return "Passwords must have at least one non alphanumeric character.";
        if (!p.Any(char.IsAsciiDigit)) yield return "Passwords must have at least one digit ('0'-'9').";
        if (!p.Any(char.IsAsciiLetterLower)) yield return "Passwords must have at least one lowercase ('a'-'z').";
        if (!p.Any(char.IsAsciiLetterUpper)) yield return "Passwords must have at least one uppercase ('A'-'Z').";
    }

    private static bool IsEmail(string? s) => !string.IsNullOrWhiteSpace(s) && MailAddress.TryCreate(s.Trim(), out var a) && a.Address == s.Trim();
}
