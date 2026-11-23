using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.DevIdentity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Integration.Tests.AuthContract;

/// <summary>
/// T-11.06: forgot, reset and change password. Wire shapes and status codes only, never the wording, so the same facts hold on DevIdentity and on the real image.
/// The reset token itself cannot be read from the real image (it goes out by e-mail), so what needs a valid token lives in <see cref="DevIdentityPasswordTests"/>.
/// </summary>
public abstract partial class AuthContractSuite
{
    protected Task<HttpResponseMessage> ForgotAsync(string email) => Http.PostAsJsonAsync("api/v1/auth/forgot-password", new { email });

    protected Task<HttpResponseMessage> ResetAsync(string email, string token, string newPassword) =>
        Http.PostAsJsonAsync("api/v1/auth/reset-password", new { email, token, newPassword });

    protected async Task<HttpResponseMessage> ChangeAsync(string? accessToken, string currentPassword, string newPassword)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/change-password") { Content = JsonContent.Create(new { currentPassword, newPassword }) };
        if (accessToken is not null) request.Headers.Authorization = new("Bearer", accessToken);
        return await Http.SendAsync(request);
    }

    protected static void AssertErrorsList(JsonElement body)
    {
        Assert.Equal(JsonValueKind.Array, body.GetProperty("errors").ValueKind);
        Assert.True(body.GetProperty("errors").GetArrayLength() > 0);
    }

    protected const string NewPassword = "Contract-Pass-456!";

    [ContractFact]
    public async Task Forgot_password_answers_a_known_and_an_unknown_address_byte_for_byte_alike()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var known = await ForgotAsync(email);
        var unknown = await ForgotAsync(NewEmail());

        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        var body = await known.Content.ReadAsStringAsync();
        Assert.Equal(body, await unknown.Content.ReadAsStringAsync()); // no account-existence oracle
        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal(JsonValueKind.String, json.GetProperty("message").ValueKind);
        Assert.Equal(JsonValueKind.False, json.GetProperty("isOAuthOnly").ValueKind); // an account with a password
    }

    [ContractFact]
    public async Task Reset_password_with_a_token_that_was_never_issued_is_400_with_an_errors_list()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var response = await ResetAsync(email, "demo-never-issued-token", NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertErrorsList(await ReadJson(response));
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, Password)).StatusCode); // the password did not change
    }

    [ContractFact]
    public async Task Reset_password_for_an_unknown_address_is_400_with_an_errors_list()
    {
        var response = await ResetAsync(NewEmail(), "demo-never-issued-token", NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertErrorsList(await ReadJson(response));
    }

    [ContractFact]
    public async Task Change_password_needs_a_bearer_token()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await ChangeAsync(null, Password, NewPassword)).StatusCode);
    }

    [ContractFact]
    public async Task Change_password_with_a_wrong_current_password_is_400_and_changes_nothing()
    {
        var email = NewEmail();
        var tokens = await RegisterAsync(email);

        var response = await ChangeAsync(tokens.GetProperty("accessToken").GetString()!, Password + "-wrong", NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertErrorsList(await ReadJson(response));
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, NewPassword)).StatusCode);
    }

    [ContractFact]
    public async Task Change_password_with_a_new_password_that_breaks_the_rules_is_400_with_an_errors_list()
    {
        var tokens = await RegisterAsync(NewEmail());

        var response = await ChangeAsync(tokens.GetProperty("accessToken").GetString()!, Password, "password-without-the-rest");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertErrorsList(await ReadJson(response));
    }

    [ContractFact]
    public async Task Change_password_returns_fresh_tokens_ends_the_old_sessions_and_switches_the_password()
    {
        var email = NewEmail();
        var first = await RegisterAsync(email);
        var oldRefresh = first.GetProperty("refreshToken").GetString()!;

        var response = await ChangeAsync(first.GetProperty("accessToken").GetString()!, Password, NewPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJson(response);
        Assert.Equal(JsonValueKind.String, body.GetProperty("message").ValueKind);
        Assert.True(body.GetProperty("sessionsRevoked").GetBoolean());
        var fresh = body.GetProperty("tokens");
        AssertTokenResponse(fresh);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(oldRefresh)).StatusCode); // the other sessions ended
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(fresh.GetProperty("refreshToken").GetString()!)).StatusCode); // this one survives
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, Password)).StatusCode);
    }
}

