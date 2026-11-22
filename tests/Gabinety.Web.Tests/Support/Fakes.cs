using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gabinety.Web.Api;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gabinety.Web.Tests;

/// <summary>What a backend saw of a request, copied while the request is alive (the message is disposed after the call).</summary>
public sealed record Recorded(HttpMethod Method, string PathAndQuery, string? Authorization, string? ClientAddress, string? EdgeKey, string? Body);

/// <summary>A backend that answers from a function and remembers every request. No network: this is the primary handler of the typed clients in every test.</summary>
public sealed class FakeBackend : HttpMessageHandler
{
    readonly List<Recorded> _requests = [];
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
    public IReadOnlyList<Recorded> Requests { get { lock (_requests) return [.. _requests]; } }
    public int Calls => Requests.Count;
    /// <summary>The calls except the header's own practitioner check (GET /api/v1/me), which is a shell read and not a page's call to its backend.</summary>
    public int CallsExceptShellCheck => Requests.Count(r => r.PathAndQuery != "/api/v1/me");
    /// <summary>How long the backend takes before it answers, honouring cancellation (a timeout of the client's pipeline ends the wait).</summary>
    public TimeSpan Delay { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? One(string name) => request.Headers.TryGetValues(name, out var v) ? string.Join(",", v) : null;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_requests)
            _requests.Add(new Recorded(request.Method, request.RequestUri!.PathAndQuery, One("Authorization"), One("X-Client-Address"), One("X-Edge-Key"), body));
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
        return Respond(request);
    }

    public static HttpResponseMessage Json(object? body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body, ApiClient.Json), Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Raw(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>An RFC 9457 body (CONTRACTS 0). The title and detail are deliberately text a page must never show.</summary>
    public static HttpResponseMessage Problem(HttpStatusCode status, string code) =>
        new(status) { Content = new StringContent($"{{\"type\":\"urn:gabinety:error:{code}\",\"title\":\"SERVER-TITLE-MUST-NOT-APPEAR\",\"status\":{(int)status},\"detail\":\"SERVER-DETAIL-MUST-NOT-APPEAR\",\"code\":\"{code}\"}}", Encoding.UTF8, "application/problem+json") };

    public static Func<HttpRequestMessage, HttpResponseMessage> Throw(Exception e) => _ => throw e;
}

public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
    public static TestClock Default() => new(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero));
}

/// <summary>An identity provider in memory: a real RSA key, real RS256 tokens with the claims of the spike (<c>sub</c>, <c>email</c>, the role claim URI), a JWKS and the login, refresh and logout endpoints.</summary>
public sealed class FakeIdentity
{
    public const string RoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role", Password = "Demo-Dev-Passw0rd!";
    public const string DefaultIssuer = "AuthService", DefaultAudience = "AuthService";
    RSA _rsa = RSA.Create(2048);
    readonly object _gate = new();
    int _counter;

    public FakeIdentity(TimeProvider? clock = null)
    {
        Clock = clock ?? TimeProvider.System;
        Key = Keys(_rsa, "kid-1");
        Backend = new FakeBackend { Respond = Handle };
    }

    public TimeProvider Clock { get; }
    public RsaSecurityKey Key { get; private set; }
    public FakeBackend Backend { get; }
    public string KeyId { get; set; } = "kid-1";
    public int RefreshCalls { get; private set; }
    public bool RefuseRefresh { get; set; }
    /// <summary>Sign-in behaviours authservice has and DevIdentity does not show: <c>LoginStatus</c> 429 (throttled), a locked account, a second factor, a registration that needs e-mail confirmation.</summary>
    public int LoginStatus { get; set; }
    public bool LockedOut { get; set; }
    public bool RequireTwoFactor { get; set; }
    public const string TwoFactorCode = "123456", RecoveryCode = "RECOVERY-0001", ConsentVersion = "2026-01-01";
    public bool ConsentVersionsDown { get; set; }
    public int RegisterStatus { get; set; }
    /// <summary>The recovery code is single use, like authservice's.</summary>
    bool _recoveryUsed;
    /// <summary>A status the refresh endpoint answers with instead of working (429, 503): the identity service throttling or failing.</summary>
    public int RefreshStatus { get; set; }
    /// <summary>The refresh tokens presented to <c>/refresh</c>, in order. A rotating service spends each once: a repeat here is a replay, which authservice answers by revoking the family.</summary>
    public List<string> SpentRefreshTokens { get; } = [];
    /// <summary>Signs the ACCESS tokens of refreshes with this key instead of the published one (an attacker's token).</summary>
    public RSA? AccessTokenSigner { get; set; }
    public bool Down { get; set; }
    /// <summary>Set by <c>/logout</c>: authservice revokes every refresh token of the user.</summary>
    public bool Revoked { get; private set; }
    public TimeSpan AccessLifetime { get; set; } = TimeSpan.FromHours(1);
    public string[] Roles { get; set; } = [];

