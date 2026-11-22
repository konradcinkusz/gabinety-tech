using System.Security.Claims;
using Gabinety.Web.Api;
using Gabinety.Web.Auth;
using Gabinety.Web.Hosting;
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

public class SessionCacheTests
{
    [Fact]
    public void A_sign_in_opens_an_entry_and_a_restart_is_re_seeded_from_the_cookie()
    {
        var clock = TestClock.Default();
        var cache = new SessionCache(clock);
        cache.Open("s1", "access", clock.GetUtcNow().AddHours(1), "refresh-1", new SessionIdentity("u", "a@b.c", []));
        Assert.Equal("refresh-1", cache.Find("s1")!.RefreshToken);
        Assert.Null(cache.Find("never-seen"));

        var restarted = new SessionCache(clock); // a deploy: nothing in memory, the cookie's refresh token is the seed
        var entry = restarted.GetOrSeed("s1", "refresh-1");
        Assert.Equal("refresh-1", entry.RefreshToken);
        Assert.Null(entry.AccessToken);
        Assert.Same(entry, restarted.GetOrSeed("s1", "something-else")); // the first seed wins: a second caller never replaces newer tokens
    }

    [Fact]
    public void An_ended_session_stays_ended_so_the_same_cookie_is_refused_without_asking_again()
    {
        var clock = TestClock.Default();
        var cache = new SessionCache(clock);
        cache.Open("s1", "a", clock.GetUtcNow().AddHours(1), "r", new SessionIdentity("u", null, []));
        Assert.True(cache.IsAlive("s1"));
        cache.End("s1", "r");
        Assert.False(cache.IsAlive("s1"));
        Assert.Null(cache.Find("s1")!.AccessToken);
        Assert.True(cache.IsAlive("unknown-session")); // not seen is not dead: the cookie is the session
    }

    [Fact]
    public void Entries_are_swept_after_the_cookie_lifetime_and_not_before()
    {
        var clock = TestClock.Default();
        var cache = new SessionCache(clock);
        cache.Open("old", "a", clock.GetUtcNow().AddHours(1), "r", new SessionIdentity("u", null, []));
        clock.Advance(SessionCache.Retention - TimeSpan.FromMinutes(10));
        cache.Open("mid", "a", clock.GetUtcNow().AddHours(1), "r", new SessionIdentity("u", null, []));
        Assert.Equal(2, cache.Count);
        clock.Advance(TimeSpan.FromMinutes(11));
        cache.Open("new", "a", clock.GetUtcNow().AddHours(1), "r", new SessionIdentity("u", null, []));
        Assert.Equal(2, cache.Count); // "old" is gone, "mid" and "new" remain
        Assert.Null(cache.Find("old"));
    }
}

public class UserTokensTests
{
    sealed class FixedState(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }

    /// <summary>Everything a caller of <see cref="UserTokens"/> has: the real verifier and identity client against the identity fake, an empty cache (a fresh process) and the seed of the cookie.</summary>
    sealed class Rig : IDisposable
    {
        readonly ServiceProvider _sp;
        public TestClock Clock { get; } = TestClock.Default();
        public FakeIdentity Identity { get; }
        public SessionCache Cache { get; }
        public UserTokens Tokens { get; }
        public string Sid { get; } = "sid-1";
        public ClaimsPrincipal User { get; }

        public Rig(bool signedIn = true, TimeSpan? accessLeft = null, string seed = "refresh-0", bool seeded = true)
        {
            Identity = new FakeIdentity(Clock);
            Cache = new SessionCache(Clock);
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddHttpClient(Backend.Auth, c => c.BaseAddress = new Uri("http://auth.test")).ConfigurePrimaryHttpMessageHandler(() => Identity.Backend);
            _sp = services.BuildServiceProvider();
            var verifier = new JwksTokenVerifier(_sp.GetRequiredService<IHttpClientFactory>(), new WebOptions(), Clock);
            var http = _sp.GetRequiredService<IHttpClientFactory>().CreateClient(Backend.Auth);
            var identity = new IdentityClient(http, new VisitorAddressAccessor(new Microsoft.AspNetCore.Http.HttpContextAccessor()));
            User = signedIn
                ? new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-1"), new Claim(SessionCookie.SessionClaim, Sid)], "Cookies"))
                : new ClaimsPrincipal(new ClaimsIdentity());
            if (signedIn && accessLeft is { } left && seeded)
                Cache.Open(Sid, Identity.Issue(), Clock.GetUtcNow() + left, seed, new SessionIdentity("user-1", null, []));
            Tokens = new UserTokens(new FixedState(User), new SeededSeed(seeded ? seed : null), Cache, identity, verifier, Clock);
        }

        public void Dispose() => _sp.Dispose();
    }

    /// <summary>The seed of a request whose cookie carries <c>seed</c>.</summary>
    sealed class SeededSeed(string? token) : ISessionSeed
    {
        public Task<string?> RefreshTokenAsync() => Task.FromResult(token);
        public Task CaptureAsync() => Task.CompletedTask;
    }

