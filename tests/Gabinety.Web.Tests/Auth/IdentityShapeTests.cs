using System.Net;
using System.Security.Claims;
using Gabinety.Web.Api;
using Gabinety.Web.Auth;
using Microsoft.AspNetCore.Http;

namespace Gabinety.Web.Tests.Sessions;

/// <summary>The identity client against canned answers with the REAL shapes of authservice v0.3.4 (docs/spikes/authservice.md); DevIdentity answers the same paths and shapes.</summary>
public class IdentityShapeTests
{
    static (IdentityClient Client, FakeBackend Backend) Rig(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var backend = new FakeBackend { Respond = respond };
        return (new IdentityClient(new HttpClient(backend) { BaseAddress = new Uri("http://auth.test") }, new VisitorAddressAccessor(new HttpContextAccessor())), backend);
    }

    const string Tokens = "{\"accessToken\":\"a.b.c\",\"refreshToken\":\"r-1\",\"expiresIn\":3600,\"tokenType\":\"Bearer\"}";

    [Fact]
    public async Task Login_200_with_a_TokenResponse_is_tokens()
    {
        var (c, b) = Rig(_ => FakeBackend.Raw(Tokens));
        var r = await c.LoginAsync("a@demo.example", "pw");
        Assert.Equal((IdentityOutcome.Tokens, "a.b.c", "r-1", 3600), (r.Outcome, r.AccessToken, r.RefreshToken, r.ExpiresInSeconds));
        Assert.Equal(("POST", "/api/v1/auth/login"), (b.Requests[0].Method.Method, b.Requests[0].PathAndQuery));
        Assert.Contains("\"email\":\"a@demo.example\"", b.Requests[0].Body);
    }

    [Fact] public async Task Login_200_with_a_two_factor_challenge_is_a_state_not_a_session() => Assert.Equal(IdentityOutcome.TwoFactor, (await Rig(_ => FakeBackend.Raw("{\"requiresTwoFactor\":true,\"challengeToken\":\"x\",\"expiresIn\":300}")).Client.LoginAsync("a", "b")).Outcome);

    [Theory]
    [InlineData(401, "{\"message\":\"Invalid email or password.\"}", IdentityOutcome.Unauthorized)]
    [InlineData(403, "{\"emailVerificationRequired\":true}", IdentityOutcome.VerificationRequired)]
    [InlineData(403, "{}", IdentityOutcome.Rejected)]
    [InlineData(400, "{\"errors\":[\"x\"]}", IdentityOutcome.Rejected)]
    [InlineData(202, "{\"userId\":\"u\",\"email\":\"a@b\",\"message\":\"m\",\"emailVerificationRequired\":true}", IdentityOutcome.Accepted)]
    [InlineData(429, "{\"message\":\"Too many requests\"}", IdentityOutcome.RateLimited)]
    [InlineData(401, "{\"error\":\"Account is temporarily locked\",\"lockedOut\":true,\"lockoutEnd\":\"2026-12-01T10:00:00Z\"}", IdentityOutcome.LockedOut)]
    [InlineData(401, "{\"error\":\"Invalid\",\"lockedOut\":false}", IdentityOutcome.Unauthorized)]
    [InlineData(401, "not json", IdentityOutcome.Unauthorized)]
    [InlineData(500, "oops", IdentityOutcome.Unavailable)]
    [InlineData(503, "", IdentityOutcome.Unavailable)]
    [InlineData(200, "not json", IdentityOutcome.Unavailable)]
    [InlineData(200, "{\"accessToken\":1}", IdentityOutcome.Unavailable)]
    public async Task Every_documented_answer_is_a_named_state(int status, string body, IdentityOutcome expected)
    {
        var (c, _) = Rig(_ => FakeBackend.Raw(body, (HttpStatusCode)status));
        Assert.Equal(expected, (await c.LoginAsync("a", "b")).Outcome);
    }

    [Fact]
    public async Task The_visitors_validated_address_goes_to_authservice_in_Fly_Client_IP_and_never_a_forged_one()
    {
        var backend = new FakeBackend { Respond = _ => FakeBackend.Raw(Tokens) };
        string? seen = null;
        var capture = new HeaderProbe(h => seen = h);
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["Fly-Client-IP"] = "203.0.113.7";
        var accessor = new HttpContextAccessor { HttpContext = ctx };
        var client = new IdentityClient(new HttpClient(capture) { BaseAddress = new Uri("http://auth.test") }, new VisitorAddressAccessor(accessor));
        await client.LoginAsync("a", "b");
        Assert.Equal("203.0.113.7", seen);
        ctx.Request.Headers["Fly-Client-IP"] = "not-an-ip";
        ctx.Request.Headers["X-Forwarded-For"] = "also bad";
        seen = "unset";
        await client.LoginAsync("a", "b");
        Assert.True(seen is null or "unset" || System.Net.IPAddress.TryParse(seen, out _)); // never the garbage
        Assert.NotEqual("not-an-ip", seen);
        _ = backend;
    }

