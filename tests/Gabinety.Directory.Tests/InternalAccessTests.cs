using System.Net;
using Gabinety.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gabinety.Directory.Tests;

/// <summary>F-03 (T-10.10): the public-edge guard for /internal/*, per-caller internal keys and the startup validation of key strength.</summary>
public class InternalAccessTests
{
    // 43 base64url characters, synthetic.
    const string KeyA = "demo-AAAAbbbbCCCCddddEEEEffffGGGGhhhh_12345";
    const string KeyB = "demo-ZZZZyyyyXXXXwwwwVVVVuuuuTTTTssss-67890";
    const string KeyC = "demo-QQQQppppOOOOnnnnMMMMllllKKKKjjjj_abcde";

    static async Task<WebApplication> Host(Dictionary<string, string?> settings, string environment = "Production", Func<HttpRequest, bool>? publicPath = null)
    {
        var b = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        b.WebHost.UseTestServer();
        foreach (var (k, v) in settings) b.Configuration[k] = v;
        b.Services.AddAuthentication().AddInternalKeyAuthentication();
        b.Services.AddAuthorization(o => o.AddPolicy("internal", p => p.AddAuthenticationSchemes(Security.InternalScheme).RequireAuthenticatedUser()));
        var app = b.Build();
        app.UsePublicEdgeGuard(publicPath);
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/internal/v1/x", (HttpContext c) => "secret:" + c.User.FindFirst("caller")?.Value).RequireAuthorization("internal");
        app.MapGet("/internal/v1/test/clock", () => "fake").RequireAuthorization("internal");
        app.MapGet("/api/v1/read", () => "public");
        app.MapGet("/admin-ish", () => "other");
        await app.StartAsync();
        return app;
    }

    static HttpRequestMessage Get(string path, string? key = null, params (string, string)[] headers)
    {
        var r = new HttpRequestMessage(HttpMethod.Get, path);
        if (key != null) r.Headers.Add("X-Internal-Key", key);
        foreach (var (n, v) in headers) r.Headers.Add(n, v);
        return r;
    }

    static Dictionary<string, string?> Keyed() => new() { ["Internal:Callers:Booking"] = KeyA, ["Internal:Callers:Reputation"] = KeyB };

    // ---- the public-edge guard ----

