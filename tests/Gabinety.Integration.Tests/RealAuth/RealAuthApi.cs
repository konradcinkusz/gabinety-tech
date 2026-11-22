using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Gabinety.Integration.Tests.AuthContract;

namespace Gabinety.Integration.Tests.RealAuth;

/// <summary>A signed-in user of the real authservice: what the tests need to act as them.</summary>
public sealed record RealUser(string Email, string Password, string Sub, string AccessToken, string RefreshToken)
{
    public ParsedJwt Jwt => ParsedJwt.Parse(AccessToken);
}

/// <summary>The calls every real-authservice test makes. Wire names are those of docs/spikes/authservice.md (read from the source and observed against the image).</summary>
public static class RealAuthApi
{
    public const string UserPassword = "Real-Auth-Pass-123!";

    public static string NewEmail() => $"demo-real-{Guid.NewGuid():N}@example.test";

    public static async Task<JsonElement> Json(this HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    public static async Task<HttpResponseMessage> Send(this HttpClient http, HttpMethod method, string path, string? bearer = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await http.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> RegisterRaw(this HttpClient http, string email, string password = UserPassword)
    {
        var versions = await (await http.GetAsync("api/v1/auth/consents/versions")).Json();
        return await http.PostAsJsonAsync("api/v1/auth/register", new
        {
            email, password, locale = "pl",
            acceptedTermsVersion = versions.GetProperty("terms").GetString(),
            acceptedPrivacyVersion = versions.GetProperty("privacy").GetString(),
        });
    }

    /// <summary>Registers an account on an instance that does not need e-mail confirmation (200 with tokens).</summary>
    public static async Task<RealUser> Register(this AuthInstance auth, string? email = null, string password = UserPassword)
    {
        email ??= NewEmail();
        using var http = auth.Client();
        var response = await http.RegisterRaw(email, password);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return ToUser(email, password, await response.Json());
    }

    public static async Task<RealUser> Login(this AuthInstance auth, string email, string password)
    {
        using var http = auth.Client();
        var response = await http.PostAsJsonAsync("api/v1/auth/login", new { email, password });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return ToUser(email, password, await response.Json());
    }

    static RealUser ToUser(string email, string password, JsonElement tokens)
    {
        var access = tokens.GetProperty("accessToken").GetString()!;
        return new RealUser(email, password, ParsedJwt.Parse(access).Payload.GetProperty("sub").GetString()!, access, tokens.GetProperty("refreshToken").GetString()!);
    }

    /// <summary>The seeded first administrator (a SuperAdmin).</summary>
    public static Task<RealUser> LoginOwner(this AuthInstance auth) => auth.Login(RealAuthFixture.OwnerEmail, RealAuthFixture.OwnerPassword);

    /// <summary>A new account holding exactly <paramref name="roles"/> (assigned by the owner through the admin API), signed in again so the token carries them.</summary>
    public static async Task<RealUser> UserWithRoles(this AuthInstance auth, params string[] roles)
    {
        var user = await auth.Register();
        if (roles.Length == 0) return user;
        var owner = await auth.LoginOwner();
        using var http = auth.Client();
        foreach (var role in roles)
            Assert.Equal(System.Net.HttpStatusCode.OK, (await http.Send(HttpMethod.Post, $"api/v1/admin/users/{user.Sub}/roles", owner.AccessToken, new { role })).StatusCode);
        return await auth.Login(user.Email, user.Password);
    }

    /// <summary>RFC 6238 (SHA-1, 30 s, 6 digits) from the base32 shared key authservice hands out, as an authenticator app computes it.</summary>
    public static string Totp(string sharedKey, DateTimeOffset now)
    {
        var key = Base32(sharedKey);
        var counter = BitConverter.GetBytes(now.ToUnixTimeSeconds() / 30);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0xF;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    static byte[] Base32(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0; var value = 0; var bytes = new List<byte>();
        foreach (var c in text.Where(c => c != ' ' && c != '-' && c != '=').Select(char.ToUpperInvariant))
        {
            value = (value << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8) { bytes.Add((byte)((value >> (bits - 8)) & 0xFF)); bits -= 8; }
        }
        return bytes.ToArray();
    }
}