/// <summary>What only the stand-in can do: read the reset token from the dev route, and so prove single use, expiry and the effects of a reset.</summary>
public class DevIdentityPasswordTests(DevIdentityHost host) : AuthContractSuite(host.CreateClient()), IClassFixture<DevIdentityHost>
{
    private async Task<string> IssueTokenAsync(string email)
    {
        Assert.Equal(HttpStatusCode.OK, (await ForgotAsync(email)).StatusCode);
        var response = await Http.GetAsync($"dev/last-reset-token?email={Uri.EscapeDataString(email)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJson(response)).GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task A_reset_token_works_once_signs_in_with_the_new_password_and_revokes_the_refresh_tokens()
    {
        var email = NewEmail();
        var tokens = await RegisterAsync(email);
        var token = await IssueTokenAsync(email);

        var reset = await ResetAsync(email, token, NewPassword);

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(JsonValueKind.String, (await ReadJson(reset)).GetProperty("message").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tokens.GetProperty("refreshToken").GetString()!)).StatusCode);

        var again = await ResetAsync(email, token, NewPassword + "x"); // single use
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        AssertErrorsList(await ReadJson(again));
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, NewPassword)).StatusCode); // the second attempt changed nothing
    }

    [Fact]
    public async Task A_token_is_bound_to_its_address_and_a_newer_token_replaces_the_older()
    {
        var email = NewEmail();
        var other = NewEmail();
        await RegisterAsync(email);
        await RegisterAsync(other);
        var older = await IssueTokenAsync(email);
        var newer = await IssueTokenAsync(email);

        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(other, newer, NewPassword)).StatusCode); // somebody else's address
        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(email, older, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ResetAsync(email, newer, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task A_password_that_breaks_the_rules_is_400_and_leaves_the_token_usable()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var token = await IssueTokenAsync(email);

        var weak = await ResetAsync(email, token, "password-without-the-rest");

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        AssertErrorsList(await ReadJson(weak));
        Assert.Equal(HttpStatusCode.OK, (await ResetAsync(email, token, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Forgot_for_an_unknown_address_issues_no_token()
    {
        var email = NewEmail();
        await ForgotAsync(email);

        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync($"dev/last-reset-token?email={Uri.EscapeDataString(email)}")).StatusCode);
    }

    [Fact]
    public async Task Malformed_requests_are_400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await ForgotAsync("not-an-address")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(NewEmail(), "", NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(NewEmail(), "t", "short")).StatusCode);
    }

    [Fact]
    public void A_reset_token_expires_after_its_lifetime()
    {
        const string email = "demo-expiry@example.test";
        var clock = new ManualClock();
        var store = new IdentityStore(clock);
        store.TryAdd(email, Password, [], null, out _);

        var inTime = store.IssueResetToken(email)!;
        clock.Advance(IdentityStore.ResetTokenLifetime - TimeSpan.FromSeconds(1));
        Assert.Empty(store.ResetPassword(email, inTime, NewPassword, _ => []));

        var late = store.IssueResetToken(email)!;
        clock.Advance(IdentityStore.ResetTokenLifetime);
        Assert.NotEmpty(store.ResetPassword(email, late, NewPassword + "x", _ => []));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}

/// <summary>The dev route exists only under the fake profile: with the flag on a issued token is served, with it off the same request is a 404 (the process itself refuses to start without GABINETY_FAKE=1).</summary>
public class DevResetRouteGateTests
{
    private const string Email = "demo-gate@example.test";

    private static async Task<HttpStatusCode> StatusAsync(bool fakeProfile)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(sp => new IdentityStore(sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton(sp => new TokenIssuer(sp.GetRequiredService<TimeProvider>(), "AuthService", "AuthService"));
        await using var app = builder.Build();
        app.MapDevIdentityEndpoints(null, fakeProfile);
        var store = app.Services.GetRequiredService<IdentityStore>();
        store.TryAdd(Email, "Contract-Pass-123!", [], null, out _);
        Assert.NotNull(store.IssueResetToken(Email)); // a token exists in both cases
        await app.StartAsync();
        using var http = app.GetTestClient();
        return (await http.GetAsync($"dev/last-reset-token?email={Uri.EscapeDataString(Email)}")).StatusCode;
    }

    [Fact]
    public async Task With_the_fake_profile_the_route_serves_the_token() => Assert.Equal(HttpStatusCode.OK, await StatusAsync(true));

    [Fact]
    public async Task Without_the_fake_profile_the_route_is_a_404_even_when_a_token_exists() => Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(false));
}