    [Theory]
    [InlineData("Fly-Client-IP", "203.0.113.9")]
    [InlineData("X-Forwarded-For", "203.0.113.9")]
    [InlineData("Fly-Forwarded-Proto", "https")]
    [InlineData("Fly-Forwarded-Port", "443")]
    [InlineData("X-Forwarded-Proto", "https")]
    public async Task Internal_route_from_the_public_edge_is_404_even_with_a_valid_key(string header, string value)
    {
        await using var app = await Host(Keyed());
        var res = await app.GetTestClient().SendAsync(Get("/internal/v1/x", KeyA, (header, value)));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Fake_profile_test_endpoints_are_also_refused_from_the_public_edge()
    {
        await using var app = await Host(Keyed());
        Assert.Equal(HttpStatusCode.NotFound, (await app.GetTestClient().SendAsync(Get("/internal/v1/test/clock", KeyA, ("Fly-Client-IP", "203.0.113.9")))).StatusCode);
    }

    [Fact]
    public async Task Path_matching_is_case_insensitive_and_segment_based()
    {
        await using var app = await Host(Keyed());
        var c = app.GetTestClient();
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Get("/INTERNAL/v1/x", KeyA, ("Fly-Client-IP", "203.0.113.9")))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Get("/internal", KeyA, ("Fly-Client-IP", "203.0.113.9")))).StatusCode);
    }

    [Fact]
    public async Task Internal_route_without_edge_headers_works_with_the_key()
    {
        await using var app = await Host(Keyed());
        var res = await app.GetTestClient().SendAsync(Get("/internal/v1/x", KeyA));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Other_routes_are_not_touched_by_the_edge_guard_when_no_path_list_is_given()
    {
        await using var app = await Host(Keyed());
        Assert.Equal(HttpStatusCode.OK, (await app.GetTestClient().SendAsync(Get("/api/v1/read", null, ("Fly-Client-IP", "203.0.113.9")))).StatusCode);
    }

    [Fact]
    public async Task The_fake_profile_default_is_off_so_localhost_callers_and_the_seeder_work()
    {
        var s = Keyed(); s["GABINETY_FAKE"] = "1";
        await using var app = await Host(s);
        Assert.Equal(HttpStatusCode.OK, (await app.GetTestClient().SendAsync(Get("/internal/v1/x", KeyA, ("Fly-Client-IP", "203.0.113.9")))).StatusCode);
    }

    [Fact]
    public async Task The_setting_can_switch_the_guard_off_and_on_explicitly()
    {
        var off = Keyed(); off["Internal:RejectPublicEdge"] = "false";
        await using (var a = await Host(off))
            Assert.Equal(HttpStatusCode.OK, (await a.GetTestClient().SendAsync(Get("/internal/v1/x", KeyA, ("Fly-Client-IP", "1.2.3.4")))).StatusCode);
        var on = Keyed(); on["GABINETY_FAKE"] = "1"; on["Internal:RejectPublicEdge"] = "true";
        await using var b = await Host(on);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetTestClient().SendAsync(Get("/internal/v1/x", KeyA, ("Fly-Client-IP", "1.2.3.4")))).StatusCode);
    }

    [Fact]
    public async Task The_header_list_is_configurable()
    {
        var s = Keyed(); s["Internal:PublicEdgeHeaders"] = "X-Edge-Only";
        await using var app = await Host(s);
        var c = app.GetTestClient();
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Get("/internal/v1/x", KeyA, ("X-Edge-Only", "1")))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Get("/internal/v1/x", KeyA, ("Fly-Client-IP", "1.2.3.4")))).StatusCode); // not in the list now
    }

    [Fact]
    public async Task A_public_path_list_serves_only_those_paths_from_the_edge()
    {
        await using var app = await Host(Keyed(), publicPath: r => r.Path.StartsWithSegments("/api/v1"));
        var c = app.GetTestClient();
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Get("/api/v1/read", null, ("Fly-Client-IP", "1.2.3.4")))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Get("/admin-ish", null, ("Fly-Client-IP", "1.2.3.4")))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Get("/internal/v1/x", KeyA, ("Fly-Client-IP", "1.2.3.4")))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Get("/admin-ish"))).StatusCode); // private network: everything is served
    }

    // ---- per-caller keys ----

    [Fact]
    public async Task Each_caller_key_is_accepted_and_the_caller_is_a_claim()
    {
        await using var app = await Host(Keyed());
        var c = app.GetTestClient();
        Assert.Equal("secret:Booking", await (await c.SendAsync(Get("/internal/v1/x", KeyA))).Content.ReadAsStringAsync());
        Assert.Equal("secret:Reputation", await (await c.SendAsync(Get("/internal/v1/x", KeyB))).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unknown_or_missing_key_is_401()
    {
        await using var app = await Host(Keyed());
        var c = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Get("/internal/v1/x", KeyC))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Get("/internal/v1/x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Get("/internal/v1/x", KeyA + "x"))).StatusCode);
    }

    [Fact]
    public async Task The_single_callee_key_still_works_for_the_fake_profile_and_tests()
    {
        await using var app = await Host(new() { ["Internal:Key"] = "k-test-1234" }, environment: "Development");
        Assert.Equal(HttpStatusCode.OK, (await app.GetTestClient().SendAsync(Get("/internal/v1/x", "k-test-1234"))).StatusCode);
    }

    // ---- startup validation ----

    static Task<Exception?> Start(Dictionary<string, string?> settings, string environment = "Production") =>
        Host(settings, environment).ContinueWith(t => t.Exception?.GetBaseException());

    [Theory]
    [InlineData("short")]
    [InlineData("k-internal-DO-NOT-LEAK-5678")]                       // 27 chars
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]  // long, no variety
    [InlineData("demo-AAAAbbbbCCCCddddEEEEffffGGGGhhhh 12345678901")] // a space is not in the alphabet
    public async Task A_weak_key_stops_the_start_outside_the_fake_profile_and_the_message_has_no_key(string weak)
    {
        var ex = await Start(new() { ["Internal:Callers:Booking"] = weak });
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("Internal:Callers:Booking", ex!.Message);
        Assert.DoesNotContain(weak, ex.Message);
    }

    [Fact]
    public async Task A_weak_outgoing_or_callee_key_also_stops_the_start()
    {
        Assert.IsType<InvalidOperationException>(await Start(new() { ["Internal:Keys:Directory"] = "short" }));
        Assert.IsType<InvalidOperationException>(await Start(new() { ["Internal:Key"] = "short" }));
    }

    [Fact]
    public async Task Two_callers_sharing_a_key_stop_the_start()
    {
        var ex = await Start(new() { ["Internal:Callers:Booking"] = KeyA, ["Internal:Callers:Reputation"] = KeyA });
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("Booking", ex!.Message);
        Assert.Contains("Reputation", ex.Message);
        Assert.DoesNotContain(KeyA, ex.Message);
    }

    [Fact]
    public async Task One_caller_using_one_key_for_two_callees_stops_the_start()
    {
        Assert.IsType<InvalidOperationException>(await Start(new() { ["Internal:Keys:Directory"] = KeyA, ["Internal:Keys:Booking"] = KeyA }));
    }

    [Fact]
    public async Task The_callee_key_equal_to_a_caller_key_stops_the_start()
    {
        Assert.IsType<InvalidOperationException>(await Start(new() { ["Internal:Key"] = KeyA, ["Internal:Callers:Booking"] = KeyA }));
    }

    [Fact]
    public async Task Strong_distinct_keys_start()
    {
        Assert.Null(await Start(new() { ["Internal:Callers:Booking"] = KeyA, ["Internal:Callers:Reputation"] = KeyB, ["Internal:Keys:Directory"] = KeyC }));
        // a 64-character hex key and an openssl rand -base64 32 value
        Assert.Null(await Start(new() { ["Internal:Callers:Booking"] = "9f3a7c1e5b2d48a6c0e9d7b3f1a58426e0c9b7d5a3f19e8c6b4d2a07f5e3c1b9", ["Internal:Callers:Reputation"] = "0123456789abcdefABCDEF+/0123456789abcdefABCDEF1=" }));
    }

    [Fact]
    public async Task The_fake_profile_and_development_keep_their_documented_dev_key()
    {
        Assert.Null(await Start(new() { ["Internal:Key"] = "demo-development-internal-key-not-a-secret", ["Internal:Keys:Directory"] = "demo-development-internal-key-not-a-secret", ["GABINETY_FAKE"] = "1" }));
        Assert.Null(await Start(new() { ["Internal:Key"] = "k" }, "Development"));
    }

    [Fact]
    public async Task Enforcement_can_be_forced_in_development_by_setting()
    {
        Assert.IsType<InvalidOperationException>(await Start(new() { ["Internal:Key"] = "k", ["Internal:EnforceKeyStrength"] = "true" }, "Development"));
    }
}

