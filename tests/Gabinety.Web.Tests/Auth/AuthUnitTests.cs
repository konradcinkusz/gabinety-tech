using System.Security.Claims;
using Gabinety.Web.Api;
using Gabinety.Web.Auth;
using Gabinety.Web.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Gabinety.Web.Tests.Sessions;

/// <summary>A verifier wired like the host: the "auth" client with the identity fake as its primary handler, the real resilience handler included.</summary>
sealed class VerifierRig : IDisposable
{
    readonly ServiceProvider _sp;
    public FakeIdentity Identity { get; }
    public TestClock Clock { get; } = TestClock.Default();
    public JwksTokenVerifier Verifier { get; }

    public VerifierRig(string? issuer = null, string? audience = null)
    {
        Identity = new FakeIdentity(Clock);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddHttpClient(Backend.Auth, c => c.BaseAddress = new Uri("http://auth.test")).ConfigurePrimaryHttpMessageHandler(() => Identity.Backend);
        _sp = services.BuildServiceProvider();
        Verifier = new JwksTokenVerifier(_sp.GetRequiredService<IHttpClientFactory>(), new WebOptions { IdentityIssuer = issuer, IdentityAudience = audience }, Clock);
    }

    public void Dispose() => _sp.Dispose();
}

public class TokenVerifierTests
{
    [Fact]
    public async Task A_token_signed_by_the_identity_service_is_valid_and_its_claims_are_read()
    {
        using var rig = new VerifierRig();
        var (outcome, token) = await rig.Verifier.VerifyAsync(rig.Identity.Issue("u-7", "demo-admin@demo.example", ["Admin", "SuperAdmin"]), default);
        Assert.Equal(VerifyOutcome.Valid, outcome);
        Assert.Equal("u-7", token!.Subject);
        Assert.Equal("demo-admin@demo.example", token.Email);
        Assert.Equal(["Admin", "SuperAdmin"], token.Roles);
        Assert.Equal(rig.Clock.GetUtcNow() + TimeSpan.FromHours(1), token.ExpiresAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_forged_token_with_a_valid_looking_payload_is_rejected_because_the_signature_is_verified_not_just_decoded()
    {
        // FRONTEND-BFF section 4: a verifier that only decodes accepts any base64 payload with a future expiry.
        using var rig = new VerifierRig();
        using var attacker = FakeIdentity.NewKey();
        var forged = rig.Identity.Issue("u-1", roles: ["Admin"], signWith: attacker);
        Assert.Equal(VerifyOutcome.Invalid, (await rig.Verifier.VerifyAsync(forged, default)).Outcome);

        var parts = rig.Identity.Issue().Split('.');
        var tampered = $"{parts[0]}.{Convert.ToBase64String("{\"sub\":\"u-1\",\"email\":\"x@x\",\"exp\":9999999999}"u8.ToArray()).TrimEnd('=')}.{parts[2]}";
        Assert.Equal(VerifyOutcome.Invalid, (await rig.Verifier.VerifyAsync(tampered, default)).Outcome);
    }

    [Theory]
    [InlineData("")] [InlineData("not a token")] [InlineData("a.b.c")]
    public async Task Garbage_is_invalid(string raw) { using var rig = new VerifierRig(); Assert.Equal(VerifyOutcome.Invalid, (await rig.Verifier.VerifyAsync(raw, default)).Outcome); }

    [Fact]
    public async Task An_unsigned_token_is_rejected()
    {
        using var rig = new VerifierRig();
        var header = Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = Base64UrlEncoder.Encode("{\"sub\":\"u-1\",\"exp\":9999999999}");
        Assert.Equal(VerifyOutcome.Invalid, (await rig.Verifier.VerifyAsync($"{header}.{payload}.", default)).Outcome);
    }

    [Fact]
    public async Task An_expired_token_is_rejected_and_a_token_inside_the_skew_is_not()
    {
        using var rig = new VerifierRig();
        Assert.Equal(VerifyOutcome.Invalid, (await rig.Verifier.VerifyAsync(rig.Identity.Issue(lifetime: TimeSpan.FromSeconds(-30)), default)).Outcome);
        Assert.Equal(VerifyOutcome.Valid, (await rig.Verifier.VerifyAsync(rig.Identity.Issue(lifetime: TimeSpan.FromSeconds(-2)), default)).Outcome);
    }

    [Fact]
    public async Task Issuer_and_audience_are_checked_when_configured_and_not_when_they_are_not()
    {
        using var strict = new VerifierRig("AuthService", "gabinety");
        Assert.Equal(VerifyOutcome.Invalid, (await strict.Verifier.VerifyAsync(strict.Identity.Issue(audience: "other-app"), default)).Outcome);
        Assert.Equal(VerifyOutcome.Invalid, (await strict.Verifier.VerifyAsync(strict.Identity.Issue(audience: "gabinety", issuer: "evil"), default)).Outcome);
        Assert.Equal(VerifyOutcome.Valid, (await strict.Verifier.VerifyAsync(strict.Identity.Issue(audience: "gabinety"), default)).Outcome);
        using var loose = new VerifierRig();
        Assert.Equal(VerifyOutcome.Valid, (await loose.Verifier.VerifyAsync(loose.Identity.Issue(audience: "anything", issuer: "anyone"), default)).Outcome);
    }

    [Fact]
    public async Task A_rotated_key_is_picked_up_once_and_not_hammered()
    {
        using var rig = new VerifierRig();
        Assert.Equal(VerifyOutcome.Valid, (await rig.Verifier.VerifyAsync(rig.Identity.Issue(), default)).Outcome); // caches kid-1
        rig.Identity.Rotate("kid-2");
        rig.Clock.Advance(TimeSpan.FromSeconds(31)); // past the minimum gap between key fetches
        var fetches = rig.Identity.Backend.Calls;
        Assert.Equal(VerifyOutcome.Valid, (await rig.Verifier.VerifyAsync(rig.Identity.Issue(), default)).Outcome);
        Assert.Equal(fetches + 1, rig.Identity.Backend.Calls);

        // a token with an id nobody published refetches at most every 30 s
        using var other = FakeIdentity.NewKey();
        var unknown = rig.Identity.Issue(signWith: other, kid: "kid-unknown");
        Assert.Equal(VerifyOutcome.Invalid, (await rig.Verifier.VerifyAsync(unknown, default)).Outcome);
        var after = rig.Identity.Backend.Calls;
        Assert.Equal(VerifyOutcome.Invalid, (await rig.Verifier.VerifyAsync(unknown, default)).Outcome);
        Assert.Equal(after, rig.Identity.Backend.Calls);
    }

    [Fact]
    public async Task An_identity_service_that_cannot_be_reached_is_unavailable_not_invalid_and_cached_keys_keep_working()
    {
        using var rig = new VerifierRig();
        rig.Identity.Down = true;
        Assert.Equal(VerifyOutcome.Unavailable, (await rig.Verifier.VerifyAsync(rig.Identity.Issue(), default)).Outcome);
        rig.Identity.Down = false;
        Assert.Equal(VerifyOutcome.Valid, (await rig.Verifier.VerifyAsync(rig.Identity.Issue(), default)).Outcome);
        rig.Identity.Down = true;
        rig.Clock.Advance(TimeSpan.FromMinutes(10)); // the cache is stale and the refetch fails: the last known keys are used
        Assert.Equal(VerifyOutcome.Valid, (await rig.Verifier.VerifyAsync(rig.Identity.Issue(), default)).Outcome);
    }
}

public class ServerSessionStoreTests
{
    static AuthenticationTicket Ticket(string sid, DateTimeOffset? expires) =>
        new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ServerSessionStore.SessionClaim, sid)], "Cookies")), new AuthenticationProperties { ExpiresUtc = expires }, "Cookies");

    static SessionTokens Tokens(TestClock c, string at = "at", string rt = "rt") => new(at, rt, c.GetUtcNow().AddHours(1));

    [Fact]
    public async Task A_sign_in_opens_a_session_and_the_cookie_handler_stores_the_ticket_under_the_same_key()
    {
        var clock = TestClock.Default();
        var store = new ServerSessionStore(clock);
        var sid = store.Open(Tokens(clock));
        Assert.True(sid.Length >= 43); // 32 random bytes
        var key = await store.StoreAsync(Ticket(sid, clock.GetUtcNow().AddDays(7)));
        Assert.Equal(sid, key);
        Assert.NotNull(await store.RetrieveAsync(key));
        Assert.Equal("at", store.TokensOf(sid)!.AccessToken);
    }

    [Fact] public async Task A_ticket_whose_session_was_never_opened_is_refused() => await Assert.ThrowsAsync<InvalidOperationException>(() => new ServerSessionStore(TestClock.Default()).StoreAsync(Ticket("made-up", null)));

    [Fact]
    public async Task The_session_ends_with_the_ticket_at_the_same_moment_as_the_cookie()
    {
        var clock = TestClock.Default();
        var store = new ServerSessionStore(clock);
        var sid = store.Open(Tokens(clock));
        await store.StoreAsync(Ticket(sid, clock.GetUtcNow().AddDays(7)));
        clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));
        Assert.True(store.IsAlive(sid));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(store.IsAlive(sid));
        Assert.Null(store.TokensOf(sid));
        Assert.Null(await store.RetrieveAsync(sid));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task A_session_that_was_opened_but_never_signed_in_does_not_linger()
    {
        var clock = TestClock.Default();
        var store = new ServerSessionStore(clock);
        store.Open(Tokens(clock));
        clock.Advance(TimeSpan.FromMinutes(2));
        store.Open(Tokens(clock)); // opening sweeps
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task Removing_a_session_removes_its_tokens_with_it()
    {
        var clock = TestClock.Default();
        var store = new ServerSessionStore(clock);
        var sid = store.Open(Tokens(clock));
        await store.StoreAsync(Ticket(sid, clock.GetUtcNow().AddDays(7)));
        await store.RemoveAsync(sid);
        Assert.Null(store.TokensOf(sid));
        Assert.Null(await store.RetrieveAsync(sid));
    }
}