    sealed class HeaderProbe(Action<string?> look) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            look(r.Headers.TryGetValues(IdentityClient.ClientIpHeader, out var v) ? string.Join(",", v) : null);
            return Task.FromResult(FakeBackend.Raw(Tokens));
        }
    }

    [Fact] public async Task A_429_on_refresh_does_not_end_the_session()
    {
        var (c, _) = Rig(_ => FakeBackend.Raw("{}", (HttpStatusCode)429));
        Assert.Equal(IdentityOutcome.RateLimited, (await c.RefreshAsync("r")).Outcome); // UserTokens closes a session only on Unauthorized/Rejected
    }

    [Fact] public async Task A_down_identity_service_is_unavailable() => Assert.Equal(IdentityOutcome.Unavailable, (await Rig(FakeBackend.Throw(new HttpRequestException("x"))).Client.LoginAsync("a", "b")).Outcome);

    [Fact]
    public async Task Registration_asks_for_the_current_consent_versions_and_sends_them_and_202_is_confirmation_pending()
    {
        var (c, b) = Rig(r => r.RequestUri!.AbsolutePath.EndsWith("consents/versions") ? FakeBackend.Raw("{\"terms\":\"2026-01-01\",\"privacy\":\"2026-02-02\",\"cookies\":\"2026-01-01\"}") : FakeBackend.Raw("{\"emailVerificationRequired\":true}", HttpStatusCode.Accepted));
        var r = await c.RegisterAsync("new@demo.example", "Passw0rd-long", locale: "pl");
        Assert.Equal(IdentityOutcome.Accepted, r.Outcome);
        Assert.Equal("/api/v1/auth/consents/versions", b.Requests[0].PathAndQuery);
        Assert.Contains("\"acceptedTermsVersion\":\"2026-01-01\"", b.Requests[1].Body);
        Assert.Contains("\"acceptedPrivacyVersion\":\"2026-02-02\"", b.Requests[1].Body);
    }

    [Fact] public async Task Registration_without_known_consent_versions_is_unavailable_and_sends_nothing() { var (c, b) = Rig(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)); Assert.Equal(IdentityOutcome.Unavailable, (await c.RegisterAsync("a@b.c", "x")).Outcome); Assert.Single(b.Requests); }

    [Fact]
    public async Task The_exchange_posts_the_code_and_reads_tokens_or_a_challenge()
    {
        var (c, b) = Rig(_ => FakeBackend.Raw(Tokens));
        Assert.Equal(IdentityOutcome.Tokens, (await c.ExchangeAsync("one-time")).Outcome);
        Assert.Equal("/api/v1/external-auth/exchange", b.Requests[0].PathAndQuery);
        Assert.Contains("\"code\":\"one-time\"", b.Requests[0].Body);
        Assert.Equal(IdentityOutcome.Unauthorized, (await Rig(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)).Client.ExchangeAsync("used")).Outcome);
    }

    [Fact] public async Task Logout_sends_the_bearer_and_never_throws() { var (c, _) = Rig(FakeBackend.Throw(new HttpRequestException("x"))); Assert.False(await c.LogoutAsync("tok")); }
}

/// <summary>The single class that reads claims (CONTRACTS 1). The role claim is the long URI; one role is a string, several repeat the claim.</summary>
public class ClaimMappingTests
{
    static Claim[] C(params (string, string)[] c) => [.. c.Select(x => new Claim(x.Item1, x.Item2))];

    [Fact]
    public void Subject_email_and_roles_are_read_from_the_spike_names()
    {
        var claims = C(("sub", "u-1"), ("email", "a@demo.example"), (ClaimMapping.RoleClaimUri, "Admin"), (ClaimMapping.RoleClaimUri, "SuperAdmin"), (ClaimMapping.RoleClaimUri, "Admin"));
        Assert.Equal(("u-1", "a@demo.example"), (ClaimMapping.Subject(claims), ClaimMapping.Email(claims)));
        Assert.Equal(["Admin", "SuperAdmin"], ClaimMapping.Roles(claims));
    }

    [Fact] public void The_framework_claim_types_work_too() { var c = C((ClaimTypes.NameIdentifier, "u-2"), (ClaimTypes.Email, "b@x"), (ClaimTypes.Role, "Admin"), ("role", "Moderator")); Assert.Equal(("u-2", "b@x"), (ClaimMapping.Subject(c), ClaimMapping.Email(c))); Assert.Equal(["Admin", "Moderator"], ClaimMapping.Roles(c)); }

    [Fact] public void Nothing_is_invented() { var c = C(("jti", "x")); Assert.Null(ClaimMapping.Subject(c)); Assert.Null(ClaimMapping.Email(c)); Assert.Empty(ClaimMapping.Roles(c)); Assert.False(ClaimMapping.IsAdmin(ClaimMapping.Roles(c))); }

    [Theory] [InlineData("Admin", true)] [InlineData("SuperAdmin", true)] [InlineData("admin", false)] [InlineData("Moderator", false)] [InlineData("AdminX", false)]
    public void Only_the_two_platform_roles_are_admin(string role, bool admin) => Assert.Equal(admin, ClaimMapping.IsAdmin([role]));

    [Fact] public void A_cookie_principal_is_admin_through_the_same_rule() => Assert.True(ClaimMapping.IsAdmin(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "Cookies"))));
}
