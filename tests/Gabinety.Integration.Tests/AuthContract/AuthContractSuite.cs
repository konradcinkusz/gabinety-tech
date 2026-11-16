using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Gabinety.Integration.Tests.AuthContract;

/// <summary>
/// The auth contract (ADR-0003). One body of tests, parameterised by a base URL: it runs against DevIdentity in the unit tier
/// and against the real authservice image in the integration tier, so the stand-in cannot drift from the real surface.
/// Wire names are those of docs/spikes/authservice.md.
/// </summary>
public abstract class AuthContractSuite(string baseUrl) : IDisposable
{
    public const string RoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
    // Fixed synthetic value for users this suite registers; not a secret.
    protected const string Password = "Contract-Pass-123!";

    protected HttpClient Http { get; } = new() { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };

    public void Dispose() => Http.Dispose();

    protected static string NewEmail() => $"demo-contract-{Guid.NewGuid():N}@example.test";

    protected async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    protected async Task<JsonElement> RegisterAsync(string email, string password = Password)
    {
        var versions = await ReadJson(await Http.GetAsync("api/v1/auth/consents/versions"));
        var response = await Http.PostAsJsonAsync("api/v1/auth/register", new
        {
            email,
            password,
            acceptedTermsVersion = versions.GetProperty("terms").GetString(),
            acceptedPrivacyVersion = versions.GetProperty("privacy").GetString(),
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJson(response);
    }

    protected Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        Http.PostAsJsonAsync("api/v1/auth/login", new { email, password });

    protected Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        Http.PostAsJsonAsync("api/v1/auth/refresh", new { refreshToken });

    protected async Task<HttpResponseMessage> AuthorizedAsync(HttpMethod method, string path, string accessToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await Http.SendAsync(request);
    }

    protected static void AssertTokenResponse(JsonElement tokens)
    {
        Assert.False(string.IsNullOrWhiteSpace(tokens.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(tokens.GetProperty("refreshToken").GetString()));
        Assert.True(tokens.GetProperty("expiresIn").GetInt32() > 0);
        Assert.Equal("Bearer", tokens.GetProperty("tokenType").GetString());
    }

    [ContractFact]
    public async Task Register_returns_tokens_whose_claims_follow_the_spike()
    {
        var email = NewEmail();
        var tokens = await RegisterAsync(email);
        AssertTokenResponse(tokens);

        var jwt = ParsedJwt.Parse(tokens.GetProperty("accessToken").GetString()!);
        Assert.Equal("RS256", jwt.Header.GetProperty("alg").GetString());
        Assert.False(string.IsNullOrWhiteSpace(jwt.Payload.GetProperty("sub").GetString()));
        Assert.Equal(email, jwt.Payload.GetProperty("email").GetString(), ignoreCase: true);
        Assert.Equal(JsonValueKind.True, jwt.Payload.GetProperty("email_verified").ValueKind);
    }

    [ContractFact]
    public async Task Login_with_the_registered_password_returns_tokens()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var response = await LoginAsync(email, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertTokenResponse(await ReadJson(response));
    }

    [ContractFact]
    public async Task Login_with_a_wrong_password_is_401()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var response = await LoginAsync(email, Password + "-wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [ContractFact]
    public async Task Refresh_rotates_the_refresh_token_and_issues_a_working_access_token()
    {
        var first = await RegisterAsync(NewEmail());
        var oldRefresh = first.GetProperty("refreshToken").GetString()!;

        var response = await RefreshAsync(oldRefresh);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var second = await ReadJson(response);
        AssertTokenResponse(second);
        Assert.NotEqual(oldRefresh, second.GetProperty("refreshToken").GetString());
        var me = await AuthorizedAsync(HttpMethod.Get, "api/v1/auth/me", second.GetProperty("accessToken").GetString()!);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [ContractFact]
    public async Task A_replayed_refresh_token_is_401()
    {
        var first = await RegisterAsync(NewEmail());
        var oldRefresh = first.GetProperty("refreshToken").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(oldRefresh)).StatusCode);

        var replay = await RefreshAsync(oldRefresh);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [ContractFact]
    public async Task Me_returns_the_caller_and_requires_a_bearer_token()
    {
        var email = NewEmail();
        var tokens = await RegisterAsync(email);
        var access = tokens.GetProperty("accessToken").GetString()!;
        var sub = ParsedJwt.Parse(access).Payload.GetProperty("sub").GetString();

        var response = await AuthorizedAsync(HttpMethod.Get, "api/v1/auth/me", access);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await ReadJson(response);
        Assert.Equal(sub, me.GetProperty("id").GetString());
        Assert.Equal(email, me.GetProperty("email").GetString(), ignoreCase: true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Http.GetAsync("api/v1/auth/me")).StatusCode);
    }

    [ContractFact]
    public async Task Logout_revokes_the_refresh_tokens_of_the_user()
    {
        var tokens = await RegisterAsync(NewEmail());

        var logout = await AuthorizedAsync(HttpMethod.Post, "api/v1/auth/logout", tokens.GetProperty("accessToken").GetString()!);

        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tokens.GetProperty("refreshToken").GetString()!)).StatusCode);
    }

    [ContractFact]
    public async Task Jwks_contains_one_key_that_verifies_the_access_token()
    {
        var access = (await RegisterAsync(NewEmail())).GetProperty("accessToken").GetString()!;
        var jwt = ParsedJwt.Parse(access);

        var response = await Http.GetAsync(".well-known/jwks.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var keys = (await ReadJson(response)).GetProperty("keys");
        Assert.Equal(1, keys.GetArrayLength());
        var key = keys[0];
        Assert.Equal("RSA", key.GetProperty("kty").GetString());
        Assert.Equal(jwt.Header.GetProperty("kid").GetString(), key.GetProperty("kid").GetString());
        Assert.True(jwt.VerifyRs256(key), "the access token signature must verify with the published key");
        var tampered = jwt with { SigningInput = jwt.SigningInput.Append((byte)'x').ToArray() };
        Assert.False(tampered.VerifyRs256(key), "a changed token must not verify");
    }

    [ContractFact]
    public async Task Discovery_names_the_jwks_and_the_issuer_of_the_tokens()
    {
        var access = (await RegisterAsync(NewEmail())).GetProperty("accessToken").GetString()!;

        var response = await Http.GetAsync(".well-known/openid-configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var discovery = await ReadJson(response);
        Assert.Equal(ParsedJwt.Parse(access).Payload.GetProperty("iss").GetString(), discovery.GetProperty("issuer").GetString());
        var jwksUri = new Uri(discovery.GetProperty("jwks_uri").GetString()!, UriKind.Absolute);
        Assert.Equal("/.well-known/jwks.json", jwksUri.AbsolutePath);
        var direct = await (await Http.GetAsync(".well-known/jwks.json")).Content.ReadAsStringAsync();
        var named = await (await Http.GetAsync(jwksUri.AbsolutePath.TrimStart('/'))).Content.ReadAsStringAsync();
        Assert.Equal(direct, named);
    }
}