    [Fact]
    public async Task A_fresh_cached_token_is_returned_without_a_call_to_the_identity_service()
    {
        using var rig = new Rig(accessLeft: TimeSpan.FromHours(1));
        var token = await rig.Tokens.AccessTokenAsync();
        Assert.Equal(rig.Cache.Find(rig.Sid)!.AccessToken, token);
        Assert.Equal(0, rig.Identity.Backend.Calls);
    }

    [Fact]
    public async Task After_a_deploy_the_access_token_is_minted_from_the_cookies_refresh_token()
    {
        using var rig = new Rig(); // an empty process: only the cookie's refresh token is known
        var token = await rig.Tokens.AccessTokenAsync();
        Assert.StartsWith("eyJ", token);
        Assert.Equal(1, rig.Identity.RefreshCalls);
        Assert.Equal("refresh-1", rig.Cache.Find(rig.Sid)!.RefreshToken); // the rotated token is kept for the cookie
        Assert.NotNull(rig.Cache.Find(rig.Sid)!.Identity); // the claims of the new token, for the cookie that is re-issued
    }

    [Fact] public async Task Nobody_signed_in_has_no_token() { using var rig = new Rig(signedIn: false); Assert.Null(await rig.Tokens.AccessTokenAsync()); Assert.Equal(0, rig.Identity.Backend.Calls); }

    [Fact] public async Task A_signed_in_user_without_a_refresh_token_has_no_token() { using var rig = new Rig(seeded: false); Assert.Null(await rig.Tokens.AccessTokenAsync()); Assert.Equal(0, rig.Identity.Backend.Calls); }

    [Fact]
    public async Task A_token_about_to_expire_is_refreshed_and_the_session_gets_the_new_pair()
    {
        using var rig = new Rig(accessLeft: TimeSpan.FromSeconds(10));
        var old = rig.Cache.Find(rig.Sid)!.AccessToken;
        var token = await rig.Tokens.AccessTokenAsync();
        Assert.NotEqual(old, token);
        Assert.Equal(1, rig.Identity.RefreshCalls);
        var now = rig.Cache.Find(rig.Sid)!;
        Assert.Equal(token, now.AccessToken);
        Assert.Equal("refresh-1", now.RefreshToken); // the refresh token is single use: the new one is kept
        Assert.True(now.AccessExpiresAt > rig.Clock.GetUtcNow().AddMinutes(30));
    }

