extern alias BookingApp;
extern alias DirectoryApp;
extern alias ReputationApp;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Integration.Tests.AuthContract;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using DirectoryApp::Gabinety.Directory.Clients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit.Abstractions;

namespace Gabinety.Integration.Tests.RealAuth;

/// <summary>
/// A product service (in process) configured exactly as the AppHost default profile configures it for the real authservice: <c>Identity:Authority</c> is the authservice URL and
/// <c>Identity:Audience</c> its audience, nothing else about identity. The JWKS is fetched over HTTP from the container; there is no test handler in between.
/// </summary>
public sealed class Service<TProgram>(string authority, string internalKey, Action<IServiceCollection>? configure = null) : WebApplicationFactory<TProgram> where TProgram : class
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("Identity:Authority", authority);
        builder.UseSetting("Identity:Audience", RealAuthFixture.Audience);
        builder.UseSetting("Internal:Key", internalKey);
        builder.UseSetting("Environment:Kind", "Development"); // as the AppHost sets it for every service (Mock payments are allowed only there)
        builder.UseSetting("Backend:Directory", "http://directory.invalid");
        builder.UseSetting("Backend:Booking", "http://booking.invalid");
        builder.UseSetting("Backend:Assistant", "http://assistant.invalid");
        builder.UseSetting("Internal:Keys:Directory", internalKey);
        builder.UseSetting("Internal:Keys:Booking", internalKey);
        builder.UseSetting("Internal:Keys:Assistant", internalKey);
        builder.ConfigureTestServices(s =>
        {
            s.PostConfigure<RateLimiterOptions>(o => o.GlobalLimiter = null);
            configure?.Invoke(s);
        });
    }
}

/// <summary>
/// The platform roles of the real authservice reaching the product's admin policies, and the owner's erasure sequence (docs/operations/RUNBOOK.md section 8) with a real account.
/// Directory, Booking and Reputation run in process on the real token and the real JWKS; their databases are in memory.
/// </summary>
[Trait("tier", "integration")]
[Collection(RealAuthCollection.Name)]
public sealed class RealAuthProductTests(RealAuthFixture auth, ITestOutputHelper output) : IDisposable
{
    const string Key = "demo-real-auth-internal-key-not-a-secret-0123456789";
    readonly Service<BookingApp::Program> _booking = new(auth.Main.Url, Key);
    readonly Service<DirectoryApp::Program> _directory = new(auth.Main.Url, Key);
    readonly Service<ReputationApp::Program> _reputation = new(auth.Main.Url, Key);

    public void Dispose()
    {
        _directory.Dispose();
        _booking.Dispose();
        _reputation.Dispose();
    }

    static HttpClient Client<T>(Service<T> service, string? token) where T : class
    {
        var http = service.CreateClient();
        if (token is not null) http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return http;
    }

    // The three admin surfaces of the services (docs/operations/ROUTE-AUTH.md): each is behind the "Admin" policy, role Admin or SuperAdmin.
    const string DirectoryAdmin = "/api/v1/admin/practitioners", BookingAdmin = "/api/v1/admin/disputes?status=open", ReputationAdmin = "/api/v1/admin/reviews";

    async Task<HttpStatusCode> Get(string service, string path, string? token)
    {
        using var http = service switch
        {
            "directory" => Client(_directory, token),
            "booking" => Client(_booking, token),
            _ => Client(_reputation, token),
        };
        return (await http.GetAsync(path)).StatusCode;
    }

    [Theory]
    [InlineData("directory", DirectoryAdmin)]
    [InlineData("booking", BookingAdmin)]
    [InlineData("reputation", ReputationAdmin)]
    public async Task The_admin_routes_admit_a_real_Admin_a_real_SuperAdmin_and_a_user_with_two_roles_and_refuse_everyone_else(string service, string path)
    {
        var plain = await auth.Main.Register();
        var admin = await auth.Main.UserWithRoles("Admin");
        var two = await auth.Main.UserWithRoles("Admin", "User");
        var onlyUser = await auth.Main.UserWithRoles("User");
        var owner = await auth.Main.LoginOwner(); // SuperAdmin, seeded by InitialAdmin

        Assert.Equal(HttpStatusCode.Unauthorized, await Get(service, path, null));
        Assert.Equal(HttpStatusCode.Forbidden, await Get(service, path, plain.AccessToken));
        Assert.Equal(HttpStatusCode.Forbidden, await Get(service, path, onlyUser.AccessToken)); // the platform role User is not an administrator
        Assert.Equal(HttpStatusCode.OK, await Get(service, path, admin.AccessToken));
        Assert.Equal(HttpStatusCode.OK, await Get(service, path, two.AccessToken)); // the role claim is a JSON array here
        Assert.Equal(HttpStatusCode.OK, await Get(service, path, owner.AccessToken));
        output.WriteLine($"OBSERVED {service} {path}: none=401, plain=403, User=403, Admin=200, Admin+User=200, SuperAdmin=200");
    }

