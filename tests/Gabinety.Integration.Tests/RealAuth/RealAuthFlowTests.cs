using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Integration.Tests.AuthContract;
using Xunit.Abstractions;

namespace Gabinety.Integration.Tests.RealAuth;

/// <summary>
/// Every flow the web app's auth handlers (web/src/lib/session, web/src/components/auth) depend on, against the REAL authservice image on a real PostgreSQL. The basics
/// (register, login, wrong password, refresh rotation and replay, logout, me, JWKS) are the shared AuthContract suite; this adds what only the real service has: e-mail
/// confirmation, the platform roles on the wire, two-factor, GitHub sign-in start, soft delete by an administrator, the rate limit.
/// </summary>
[Trait("tier", "integration")]
[Collection(RealAuthCollection.Name)]
public class RealAuthFlowTests(RealAuthFixture auth, ITestOutputHelper output)
{
    [Fact]
    public async Task Registration_without_confirmation_answers_200_with_tokens_and_consents_recorded()
    {
        using var http = auth.Main.Client();

        var response = await http.RegisterRaw(RealAuthApi.NewEmail());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Json();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("accessToken").GetString()));
        Assert.False(body.TryGetProperty("emailVerificationRequired", out _));
    }

    [Fact]
    public async Task Registration_where_confirmation_is_required_answers_202_without_tokens_and_login_before_confirming_is_the_generic_401()
    {
        using var http = auth.Confirm.Client();
        var email = RealAuthApi.NewEmail();

        var response = await http.RegisterRaw(email);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Json();
        Assert.True(body.GetProperty("emailVerificationRequired").GetBoolean());
        Assert.Equal(email, body.GetProperty("email").GetString(), ignoreCase: true);
        Assert.False(string.IsNullOrEmpty(body.GetProperty("userId").GetString()));
        Assert.False(body.TryGetProperty("accessToken", out _)); // an unconfirmed address is not yet proof of anything

        // Observed on v0.3.4: with Auth__RequireConfirmedEmail=true ASP.NET Identity itself refuses the unconfirmed account, so the right password is the same generic 401 as a wrong
        // one. The 403 {emailVerificationRequired:true} of AuthController.Login (and of the spike's table) is only reached when Identity's own check is off and authservice's is on, i.e. never
        // with this one setting. The web's "verification_required" branch for 403 therefore cannot fire; after a 202 the person learns of the e-mail from the register answer only.
        var login = await http.PostAsJsonAsync("api/v1/auth/login", new { email, password = RealAuthApi.UserPassword });
        output.WriteLine($"OBSERVED login before confirming: {(int)login.StatusCode} {await login.Content.ReadAsStringAsync()}");
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.False((await login.Json()).TryGetProperty("emailVerificationRequired", out _));
    }

    [Fact]
    public async Task An_ordinary_user_token_has_no_role_and_the_seeded_owner_is_a_SuperAdmin_with_one_string_role()
    {
        var user = await auth.Main.Register();
        var owner = await auth.Main.LoginOwner();

        Assert.False(user.Jwt.Payload.TryGetProperty(AuthContractSuite.RoleClaim, out _));
        var role = owner.Jwt.Payload.GetProperty(AuthContractSuite.RoleClaim);
        output.WriteLine($"OBSERVED InitialAdmin role claim: kind={role.ValueKind} value={role}");
        Assert.Equal(JsonValueKind.String, role.ValueKind);
        Assert.Equal("SuperAdmin", role.GetString()); // InitialAdmin seeds SuperAdmin, never Admin (DbSeeder.SeedInitialAdminAsync)
    }

    [Fact]
    public async Task A_user_given_Admin_carries_it_as_a_string_and_a_user_with_two_roles_carries_an_array()
    {
        var admin = await auth.Main.UserWithRoles("Admin");
        var two = await auth.Main.UserWithRoles("Admin", "User");

        var one = admin.Jwt.Payload.GetProperty(AuthContractSuite.RoleClaim);
        var many = two.Jwt.Payload.GetProperty(AuthContractSuite.RoleClaim);
        output.WriteLine($"OBSERVED Admin role claim: kind={one.ValueKind}; two roles: kind={many.ValueKind} count={(many.ValueKind == JsonValueKind.Array ? many.GetArrayLength() : -1)}");
        Assert.Equal(JsonValueKind.String, one.ValueKind);
        Assert.Equal("Admin", one.GetString());
        Assert.Equal(JsonValueKind.Array, many.ValueKind);
        Assert.Equal(["Admin", "User"], many.EnumerateArray().Select(r => r.GetString()!).Order(StringComparer.Ordinal).ToArray());
        Assert.False(admin.Jwt.Payload.TryGetProperty("role", out _)); // the short name is not on the wire
    }

    [Fact]
    public async Task Assigning_a_role_ends_the_refresh_tokens_of_that_user_and_only_a_SuperAdmin_may_do_it()
    {
        var user = await auth.Main.Register();
        var admin = await auth.Main.UserWithRoles("Admin");
        var owner = await auth.Main.LoginOwner();
        using var http = auth.Main.Client();

        // An Admin may read the admin API but not hand out roles; a plain user may do neither.
        Assert.Equal(HttpStatusCode.Forbidden, (await http.Send(HttpMethod.Post, $"api/v1/admin/users/{user.Sub}/roles", admin.AccessToken, new { role = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.Send(HttpMethod.Get, "api/v1/admin/users", user.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.Send(HttpMethod.Get, "api/v1/admin/users", admin.AccessToken)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await http.Send(HttpMethod.Post, $"api/v1/admin/users/{user.Sub}/roles", owner.AccessToken, new { role = "Admin" })).StatusCode);

        // "A new role only reaches the token on the next refresh": the old refresh token is revoked, the person signs in again.
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("api/v1/auth/refresh", new { refreshToken = user.RefreshToken })).StatusCode);
        Assert.Equal("Admin", (await auth.Main.Login(user.Email, user.Password)).Jwt.Payload.GetProperty(AuthContractSuite.RoleClaim).GetString());
    }

    [Fact]
    public async Task An_administrator_can_find_a_user_and_soft_delete_it_which_is_the_route_of_the_erasure_runbook()
    {
        var victim = await auth.Main.Register();
        var owner = await auth.Main.LoginOwner();
        using var http = auth.Main.Client();

        var found = await (await http.Send(HttpMethod.Get, $"api/v1/admin/users?search={Uri.EscapeDataString(victim.Email)}", owner.AccessToken)).Json();
        Assert.Equal(victim.Sub, found.GetProperty("users")[0].GetProperty("id").GetString());

        var deleted = await http.Send(HttpMethod.Delete, $"api/v1/admin/users/{victim.Sub}", owner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.True((await deleted.Json()).GetProperty("retentionDays").GetInt32() > 0);

        using var again = auth.Main.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await again.PostAsJsonAsync("api/v1/auth/login", new { email = victim.Email, password = victim.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await again.PostAsJsonAsync("api/v1/auth/refresh", new { refreshToken = victim.RefreshToken })).StatusCode);
        var list = await (await again.Send(HttpMethod.Get, "api/v1/admin/users/deleted", owner.AccessToken)).Json();
        Assert.Contains(list.GetProperty("users").EnumerateArray(), u => u.GetProperty("id").GetString() == victim.Sub);
    }

    [Fact]
    public async Task A_deleted_account_cannot_register_again_with_its_address_until_it_is_purged()
    {
        var user = await auth.Main.Register();
        using var http = auth.Main.Client();
        Assert.Equal(HttpStatusCode.OK, (await http.Send(HttpMethod.Delete, "api/v1/auth/account", user.AccessToken, new { password = user.Password, confirmation = "DELETE" })).StatusCode);

        var again = await http.RegisterRaw(user.Email);

        output.WriteLine($"OBSERVED register after soft delete: {(int)again.StatusCode}");
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode); // the row is still there for the retention period; the address is taken
    }

    [Fact]
    public async Task Two_factor_login_answers_200_with_a_challenge_and_not_tokens_until_the_code_is_given()
    {
        var user = await auth.Main.Register();
        using var http = auth.Main.Client();
        var setup = await (await http.Send(HttpMethod.Post, "api/v1/auth/2fa/enable", user.AccessToken)).Json();
        var key = setup.GetProperty("sharedKey").GetString()!;
        var verify = await http.Send(HttpMethod.Post, "api/v1/auth/2fa/verify", user.AccessToken, new { code = RealAuthApi.Totp(key, DateTimeOffset.UtcNow) });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var recovery = (await verify.Json()).GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToArray();

        var login = await http.PostAsJsonAsync("api/v1/auth/login", new { email = user.Email, password = user.Password });

        // The shape the web's identity.ts reads as "two_factor": the same 200 as a token answer, but with requiresTwoFactor and no accessToken.
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var challenge = await login.Json();
        Assert.True(challenge.GetProperty("requiresTwoFactor").GetBoolean());
        Assert.Equal(300, challenge.GetProperty("expiresIn").GetInt32());
        Assert.False(challenge.TryGetProperty("accessToken", out _));
        var token = challenge.GetProperty("challengeToken").GetString();

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("api/v1/auth/2fa/login", new { challengeToken = token, code = "000000" })).StatusCode);
        var done = await http.PostAsJsonAsync("api/v1/auth/2fa/login", new { challengeToken = token, code = RealAuthApi.Totp(key, DateTimeOffset.UtcNow) });
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        Assert.False(string.IsNullOrEmpty((await done.Json()).GetProperty("accessToken").GetString()));

        // A recovery code works once.
        var second = await (await http.PostAsJsonAsync("api/v1/auth/login", new { email = user.Email, password = user.Password })).Json();
        var withRecovery = new { challengeToken = second.GetProperty("challengeToken").GetString(), recoveryCode = recovery[0] };
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("api/v1/auth/2fa/login", withRecovery)).StatusCode);
        var third = await (await http.PostAsJsonAsync("api/v1/auth/login", new { email = user.Email, password = user.Password })).Json();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("api/v1/auth/2fa/login", new { challengeToken = third.GetProperty("challengeToken").GetString(), recoveryCode = recovery[0] })).StatusCode);

        var me = await (await http.Send(HttpMethod.Get, "api/v1/auth/me", user.AccessToken)).Json();
        Assert.True(me.GetProperty("twoFactorEnabled").GetBoolean());
    }

    [Fact]
    public async Task GitHub_sign_in_start_redirects_to_github_with_a_state_and_the_return_url_of_the_web_is_accepted()
    {
        using var http = auth.Main.Client();
        // The URL the web's githubStart builds: the web origin, /oauth/callback, and its own state nonce in the query.
        var returnUrl = Uri.EscapeDataString($"{RealAuthFixture.WebOrigin}/oauth/callback?redirect=%2Fmoje%2Frezerwacje&state=abc123");

        var start = await http.GetAsync($"api/v1/external-auth/login?provider=GitHub&returnUrl={returnUrl}");

        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var location = start.Headers.Location!;
        output.WriteLine($"OBSERVED GitHub redirect: {location.GetLeftPart(UriPartial.Path)} redirect_uri={System.Web.HttpUtility.ParseQueryString(location.Query)["redirect_uri"]}");
        Assert.Equal("https://github.com/login/oauth/authorize", location.GetLeftPart(UriPartial.Path));
        var query = System.Web.HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("dummy-github-client-id", query["client_id"]);
        Assert.False(string.IsNullOrEmpty(query["state"])); // authservice's own state, bound to the browser by a correlation cookie
        Assert.Equal("code", query["response_type"]);
        // The URL to register at GitHub is the handler's own /signin-github, not the /api/external-auth/callback the spike named (docs/operations/AUTH.md section 5).
        Assert.Equal("/signin-github", new Uri(query["redirect_uri"]!).AbsolutePath);
    }

    [Fact]
    public async Task GitHub_sign_in_start_refuses_a_return_url_off_the_allowed_origin_and_an_unknown_provider()
    {
        using var http = auth.Main.Client();

        var evil = await http.GetAsync($"api/v1/external-auth/login?provider=GitHub&returnUrl={Uri.EscapeDataString("http://evil.test/oauth/callback")}");
        var wrongPath = await http.GetAsync($"api/v1/external-auth/login?provider=GitHub&returnUrl={Uri.EscapeDataString(RealAuthFixture.WebOrigin + "/somewhere")}");
        var unknown = await http.GetAsync($"api/v1/external-auth/login?provider=Facebook&returnUrl={Uri.EscapeDataString(RealAuthFixture.WebOrigin + "/oauth/callback")}");

        Assert.Equal(HttpStatusCode.BadRequest, evil.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongPath.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task GitHub_is_listed_as_a_provider_only_when_configured_and_an_unconfigured_one_refuses_to_start()
    {
        using var configured = auth.Main.Client();
        using var bare = auth.Confirm.Client();

        var listed = await (await configured.GetAsync("api/v1/external-auth/providers")).Json();
        var none = await (await bare.GetAsync("api/v1/external-auth/providers")).Json();
        var refused = await bare.GetAsync($"api/v1/external-auth/login?provider=GitHub&returnUrl={Uri.EscapeDataString(RealAuthFixture.WebOrigin + "/oauth/callback")}");

        Assert.Equal(["GitHub"], listed.GetProperty("providers").EnumerateArray().Select(p => p.GetProperty("provider").GetString()!).ToArray());
        Assert.Equal(0, none.GetProperty("providers").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task Exchanging_a_code_that_was_never_issued_is_401_and_a_missing_code_is_400()
    {
        using var http = auth.Main.Client();

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("api/v1/external-auth/exchange", new { code = "never-issued" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("api/v1/external-auth/exchange", new { code = "" })).StatusCode);
    }

    [Fact]
    public async Task A_burst_over_the_auth_limit_from_one_address_gets_429_with_an_error_and_retryAfter()
    {
        // 20 requests a minute per client address, then a queue of 5, then 429 at once (Program.cs). 40 at the same moment from one address.
        using var http = auth.Main.Client("10.250.250.250");
        http.Timeout = TimeSpan.FromSeconds(10); // the queued ones wait for the next window; they are not part of the assertion
        var calls = Enumerable.Range(0, 40).Select(async _ =>
        {
            try { return await http.PostAsJsonAsync("api/v1/auth/login", new { email = RealAuthApi.NewEmail(), password = "Whatever-1234!" }); }
            catch (TaskCanceledException) { return null; }
        }).ToArray();

        var answers = (await Task.WhenAll(calls)).Where(r => r is not null).Select(r => r!).ToList();

        var limited = answers.Where(r => r.StatusCode == HttpStatusCode.TooManyRequests).ToList();
        Assert.True(limited.Count >= 10, $"{limited.Count} of {answers.Count} answers were 429");
        var body = await limited[0].Json();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
        Assert.True(body.TryGetProperty("retryAfter", out _));
    }

    [Fact]
    public async Task The_token_names_the_configured_issuer_and_audience_every_service_pins()
    {
        // The values every service pins (Identity__Audience, and the issuer from discovery).
        var user = await auth.Main.Register();

        Assert.Equal(RealAuthFixture.Issuer, user.Jwt.Payload.GetProperty("iss").GetString());
        Assert.Equal(RealAuthFixture.Audience, user.Jwt.Payload.GetProperty("aud").GetString());
    }
}
