using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Profiles;
using Gabinety.Directory.Features.Verification;
using Gabinety.Directory.Persistence;
using Gabinety.Directory.Tests.Offerings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Gabinety.Directory.Tests.Verification;

/// <summary>A clock the test moves by hand.</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>The lookup double: returns a bio or none, or throws.</summary>
public sealed class FakeGithub : IGithubProfileLookup
{
    public string? Bio; public bool Exists = true; public bool Unavailable; public int Calls;
    public Task<GithubProfile> GetBioAsync(string login, CancellationToken ct)
    {
        Calls++;
        if (Unavailable) throw new GithubUnavailableException("down");
        return Task.FromResult(Exists ? GithubProfile.Found(Bio) : GithubProfile.NotFound);
    }
}

/// <summary>Directory host with role-bearing tokens (ProfileFactory's key is private), a hand-moved clock and the lookup double.</summary>
public class VerificationFactory : OfferingFactory
{
    public const string RoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
    const string Audience = "directory-test", Issuer = "https://issuer.demo.invalid";
    readonly RSA _key = RSA.Create(2048);
    public TestClock Clock { get; } = new(DateTimeOffset.UtcNow);
    public FakeGithub Github { get; } = new();

    public HttpClient As(string? sub, string? role = null, string? email = null)
    {
        var c = CreateClient();
        if (sub is null) return c;
        var claims = new Dictionary<string, object> { ["sub"] = sub };
        if (role is not null) claims[RoleClaim] = role;
        if (email is not null) claims["email"] = email;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer, Audience = Audience, Expires = DateTime.UtcNow.AddMinutes(5), Claims = claims,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(_key) { KeyId = "k2" }, SecurityAlgorithms.RsaSha256),
        });
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<TimeProvider>(Clock);
            s.AddSingleton<IGithubProfileLookup>(Github);
            s.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                var cfg = new OpenIdConnectConfiguration { Issuer = Issuer };
                cfg.SigningKeys.Add(new RsaSecurityKey(_key) { KeyId = "k2" });
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(cfg);
            });
        });
    }

    protected override void Dispose(bool disposing) { if (disposing) _key.Dispose(); base.Dispose(disposing); }

    public async Task<T> Db<T>(Func<DirectoryDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<DirectoryDbContext>());
    }

    public Task<Practitioner> Seed(PractitionerStatus status, Action<Practitioner>? configure = null, string? userId = null) => Db(async db =>
    {
        var n = Guid.NewGuid().ToString("n");
        var p = new Practitioner
        {
            Id = Guid.NewGuid(), UserId = userId ?? "demo-" + n, Slug = "demo-" + n[..16], DisplayName = "Demo " + n[..8], Headline = "Senior .NET reviewer",
            Status = status, CreatedAt = Clock.GetUtcNow(), UpdatedAt = Clock.GetUtcNow(),
        };
        configure?.Invoke(p);
        db.Practitioners.Add(p);
        await db.SaveChangesAsync();
        return p;
    });

    public Task<List<VerificationEvent>> Events(Guid practitionerId) =>
        Db(db => db.VerificationEvents.AsNoTracking().Where(e => e.PractitionerId == practitionerId).OrderBy(e => e.At).ToListAsync());

    public Task<Practitioner> Load(Guid id) => Db(db => db.Practitioners.AsNoTracking().SingleAsync(p => p.Id == id));
}

public static class Vk
{
    public static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    public static async Task<ApiProblem> Problem(HttpResponseMessage r)
    {
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        return (await r.Content.ReadFromJsonAsync<ApiProblem>(J))!;
    }
}
