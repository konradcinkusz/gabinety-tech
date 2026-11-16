using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Gabinety.Integration.Tests.AuthContract;

/// <summary>Minimal JWT reading and RS256 verification, so the suite needs no token library.</summary>
public sealed record ParsedJwt(JsonElement Header, JsonElement Payload, byte[] SigningInput, byte[] Signature)
{
    public static ParsedJwt Parse(string jwt)
    {
        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);
        return new ParsedJwt(
            JsonDocument.Parse(B64Url(parts[0])).RootElement.Clone(),
            JsonDocument.Parse(B64Url(parts[1])).RootElement.Clone(),
            Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]),
            B64Url(parts[2]));
    }

    public bool VerifyRs256(JsonElement jwk)
    {
        using var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters { Modulus = B64Url(jwk.GetProperty("n").GetString()!), Exponent = B64Url(jwk.GetProperty("e").GetString()!) });
        return rsa.VerifyData(SigningInput, Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    public static byte[] B64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
