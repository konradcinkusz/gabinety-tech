using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Gabinety.DemoSeeder;

/// <summary>A signed-in demo user. The token is for requests only: it is never part of ToString and never logged.</summary>
public sealed class DemoUser(string name, string userId, string accessToken)
{
    public string Name { get; } = name;
    public string UserId { get; } = userId;
    internal string AccessToken { get; } = accessToken;
    public override string ToString() => Name;
}

/// <summary>
/// Signs in as the DevIdentity users through its public auth endpoints. The seeded names (demo-client, demo-practitioner-1 to 8, demo-admin) just log in;
/// any other demo name is registered on first use, so the run is idempotent (a second run finds the user and logs in).
/// </summary>
public sealed class DemoIdentity(HttpClient http, SeedOptions options)
{
    static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    readonly Dictionary<string, DemoUser> _cache = new();

    public async Task<DemoUser> SignInAsync(string userName, CancellationToken ct)
    {
        if (_cache.TryGetValue(userName, out var cached)) return cached;
        var email = DemoNames.Email(userName);
        var token = await LoginAsync(email, ct);
        if (token is null)
        {
            await RegisterAsync(email, ct);
            token = await LoginAsync(email, ct) ?? throw new SeedException($"identity: {userName} cannot sign in after registering");
        }
        using var me = new HttpRequestMessage(HttpMethod.Get, "api/v1/auth/me");
        me.Headers.Authorization = new("Bearer", token);
        using var res = await http.SendAsync(me, ct);
        if (!res.IsSuccessStatusCode) throw new SeedException($"identity: /me answered {(int)res.StatusCode} for {userName}");
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>(J, ct)).GetProperty("id").GetString();
        return _cache[userName] = new DemoUser(userName, id ?? throw new SeedException($"identity: no id for {userName}"), token);
    }

    async Task<string?> LoginAsync(string email, CancellationToken ct)
    {
        using var res = await http.PostAsJsonAsync("api/v1/auth/login", new { email, password = options.DemoPassword }, J, ct);
        if (res.StatusCode == HttpStatusCode.Unauthorized) return null;
        if (!res.IsSuccessStatusCode) throw new SeedException($"identity: login answered {(int)res.StatusCode}");
        return (await res.Content.ReadFromJsonAsync<JsonElement>(J, ct)).GetProperty("accessToken").GetString();
    }

    async Task RegisterAsync(string email, CancellationToken ct)
    {
        var versions = await http.GetFromJsonAsync<JsonElement>("api/v1/auth/consents/versions", J, ct);
        using var res = await http.PostAsJsonAsync("api/v1/auth/register", new
        {
            email, password = options.DemoPassword, locale = "pl",
            acceptedTermsVersion = versions.GetProperty("terms").GetString(), acceptedPrivacyVersion = versions.GetProperty("privacy").GetString(),
        }, J, ct);
        if (!res.IsSuccessStatusCode) throw new SeedException($"identity: register answered {(int)res.StatusCode}");
    }
}