public class UserTokensTests
{
    sealed class FixedState(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }

    sealed class Rig : IDisposable
    {
        readonly ServiceProvider _sp;
        public TestClock Clock { get; } = TestClock.Default();
        public FakeIdentity Identity { get; }
        public ServerSessionStore Store { get; }
        public UserTokens Tokens { get; }
        public string Sid { get; }

        public Rig(bool signedIn = true, TimeSpan? accessLeft = null)
        {
            Identity = new FakeIdentity(Clock);
            Store = new ServerSessionStore(Clock);
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddHttpClient(Backend.Auth, c => c.BaseAddress = new Uri("http://auth.test")).ConfigurePrimaryHttpMessageHandler(() => Identity.Backend);
            _sp = services.BuildServiceProvider();
            var web = new WebOptions();
            var verifier = new JwksTokenVerifier(_sp.GetRequiredService<IHttpClientFactory>(), web, Clock);
            var http = _sp.GetRequiredService<IHttpClientFactory>().CreateClient(Backend.Auth);
            var identity = new IdentityClient(http, new VisitorAddressAccessor(new Microsoft.AspNetCore.Http.HttpContextAccessor()));
            Sid = Store.Open(new SessionTokens(Identity.Issue(), "refresh-0", Clock.GetUtcNow() + (accessLeft ?? TimeSpan.FromHours(1))));
            var user = signedIn
                ? new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-1"), new Claim(ServerSessionStore.SessionClaim, Sid)], "Cookies"))
                : new ClaimsPrincipal(new ClaimsIdentity());
            Tokens = new UserTokens(new FixedState(user), Store, identity, verifier, Clock);
            if (signedIn) Store.StoreAsync(new AuthenticationTicket(user, new AuthenticationProperties { ExpiresUtc = Clock.GetUtcNow().AddDays(7) }, "Cookies")).GetAwaiter().GetResult();
        }