    [Fact]
    public async Task Directory_me_reads_the_subject_and_the_admin_flag_from_the_real_token()
    {
        var plain = await auth.Main.Register();
        var admin = await auth.Main.UserWithRoles("Admin", "User");
        using var asPlain = Client(_directory, plain.AccessToken);
        using var asAdmin = Client(_directory, admin.AccessToken);

        var mePlain = await (await asPlain.GetAsync("/api/v1/me")).Json();
        var meAdmin = await (await asAdmin.GetAsync("/api/v1/me")).Json();

        Assert.Equal(plain.Sub, mePlain.GetProperty("userId").GetString());
        Assert.False(mePlain.GetProperty("isAdmin").GetBoolean());
        Assert.Equal(admin.Sub, meAdmin.GetProperty("userId").GetString());
        Assert.True(meAdmin.GetProperty("isAdmin").GetBoolean());
    }

    [Fact]
    public async Task Every_service_refuses_a_real_token_with_one_signature_character_changed()
    {
        var admin = await auth.Main.UserWithRoles("Admin");
        var parts = admin.AccessToken.Split('.');
        var altered = string.Join('.', parts[0], parts[1], parts[2][..10] + (parts[2][10] == 'A' ? 'B' : 'A') + parts[2][11..]);

        foreach (var (service, path) in new[] { ("directory", DirectoryAdmin), ("booking", BookingAdmin), ("reputation", ReputationAdmin) })
        {
            Assert.Equal(HttpStatusCode.OK, await Get(service, path, admin.AccessToken)); // control: the unaltered token passes
            Assert.Equal(HttpStatusCode.Unauthorized, await Get(service, path, altered));
        }
    }

    [Fact]
    public async Task Directory_sees_the_confirmed_address_through_authservice_me()
    {
        var user = await auth.Main.Register();
        using var http = Client(_directory, user.AccessToken);

        var created = await http.PostAsJsonAsync("/api/v1/me/practitioner", new { displayName = "Demo Real Auth" });

        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var me = await (await http.GetAsync("/api/v1/me/practitioner")).Json();
        Assert.True(me.GetProperty("emailVerified").GetBoolean()); // Directory's HttpEmailVerifiedLookup read emailConfirmed from the real /api/v1/auth/me
    }

