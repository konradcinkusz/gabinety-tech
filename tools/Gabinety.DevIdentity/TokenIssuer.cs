using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gabinety.DevIdentity;

/// <summary>RS256 access tokens with the claim names of docs/spikes/authservice.md. The RSA key lives in memory and dies with the process.</summary>
public sealed class TokenIssuer(TimeProvider time, string issuer, string audience)
{
    public const string RoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
    private const string NameIdentifierClaim = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";
    private const string NameClaim = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name";
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(60); // authservice default

    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false }; // authservice issues no iat and no nbf
    private RsaSecurityKey Key { get; } = CreateKey(RSA.Create(2048));

    private static RsaSecurityKey CreateKey(RSA rsa) => new(rsa) { KeyId = Base64UrlEncoder.Encode(new JsonWebKey { Kty = "RSA", N = Base64UrlEncoder.Encode(rsa.ExportParameters(false).Modulus), E = Base64UrlEncoder.Encode(rsa.ExportParameters(false).Exponent) }.ComputeJwkThumbprint()) }; // RFC 7638, as authservice

    public string Issuer => issuer;

    public string CreateAccessToken(DevUser user)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = user.Id,
            ["email"] = user.Email,
            ["jti"] = Guid.NewGuid().ToString(),
            [NameIdentifierClaim] = user.Id,
            [NameClaim] = user.Email,
        };
        // One role is a JSON string; several are an array (the same claim name repeated). The array shape is a conclusion in the spike, not yet read from a real token.
        if (user.Roles.Length == 1) claims[RoleClaim] = user.Roles[0];
        else if (user.Roles.Length > 1) claims[RoleClaim] = user.Roles;
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = (time.GetUtcNow() + AccessLifetime).UtcDateTime, // no iat and no nbf, as authservice
            Claims = claims,
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256),
        });
    }

    /// <summary>The subject of a valid, unexpired token of ours; null otherwise.</summary>
    public async Task<string?> ValidateAsync(string token)
    {
        var result = await _handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = Key,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = (_, expires, _, _) => expires is not null && expires > time.GetUtcNow().UtcDateTime,
        });
        return result.IsValid ? ((JsonWebToken)result.SecurityToken).Subject : null;
    }

    public string JwksJson()
    {
        var p = Key.Rsa.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            keys = new[] { new { kty = "RSA", use = "sig", alg = "RS256", kid = Key.KeyId, n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent) } },
        });
    }
}