public class InternalCallerMatchTests
{
    static Microsoft.Extensions.Configuration.IConfiguration Cfg(params (string, string?)[] kv) =>
        new ConfigurationBuilder().AddInMemoryCollection(kv.Select(p => new KeyValuePair<string, string?>(p.Item1, p.Item2))).Build();

    [Fact]
    public void Caller_names_the_matching_key_and_shared_is_the_single_key()
    {
        var c = Cfg(("Internal:Callers:Booking", "bk"), ("Internal:Callers:Reputation", "rp"), ("Internal:Key", "one"), ("Internal:PreviousKey", "old"));
        Assert.Equal("Booking", InternalKey.Caller(c, "bk"));
        Assert.Equal("Reputation", InternalKey.Caller(c, "rp"));
        Assert.Equal("shared", InternalKey.Caller(c, "one"));
        Assert.Equal("shared", InternalKey.Caller(c, "old"));
        Assert.Null(InternalKey.Caller(c, "other"));
        Assert.Null(InternalKey.Caller(c, ""));
    }

    [Fact]
    public void A_previous_key_without_a_current_key_never_opens_the_door_but_callers_do()
    {
        var c = Cfg(("Internal:PreviousKey", "old"), ("Internal:Callers:Booking", "bk"));
        Assert.Null(InternalKey.Caller(c, "old"));
        Assert.Equal("Booking", InternalKey.Caller(c, "bk"));
    }

    [Fact]
    public void A_blank_caller_key_never_matches_a_blank_header()
    {
        var c = Cfg(("Internal:Callers:Booking", ""));
        Assert.False(InternalKey.Configured(c));
        Assert.Null(InternalKey.Caller(c, ""));
    }

    [Fact]
    public void A_second_name_gives_a_rotation_overlap()
    {
        var c = Cfg(("Internal:Callers:Booking", "old"), ("Internal:Callers:BookingNext", "new"));
        Assert.Equal("Booking", InternalKey.Caller(c, "old"));
        Assert.Equal("BookingNext", InternalKey.Caller(c, "new"));
    }
}
