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
public abstract class AuthContractSuite(HttpClient http) : IDisposable
{
    public const string RoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
    // Fixed synthetic value for users this suite registers; not a secret.
    protected const string Password = "Contract-Pass-123!";

    protected HttpClient Http { get; } = http;

    /// <summary>A client for a real base URL (the real authservice image).</summary>
    protected static HttpClient ForBaseUrl(string baseUrl)
    {
        var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
        // authservice rate-limits its auth endpoints to 20 a minute per client address (Program.cs). Every test runs from this one machine, so each client names a
        // distinct address in the header an authservice configured with Network__ClientIpHeader=Fly-Client-IP trusts; one without that setting ignores it.
        http.DefaultRequestHeaders.Add(ClientIpHeader, UniqueClientIp());
        return http;
    }

    public const string ClientIpHeader = "Fly-Client-IP";
    private static int _clientIps;
    /// <summary>A distinct private address (10.x.y.z) per call, so a client gets its own rate-limit bucket.</summary>
    public static string UniqueClientIp()
    {
        var n = Interlocked.Increment(ref _clientIps);
        return $"10.{(n >> 16) & 255}.{(n >> 8) & 255}.{n & 255}";
    }

    public void Dispose() => Http.Dispose();

    /// <summary>The claims of an ordinary user's access token, measured on the real authservice v0.3.4 (CI run 43933246622). No email_verified, no iat, no nbf.</summary>
    protected static readonly string[] OrdinaryClaims =
    [
        "aud", "email", "exp", "iss", "jti", "sub",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier",
    ];

    protected static string[] ClaimNames(ParsedJwt jwt) => jwt.Payload.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

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
    public async Task Register_returns_tokens_with_exactly_the_claims_of_the_real_service()
    {
        var email = NewEmail();
        var tokens = await RegisterAsync(email);
        AssertTokenResponse(tokens);

        var jwt = ParsedJwt.Parse(tokens.GetProperty("accessToken").GetString()!);
        Assert.Equal("RS256", jwt.Header.GetProperty("alg").GetString());
        Assert.Equal("JWT", jwt.Header.GetProperty("typ").GetString());
        Assert.False(string.IsNullOrWhiteSpace(jwt.Header.GetProperty("kid").GetString()));
        Assert.Equal(OrdinaryClaims.Order(StringComparer.Ordinal), ClaimNames(jwt));
        Assert.False(string.IsNullOrWhiteSpace(jwt.Payload.GetProperty("sub").GetString()));
        Assert.Equal(email, jwt.Payload.GetProperty("email").GetString(), ignoreCase: true);
        Assert.Equal(JsonValueKind.Number, jwt.Payload.GetProperty("exp").ValueKind);
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

    // ---- the parts the account pages and the registration form depend on (T: real-authservice). Each was read from, and observed against, authservice v0.3.4. ----

    protected async Task<HttpResponseMessage> RegisterRawAsync(object body) => await Http.PostAsJsonAsync("api/v1/auth/register", body);

    protected async Task<(string Terms, string Privacy)> VersionsAsync()
    {
        var v = await ReadJson(await Http.GetAsync("api/v1/auth/consents/versions"));
        return (v.GetProperty("terms").GetString()!, v.GetProperty("privacy").GetString()!);
    }

    [ContractFact]
    public async Task Consent_versions_are_three_non_empty_strings()
    {
        var response = await Http.GetAsync("api/v1/auth/consents/versions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var v = await ReadJson(response);
        foreach (var name in new[] { "terms", "privacy", "cookies" }) Assert.False(string.IsNullOrWhiteSpace(v.GetProperty(name).GetString()), name);
    }

    [ContractFact]
    public async Task Register_with_a_version_that_is_not_the_required_one_is_400_with_an_errors_list()
    {
        var (terms, privacy) = await VersionsAsync();

        var response = await RegisterRawAsync(new { email = NewEmail(), password = Password, acceptedTermsVersion = terms + "-old", acceptedPrivacyVersion = privacy });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJson(response)).GetProperty("errors").GetArrayLength() > 0);
    }

