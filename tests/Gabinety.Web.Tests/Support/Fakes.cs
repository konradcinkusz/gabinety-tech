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
    public bool Down { get; set; }
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
                case "/api/v1/auth/login":
                    if (Field("password") != Password) return FakeBackend.Json(new { message = "Invalid email or password." }, HttpStatusCode.Unauthorized);
                    return Tokens(Field("email")!);
                case "/api/v1/auth/refresh":
                    RefreshCalls++;
                    if (RefuseRefresh || Field("refreshToken") is null) return FakeBackend.Json(new { message = "Invalid refresh token." }, HttpStatusCode.Unauthorized);
                    return Tokens("demo-client@demo.example");
                case "/api/v1/auth/logout":
                    return FakeBackend.Json(new { message = "Logged out." });
            }
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    HttpResponseMessage Tokens(string email) =>
        FakeBackend.Json(new { accessToken = Issue(email: email), refreshToken = $"refresh-{++_counter}", expiresIn = (int)AccessLifetime.TotalSeconds, tokenType = "Bearer" });
}