    static RsaSecurityKey Keys(RSA rsa, string kid) => new(rsa) { KeyId = kid };

    public static RSA NewKey() => RSA.Create(2048);

    public string Issue(string sub = "user-1", string email = "demo-client@demo.example", string[]? roles = null, TimeSpan? lifetime = null, RSA? signWith = null,
        string? issuer = DefaultIssuer, string? audience = DefaultAudience, string? kid = null, string algorithm = SecurityAlgorithms.RsaSha256)
    {
        var claims = new Dictionary<string, object> { ["sub"] = sub, ["email"] = email, ["jti"] = Guid.NewGuid().ToString() };
        roles ??= Roles;
        if (roles.Length == 1) claims[RoleClaim] = roles[0]; else if (roles.Length > 1) claims[RoleClaim] = roles;
        var key = signWith is null ? Key : new RsaSecurityKey(signWith) { KeyId = kid ?? KeyId };
        if (kid is not null && signWith is null) key = new RsaSecurityKey(_rsa) { KeyId = kid };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer, Audience = audience, Claims = claims,
            Expires = (Clock.GetUtcNow() + (lifetime ?? AccessLifetime)).UtcDateTime,
            SigningCredentials = new SigningCredentials(key, algorithm),
        });
    }

    public string Jwks()
    {
        var p = _rsa.ExportParameters(false);
        return JsonSerializer.Serialize(new { keys = new[] { new { kty = "RSA", use = "sig", alg = "RS256", kid = KeyId, n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent) } } });
    }

    /// <summary>The key set rotates: new key material under a new id. A cache of the old set cannot verify a token signed with it.</summary>
    public void Rotate(string newKeyId) { _rsa = RSA.Create(2048); KeyId = newKeyId; Key = Keys(_rsa, newKeyId); }

    HttpResponseMessage Handle(HttpRequestMessage r)
    {
        if (Down) throw new HttpRequestException("identity is down");
        var path = r.RequestUri!.AbsolutePath;
        if (path == "/.well-known/jwks.json") return FakeBackend.Raw(Jwks());
        var body = r.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
        using var doc = body.Length > 0 ? JsonDocument.Parse(body) : null;
        string? Field(string n) => doc is not null && doc.RootElement.TryGetProperty(n, out var v) ? v.GetString() : null;
        lock (_gate)
        {
            switch (path)
            {
                case "/api/v1/auth/consents/versions":
                    return ConsentVersionsDown ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : FakeBackend.Json(new { terms = ConsentVersion, privacy = ConsentVersion });
                case "/api/v1/auth/register":
                    if (RegisterStatus != 0) return new HttpResponseMessage((HttpStatusCode)RegisterStatus);
                    if (Field("acceptedTermsVersion") != ConsentVersion || Field("acceptedPrivacyVersion") != ConsentVersion) return FakeBackend.Json(new { errors = new[] { "consent" } }, HttpStatusCode.BadRequest);
                    if (Field("email")!.StartsWith("taken", StringComparison.Ordinal)) return FakeBackend.Json(new { errors = new[] { "DuplicateEmail" } }, HttpStatusCode.BadRequest);
                    if (Field("email")!.StartsWith("pending", StringComparison.Ordinal)) return FakeBackend.Json(new { userId = "u", email = Field("email"), message = "confirm", emailVerificationRequired = true }, HttpStatusCode.Accepted);
                    return Tokens(Field("email")!);
                case "/api/v1/auth/2fa/login":
                    if (Field("challengeToken") != "challenge-1") return FakeBackend.Json(new { error = "Invalid or expired challenge." }, HttpStatusCode.Unauthorized);
                    if (Field("code") == TwoFactorCode) return Tokens("demo-client@demo.example");
                    if (Field("recoveryCode") == RecoveryCode && !_recoveryUsed) { _recoveryUsed = true; return Tokens("demo-client@demo.example"); }
                    return FakeBackend.Json(new { error = "Invalid code." }, HttpStatusCode.Unauthorized);
                case "/api/v1/auth/login":
                    if (LoginStatus != 0) return new HttpResponseMessage((HttpStatusCode)LoginStatus);
                    if (Field("password") == Password && LockedOut) return FakeBackend.Json(new { error = "locked", lockedOut = true, lockoutEnd = "2026-12-01T10:00:00Z" }, HttpStatusCode.Unauthorized);
                    if (Field("password") == Password && RequireTwoFactor) return FakeBackend.Json(new { requiresTwoFactor = true, challengeToken = "challenge-1", expiresIn = 300 });
                    if (Field("password") != Password) return FakeBackend.Json(new { message = "Invalid email or password." }, HttpStatusCode.Unauthorized);
                    return Tokens(Field("email")!);
                case "/api/v1/auth/refresh":
                    RefreshCalls++;
                    if (RefreshStatus != 0) return new HttpResponseMessage((HttpStatusCode)RefreshStatus); // throttled or failing: the token is not spent
                    SpentRefreshTokens.Add(Field("refreshToken") ?? "");
                    if (RefuseRefresh || Revoked || Field("refreshToken") is null || SpentRefreshTokens.Count(t => t == Field("refreshToken")) > 1) return FakeBackend.Json(new { message = "Invalid refresh token." }, HttpStatusCode.Unauthorized); // a replay is refused like the real service
                    return Tokens("demo-client@demo.example", AccessTokenSigner);
                case "/api/v1/auth/logout":
                    Revoked = true;
                    return FakeBackend.Json(new { message = "Logged out." });
            }
        }
        if (Account(r, path, doc) is { } account) return account;
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    HttpResponseMessage Tokens(string email, RSA? signWith = null) =>
        FakeBackend.Json(new { accessToken = Issue(email: email, signWith: signWith), refreshToken = $"refresh-{++_counter}", expiresIn = (int)AccessLifetime.TotalSeconds, tokenType = "Bearer" });

    // ---- the account endpoints (me, consents, export, delete), as authservice answers them ----
    public bool EmailConfirmed { get; set; } = true;
    public bool HasPassword { get; set; } = true;
    public bool TermsAccepted { get; set; } = true;
    public bool PrivacyAccepted { get; set; } = true;
    public string AccountEmail { get; set; } = "demo-client@demo.example";
    /// <summary>When set, <c>/me</c>, <c>/consents</c> and the export answer with it (503: the account data is down while sign-in works).</summary>
    public int AccountStatus { get; set; }
    /// <summary>Set by a successful <c>DELETE /account</c>.</summary>
    public bool AccountDeleted { get; private set; }
    public string? LastDeleteBody { get; private set; }
    public int ConsentPosts { get; private set; }

    HttpResponseMessage? Account(HttpRequestMessage r, string path, JsonDocument? body)
    {
        if (!path.StartsWith("/api/v1/auth/") || path.Split('/')[^1] is not ("me" or "consents" or "export" or "account")) return null;
        if (r.Headers.Authorization is null) return FakeBackend.Json(new { error = "Unauthorized" }, HttpStatusCode.Unauthorized);
        if (AccountStatus != 0) return new HttpResponseMessage((HttpStatusCode)AccountStatus);
        lock (_gate)
        {
            switch (path.Split('/')[^1], r.Method.Method)
            {
                case ("me", "GET"):
                    // fields a careless page might dump: the real service returns more than the page may show
                    return FakeBackend.Json(new { id = "demo-user-1", email = AccountEmail, emailConfirmed = EmailConfirmed, hasPassword = HasPassword, accessToken = "AT-SECRET", refreshToken = "RT-SECRET", organizations = new[] { new { id = "o1" } } });
                case ("consents", "GET"):
                    return FakeBackend.Json(new
                    {
                        terms = new { requiredVersion = "2026-01-01", acceptedVersion = TermsAccepted ? "2026-01-01" : "2025-06-01", accepted = TermsAccepted },
                        privacy = new { requiredVersion = "2026-02-01", acceptedVersion = PrivacyAccepted ? "2026-02-01" : "2025-06-01", accepted = PrivacyAccepted },
                        requiresConsent = !(TermsAccepted && PrivacyAccepted),
                    });
                case ("consents", "POST"):
                    ConsentPosts++; (TermsAccepted, PrivacyAccepted) = (true, true);
                    return FakeBackend.Json(new { requiresConsent = false });
                case ("export", "GET"):
                    return FakeBackend.Json(new { exportedAt = "2026-11-17T10:00:00Z", profile = new { email = AccountEmail } });
                case ("account", "DELETE"):
                    LastDeleteBody = body?.RootElement.GetRawText();
                    var password = body is not null && body.RootElement.TryGetProperty("password", out var p) ? p.GetString() : null;
                    if (body is null || body.RootElement.GetProperty("confirmation").GetString() != "DELETE") return FakeBackend.Json(new { error = "Confirmation text must be 'DELETE'" }, HttpStatusCode.BadRequest);
                    if (HasPassword && password != Password) return FakeBackend.Json(new { error = "Invalid password" }, HttpStatusCode.BadRequest);
                    AccountDeleted = true;
                    return FakeBackend.Json(new { message = "Account deleted successfully" });
            }
        }
        return null;
    }
}