    [Fact]
    public async Task The_owners_erasure_sequence_works_for_a_real_account_authservice_first_then_the_three_hooks()
    {
        // The owner's order (RUNBOOK 8): authservice deletes the account, then Directory, Booking and Reputation are told the user id. Nothing orchestrates this in code today.
        var user = await auth.Main.Register();
        // Directory's erasure first asks Booking to cancel the practitioner's future bookings (ADR-0012): here it reaches the Booking host in process, with Booking's key.
        using var directory = new Service<DirectoryApp::Program>(auth.Main.Url, Key, s =>
        {
            s.RemoveAll<IBookingClient>();
            s.AddSingleton<IBookingClient>(_ => new BookingClient(_booking.CreateClient(), Key));
        });
        using var asUser = Client(directory, user.AccessToken);
        Assert.True((await asUser.PostAsJsonAsync("/api/v1/me/practitioner", new { displayName = "Demo Erased Person" })).IsSuccessStatusCode);
        using var directoryInternal = directory.CreateClient();
        directoryInternal.DefaultRequestHeaders.Add("X-Internal-Key", Key);
        Assert.Equal(HttpStatusCode.OK, (await directoryInternal.GetAsync($"/internal/v1/practitioners/by-user/{user.Sub}")).StatusCode); // the real sub is the key Directory holds

        using var http = auth.Main.Client();
        var deleted = await http.Send(HttpMethod.Delete, "api/v1/auth/account", user.AccessToken, new { password = user.Password, confirmation = "DELETE" });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        foreach (var (name, factory) in new (string, HttpClient)[] { ("directory", directoryInternal), ("booking", Internal(_booking)), ("reputation", Internal(_reputation)) })
        {
            var hook = await factory.DeleteAsync($"/internal/v1/subjects/{user.Sub}");
            Assert.True(hook.StatusCode == HttpStatusCode.NoContent, $"{name} hook answered {(int)hook.StatusCode}");
            Assert.Equal(HttpStatusCode.NoContent, (await factory.DeleteAsync($"/internal/v1/subjects/{user.Sub}")).StatusCode); // idempotent
        }

        Assert.Equal(HttpStatusCode.NotFound, (await directoryInternal.GetAsync($"/internal/v1/practitioners/by-user/{user.Sub}")).StatusCode); // the profile no longer belongs to that id
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("api/v1/auth/login", new { email = user.Email, password = user.Password })).StatusCode);

        static HttpClient Internal<T>(Service<T> service) where T : class
        {
            var client = service.CreateClient();
            client.DefaultRequestHeaders.Add("X-Internal-Key", Key);
            return client;
        }
    }

    [Fact]
    public async Task The_hooks_refuse_a_call_without_the_internal_key()
    {
        using var booking = _booking.CreateClient();
        using var reputation = _reputation.CreateClient();
        using var directory = _directory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await booking.DeleteAsync("/internal/v1/subjects/someone")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await reputation.DeleteAsync("/internal/v1/subjects/someone")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await directory.DeleteAsync("/internal/v1/subjects/someone")).StatusCode);
    }

    [Fact]
    public async Task A_token_issued_before_a_key_rotation_is_still_accepted_and_a_new_one_is_signed_by_the_new_key()
    {
        // Rotation as docs/operations/AUTH.md section 4: the new key is Jwt__PrivateKeyPem, the old public key is Jwt__PreviousPublicKeyPem; the JWKS then lists both.
        using var oldKey = System.Security.Cryptography.RSA.Create(2048);
        var before = await auth.StartAsync("rotation-before", signingKeyPem: oldKey.ExportPkcs8PrivateKeyPem(), database: "rotationdb");
        var oldToken = await before.Register();
        await before.StopAsync();

        using var newKey = System.Security.Cryptography.RSA.Create(2048);
        var after = await auth.StartAsync("rotation-after", new Dictionary<string, string> { ["Jwt__PreviousPublicKeyPem"] = oldKey.ExportSubjectPublicKeyInfoPem() },
            signingKeyPem: newKey.ExportPkcs8PrivateKeyPem(), database: "rotationdb"); // the same database: the same users
        using var http = after.Client();

        var jwks = (await (await http.GetAsync(".well-known/jwks.json")).Json()).GetProperty("keys");
        var newToken = await after.Login(oldToken.Email, oldToken.Password);
        Assert.Equal(2, jwks.GetArrayLength());
        var oldKid = oldToken.Jwt.Header.GetProperty("kid").GetString();
        var newKid = newToken.Jwt.Header.GetProperty("kid").GetString();
        Assert.NotEqual(oldKid, newKid);
        Assert.Contains(jwks.EnumerateArray(), k => k.GetProperty("kid").GetString() == oldKid);
        Assert.True(oldToken.Jwt.VerifyRs256(jwks.EnumerateArray().Single(k => k.GetProperty("kid").GetString() == oldKid)));
        Assert.True(newToken.Jwt.VerifyRs256(jwks.EnumerateArray().Single(k => k.GetProperty("kid").GetString() == newKid)));

        // A product service pointed at the rotated authservice accepts both (the old token has up to an hour left).
        using var directory = new Service<DirectoryApp::Program>(after.Url, Key);
        using var asOld = Client(directory, oldToken.AccessToken);
        using var asNew = Client(directory, newToken.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await asOld.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await asNew.GetAsync("/api/v1/me")).StatusCode);
    }
}