    [Fact]
    public async Task Concurrent_callers_cause_one_refresh_because_the_refresh_token_is_single_use()
    {
        using var rig = new Rig(accessLeft: TimeSpan.FromSeconds(5));
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => rig.Tokens.AccessTokenAsync())));
        Assert.Equal(1, rig.Identity.RefreshCalls);
        Assert.Single(results.Distinct());
    }

    [Fact]
    public async Task Concurrent_callers_of_a_restarted_process_spend_the_cookies_refresh_token_once()
    {
        using var rig = new Rig(); // the deploy case: 16 requests with the same cookie at once
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => rig.Tokens.AccessTokenAsync())));
        Assert.Equal(1, rig.Identity.RefreshCalls);
        Assert.All(results, r => Assert.NotNull(r));
        Assert.Single(results.Distinct());
    }

    [Fact]
    public async Task Two_circuits_and_a_request_of_one_session_share_one_rotation_and_each_rotation_uses_the_newest_token()
    {
        using var rig = new Rig(accessLeft: TimeSpan.FromSeconds(5));
        await rig.Tokens.AccessTokenAsync();
        rig.Clock.Advance(TimeSpan.FromMinutes(61)); // the new token has expired too
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => rig.Tokens.AccessTokenAsync())));
        Assert.Equal(2, rig.Identity.RefreshCalls);
        Assert.Equal(["refresh-0", "refresh-1"], rig.Identity.SpentRefreshTokens); // each token was spent exactly once, in order
    }

    [Fact]
    public async Task A_refused_refresh_ends_the_session_and_gives_no_token()
    {
        using var rig = new Rig(accessLeft: TimeSpan.FromSeconds(5));
        rig.Identity.RefuseRefresh = true;
        Assert.Null(await rig.Tokens.AccessTokenAsync());
        Assert.False(rig.Cache.IsAlive(rig.Sid));
        Assert.Null(await rig.Tokens.AccessTokenAsync());
        Assert.Equal(1, rig.Identity.RefreshCalls); // a dead session is not asked about again
    }

    [Fact]
    public async Task An_identity_service_that_is_down_keeps_the_session_and_the_token_while_it_still_lasts()
    {
        using var rig = new Rig(accessLeft: TimeSpan.FromSeconds(20));
        rig.Identity.Down = true;
        var token = await rig.Tokens.AccessTokenAsync();
        Assert.Equal(rig.Cache.Find(rig.Sid)!.AccessToken, token);
        Assert.True(rig.Cache.IsAlive(rig.Sid));
        rig.Clock.Advance(TimeSpan.FromSeconds(25)); // now expired and the service is still away
        Assert.Null(await rig.Tokens.AccessTokenAsync());
        Assert.True(rig.Cache.IsAlive(rig.Sid)); // not signed out because of an outage
        rig.Identity.Down = false;
        rig.Clock.Advance(UserTokens.RetryAfterOutage);
        Assert.NotNull(await rig.Tokens.AccessTokenAsync()); // and it recovers on its own
    }

    [Theory]
    [InlineData(429)] [InlineData(500)] [InlineData(503)]
    public async Task A_429_or_5xx_on_refresh_does_not_end_the_session_and_is_not_retried_at_once(int status)
    {
        using var rig = new Rig(accessLeft: TimeSpan.FromSeconds(5));
        rig.Identity.RefreshStatus = status;
        await rig.Tokens.AccessTokenAsync();
        await rig.Tokens.AccessTokenAsync();
        Assert.Equal(1, rig.Identity.RefreshCalls); // the second caller inside the pause did not hammer the service
        Assert.True(rig.Cache.IsAlive(rig.Sid));
        Assert.Equal("refresh-0", rig.Cache.Find(rig.Sid)!.RefreshToken); // nothing was spent
        rig.Identity.RefreshStatus = 0;
        rig.Clock.Advance(UserTokens.RetryAfterOutage);
        Assert.NotNull(await rig.Tokens.AccessTokenAsync());
        Assert.Equal(2, rig.Identity.RefreshCalls);
        Assert.Equal("refresh-1", rig.Cache.Find(rig.Sid)!.RefreshToken);
    }

    [Fact]
    public async Task A_refresh_that_rotated_but_whose_access_token_cannot_be_verified_still_keeps_the_new_refresh_token()
    {
        using var rig = new Rig(accessLeft: TimeSpan.FromSeconds(5));
        using var attacker = FakeIdentity.NewKey();
        rig.Identity.AccessTokenSigner = attacker; // the new access token will not verify
        Assert.Null(await rig.Tokens.AccessTokenAsync());
        Assert.Equal("refresh-1", rig.Cache.Find(rig.Sid)!.RefreshToken); // the old one is spent: losing the new one would end the session
        Assert.True(rig.Cache.IsAlive(rig.Sid));
    }

    [Fact]
    public async Task A_circuit_that_outlives_the_session_finds_no_token()
    {
        using var rig = new Rig(accessLeft: TimeSpan.FromHours(1));
        rig.Cache.End(rig.Sid, "refresh-0");
        Assert.Null(await rig.Tokens.AccessTokenAsync());
    }
}

public class SessionRevalidationTests
{
    sealed class Probe(SessionCache cache, TestClock clock) : SessionRevalidatingAuthenticationStateProvider(NullLoggerFactory.Instance, cache, clock, Options.Create(new CircuitRevalidationOptions()))
    {
        public Task<bool> Validate(ClaimsPrincipal p) => ValidateAuthenticationStateAsync(new AuthenticationState(p), default);
        public TimeSpan Interval => RevalidationInterval;
    }

    static ClaimsPrincipal User(TestClock clock, string sid = "s1") =>
        SessionCookie.Principal(sid, new SessionIdentity("u", "a@b.c", ["Admin"]), clock.GetUtcNow() + SessionCookie.Lifetime);

    [Fact]
    public async Task A_circuit_stays_authenticated_while_its_session_lives_and_ends_when_the_cookie_would_have_expired()
    {
        var clock = TestClock.Default();
        var provider = new Probe(new SessionCache(clock), clock);
        var user = User(clock);
        Assert.True(await provider.Validate(user));
        clock.Advance(SessionCookie.Lifetime - TimeSpan.FromSeconds(1));
        Assert.True(await provider.Validate(user));
        clock.Advance(TimeSpan.FromSeconds(1)); // the cookie has expired; the websocket has not noticed
        Assert.False(await provider.Validate(user));
        Assert.Equal(TimeSpan.FromMinutes(1), provider.Interval);
    }

    [Fact]
    public async Task A_circuit_ends_when_its_session_was_ended_by_a_sign_out_or_a_refused_refresh()
    {
        var clock = TestClock.Default();
        var cache = new SessionCache(clock);
        var user = User(clock);
        Assert.True(await new Probe(cache, clock).Validate(user));
        cache.End("s1", "r");
        Assert.False(await new Probe(cache, clock).Validate(user));
    }

    [Fact] public async Task A_visitor_without_a_session_is_never_signed_out() => Assert.True(await new Probe(new SessionCache(TestClock.Default()), TestClock.Default()).Validate(new ClaimsPrincipal(new ClaimsIdentity())));

    [Fact] public async Task A_signed_in_principal_without_a_session_claim_is_not_valid() => Assert.False(await new Probe(new SessionCache(TestClock.Default()), TestClock.Default()).Validate(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "x")], "Cookies"))));
}