        public void Dispose() => _sp.Dispose();
    }

    [Fact]
    public async Task A_fresh_token_is_returned_without_a_call_to_the_identity_service()
    {
        using var rig = new Rig();
        var token = await rig.Tokens.AccessTokenAsync();
        Assert.Equal(rig.Store.TokensOf(rig.Sid)!.AccessToken, token);
        Assert.Equal(0, rig.Identity.Backend.Calls);
    }

    [Fact] public async Task Nobody_signed_in_has_no_token() { using var rig = new Rig(signedIn: false); Assert.Null(await rig.Tokens.AccessTokenAsync()); }

    [Fact]
    public async Task A_token_about_to_expire_is_refreshed_and_the_session_gets_the_new_pair()
    {
        using var rig = new Rig(accessLeft: TimeSpan.FromSeconds(10));
        var old = rig.Store.TokensOf(rig.Sid)!;
        var token = await rig.Tokens.AccessTokenAsync();
        Assert.NotEqual(old.AccessToken, token);
        Assert.Equal(1, rig.Identity.RefreshCalls);
        var now = rig.Store.TokensOf(rig.Sid)!;
        Assert.Equal(token, now.AccessToken);
        Assert.NotEqual(old.RefreshToken, now.RefreshToken); // the refresh token is single use: the new one is kept
        Assert.True(now.AccessExpiresAt > rig.Clock.GetUtcNow().AddMinutes(30));
    }