    [ContractFact]
    public async Task Register_with_a_weak_password_is_400_with_an_errors_list()
    {
        var (terms, privacy) = await VersionsAsync();

        var response = await RegisterRawAsync(new { email = NewEmail(), password = "password", acceptedTermsVersion = terms, acceptedPrivacyVersion = privacy });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJson(response)).GetProperty("errors").GetArrayLength() > 0);
    }

    [ContractFact]
    public async Task Register_the_same_address_twice_is_400_with_an_errors_list()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var (terms, privacy) = await VersionsAsync();

        var response = await RegisterRawAsync(new { email, password = Password, acceptedTermsVersion = terms, acceptedPrivacyVersion = privacy });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJson(response)).GetProperty("errors").GetArrayLength() > 0);
    }

    [ContractFact]
    public async Task An_unknown_address_and_a_wrong_password_are_the_same_401()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var wrong = await LoginAsync(email, Password + "-wrong");
        var unknown = await LoginAsync(NewEmail(), Password);

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync()); // no account-existence oracle
    }

    [ContractFact]
    public async Task Five_wrong_passwords_lock_the_account_and_only_the_right_password_is_told_so()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, Password + "-wrong")).StatusCode);

        var right = await LoginAsync(email, Password);
        var wrong = await LoginAsync(email, Password + "-wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, right.StatusCode); // locked: the right password does not open it either
        var body = await ReadJson(right);
        Assert.True(body.GetProperty("lockedOut").GetBoolean());
        Assert.Equal(JsonValueKind.String, body.GetProperty("lockoutEnd").ValueKind);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.False((await ReadJson(wrong)).TryGetProperty("lockedOut", out _)); // a guesser learns nothing about the lock
    }

    [ContractFact]
    public async Task Refresh_without_a_token_is_400()
    {
        var response = await RefreshAsync("");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [ContractFact]
    public async Task Replaying_a_refresh_token_also_revokes_the_one_it_was_rotated_into()
    {
        var first = await RegisterAsync(NewEmail());
        var rotated = await ReadJson(await RefreshAsync(first.GetProperty("refreshToken").GetString()!));

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(first.GetProperty("refreshToken").GetString()!)).StatusCode); // the replay
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(rotated.GetProperty("refreshToken").GetString()!)).StatusCode); // the family is gone
    }

    [ContractFact]
    public async Task Consents_report_the_required_and_accepted_versions()
    {
        var (terms, privacy) = await VersionsAsync();
        var access = (await RegisterAsync(NewEmail())).GetProperty("accessToken").GetString()!;

        var response = await AuthorizedAsync(HttpMethod.Get, "api/v1/auth/consents", access);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await ReadJson(response);
        Assert.Equal(terms, status.GetProperty("terms").GetProperty("requiredVersion").GetString());
        Assert.Equal(terms, status.GetProperty("terms").GetProperty("acceptedVersion").GetString());
        Assert.True(status.GetProperty("terms").GetProperty("accepted").GetBoolean());
        Assert.Equal(privacy, status.GetProperty("privacy").GetProperty("acceptedVersion").GetString());
        Assert.False(status.GetProperty("requiresConsent").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Http.GetAsync("api/v1/auth/consents")).StatusCode);
    }

    [ContractFact]
    public async Task Accepting_the_consents_answers_the_status_and_needs_a_token()
    {
        var (terms, privacy) = await VersionsAsync();
        var access = (await RegisterAsync(NewEmail())).GetProperty("accessToken").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/consents") { Content = JsonContent.Create(new { acceptedTerms = true, acceptedPrivacy = true }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);

        var response = await Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await ReadJson(response);
        Assert.True(status.GetProperty("terms").GetProperty("accepted").GetBoolean());
        Assert.Equal(terms, status.GetProperty("terms").GetProperty("acceptedVersion").GetString());
        Assert.Equal(privacy, status.GetProperty("privacy").GetProperty("acceptedVersion").GetString());
        Assert.False(status.GetProperty("requiresConsent").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Http.PostAsJsonAsync("api/v1/auth/consents", new { acceptedTerms = true })).StatusCode);
    }

    [ContractFact]
    public async Task Consents_have_the_cookie_entry_the_account_page_reads()
    {
        var access = (await RegisterAsync(NewEmail())).GetProperty("accessToken").GetString()!;
        var status = await ReadJson(await AuthorizedAsync(HttpMethod.Get, "api/v1/auth/consents", access));
        var cookies = status.GetProperty("cookies");
        Assert.False(string.IsNullOrWhiteSpace(cookies.GetProperty("requiredVersion").GetString()));
        Assert.Equal(JsonValueKind.False, cookies.GetProperty("accepted").ValueKind);
    }

    [ContractFact]
    public async Task Exchanging_a_code_that_was_never_issued_is_401()
    {
        var response = await Http.PostAsJsonAsync("api/v1/external-auth/exchange", new { code = "demo-never-issued" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [ContractFact]
    public async Task Me_has_the_fields_the_account_page_reads()
    {
        var access = (await RegisterAsync(NewEmail())).GetProperty("accessToken").GetString()!;

        var me = await ReadJson(await AuthorizedAsync(HttpMethod.Get, "api/v1/auth/me", access));

        Assert.True(me.GetProperty("hasPassword").GetBoolean());
        Assert.True(me.GetProperty("emailConfirmed").GetBoolean());
        Assert.False(me.GetProperty("twoFactorEnabled").GetBoolean());
        Assert.False(me.GetProperty("requiresConsent").GetBoolean());
    }

    [ContractFact]
    public async Task Export_holds_the_profile_and_the_accepted_consents()
    {
        var email = NewEmail();
        var access = (await RegisterAsync(email)).GetProperty("accessToken").GetString()!;

        var response = await AuthorizedAsync(HttpMethod.Get, "api/v1/auth/export", access);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var export = await ReadJson(response);
        Assert.Equal(email, export.GetProperty("profile").GetProperty("email").GetString(), ignoreCase: true);
        Assert.Equal(JsonValueKind.Array, export.GetProperty("consents").ValueKind);
        Assert.True(export.GetProperty("consents").GetArrayLength() >= 2);
        foreach (var name in new[] { "externalLogins", "organizations", "sessions" }) Assert.Equal(JsonValueKind.Array, export.GetProperty(name).ValueKind);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Http.GetAsync("api/v1/auth/export")).StatusCode);
    }

    [ContractFact]
    public async Task Deleting_the_account_needs_the_word_and_the_password_and_then_ends_every_way_back_in()
    {
        var email = NewEmail();
        var tokens = await RegisterAsync(email);
        var access = tokens.GetProperty("accessToken").GetString()!;
        async Task<HttpResponseMessage> Delete(object body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, "api/v1/auth/account") { Content = JsonContent.Create(body) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            return await Http.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await Delete(new { password = Password, confirmation = "delete" })).StatusCode); // the word is DELETE
        Assert.Equal(HttpStatusCode.BadRequest, (await Delete(new { confirmation = "DELETE" })).StatusCode); // an account with a password needs it
        Assert.Equal(HttpStatusCode.BadRequest, (await Delete(new { password = Password + "-wrong", confirmation = "DELETE" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, Password)).StatusCode); // nothing happened yet

        Assert.Equal(HttpStatusCode.OK, (await Delete(new { password = Password, confirmation = "DELETE" })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tokens.GetProperty("refreshToken").GetString()!)).StatusCode);
    }

    [ContractFact]
    public async Task The_provider_list_is_an_array()
    {
        var response = await Http.GetAsync("api/v1/external-auth/providers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Array, (await ReadJson(response)).GetProperty("providers").ValueKind);
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