    [Fact]
    public async Task Concurrent_callers_cause_one_refresh_because_the_refresh_token_is_single_use()
    {
        using var rig = new Rig(accessLeft: TimeSpan.FromSeconds(5));
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => rig.Tokens.AccessTokenAsync()));
        Assert.Equal(1, rig.Identity.RefreshCalls);
        Assert.Single(results.Distinct());
    }

    [Fact]
    public async Task A_refused_refresh_ends_the_session_and_gives_no_token()
    {
        using var rig = new Rig(accessLeft: TimeSpan.FromSeconds(5));
        rig.Identity.RefuseRefresh = true;
        Assert.Null(await rig.Tokens.AccessTokenAsync());
        Assert.False(rig.Store.IsAlive(rig.Sid));
    }

    [Fact]
    public async Task An_identity_service_that_is_down_keeps_the_session_and_the_token_while_it_still_lasts()
    {
        using var rig = new Rig(accessLeft: TimeSpan.FromSeconds(20));
        rig.Identity.Down = true;
        var token = await rig.Tokens.AccessTokenAsync();
        Assert.Equal(rig.Store.TokensOf(rig.Sid)!.AccessToken, token);
        Assert.True(rig.Store.IsAlive(rig.Sid));
        rig.Clock.Advance(TimeSpan.FromSeconds(25)); // now expired and the service is still away
        Assert.Null(await rig.Tokens.AccessTokenAsync());
        Assert.True(rig.Store.IsAlive(rig.Sid)); // not signed out because of an outage
    }

    [Fact]
    public async Task A_circuit_that_outlives_the_session_finds_no_token()
    {
        using var rig = new Rig();
        rig.Store.Close(rig.Sid);
        Assert.Null(await rig.Tokens.AccessTokenAsync());
    }
}

public class SessionRevalidationTests
{
    sealed class Probe(ServerSessionStore store) : SessionRevalidatingAuthenticationStateProvider(NullLoggerFactory.Instance, store, Options.Create(new CircuitRevalidationOptions()))
    {
        public Task<bool> Validate(ClaimsPrincipal p) => ValidateAuthenticationStateAsync(new AuthenticationState(p), default);
        public TimeSpan Interval => RevalidationInterval;
    }

    [Fact]
    public async Task A_circuit_stays_authenticated_while_its_session_lives_and_ends_when_it_does_not()
    {
        var clock = TestClock.Default();
        var store = new ServerSessionStore(clock);
        var sid = store.Open(new SessionTokens("a", "r", clock.GetUtcNow().AddHours(1)));
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ServerSessionStore.SessionClaim, sid)], "Cookies"));
        await store.StoreAsync(new AuthenticationTicket(user, new AuthenticationProperties { ExpiresUtc = clock.GetUtcNow().AddDays(7) }, "Cookies"));
        var provider = new Probe(store);

        Assert.True(await provider.Validate(user));
        clock.Advance(TimeSpan.FromDays(8)); // the cookie has expired; the websocket has not noticed
        Assert.False(await provider.Validate(user));
        Assert.Equal(TimeSpan.FromMinutes(1), provider.Interval);
    }

    [Fact] public async Task A_visitor_without_a_session_is_never_signed_out() => Assert.True(await new Probe(new ServerSessionStore(TestClock.Default())).Validate(new ClaimsPrincipal(new ClaimsIdentity())));

    [Fact] public async Task A_signed_in_principal_without_a_session_claim_is_not_valid() => Assert.False(await new Probe(new ServerSessionStore(TestClock.Default())).Validate(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "x")], "Cookies"))));
}
