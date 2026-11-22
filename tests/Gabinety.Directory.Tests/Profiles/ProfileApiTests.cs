using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Profiles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Directory.Tests.Profiles;

public sealed class FakeEmail : IEmailVerifiedLookup
{
    public bool Verified = true;
    public Task<bool> IsVerifiedAsync(string bearerToken, CancellationToken ct) => Task.FromResult(Verified);
}

/// <summary>The Booking double of the suspension and erasure cascade: records the calls, can fail, and can look at the world at the moment of the call.</summary>
public sealed class FakeBooking : Gabinety.Directory.Clients.IBookingClient
{
    public readonly List<Guid> Calls = [];
    public bool Down;
    public Func<Guid, Task>? AtCall;
    public async Task CancelFutureBookingsAsync(Guid practitionerId, CancellationToken ct)
    {
        lock (Calls) Calls.Add(practitionerId);
        if (AtCall is not null) await AtCall(practitionerId);
        if (Down) throw new Gabinety.Directory.Clients.BookingUnavailableException();
    }
}

/// <summary>Directory host with an in-memory RSA key as its JWKS (same approach as SkeletonFactory, which is sealed) and a fake e-mail lookup.</summary>
public class ProfileFactory : WebApplicationFactory<Program>
{
    const string Audience = "directory-test", Issuer = "https://issuer.demo.invalid";
    readonly RSA _rsa = RSA.Create(2048);
    public FakeEmail Email { get; } = new();
    public FakeBooking Booking { get; } = new();
    protected virtual string Provider => "InMemory";
    protected virtual string? ConnectionString => null;

    public string Token(string sub) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer, Audience = Audience, Expires = DateTime.UtcNow.AddMinutes(5),
        Claims = new Dictionary<string, object> { ["sub"] = sub },
        SigningCredentials = new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = "k1" }, SecurityAlgorithms.RsaSha256),
    });

    public HttpClient ClientFor(string sub)
    {
        var c = CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(sub));
        return c;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Identity:Audience", Audience);
        builder.UseSetting("Database:Provider", Provider);
        if (ConnectionString != null) builder.UseSetting("ConnectionStrings:directorydb", ConnectionString);
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<IEmailVerifiedLookup>(Email);
            s.AddSingleton<Gabinety.Directory.Clients.IBookingClient>(Booking);
            s.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                var cfg = new OpenIdConnectConfiguration { Issuer = Issuer };
                cfg.SigningKeys.Add(new RsaSecurityKey(_rsa) { KeyId = "k1" });
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(cfg);
            });
        });
    }

    protected override void Dispose(bool disposing) { if (disposing) _rsa.Dispose(); base.Dispose(disposing); }
}

public class ProfileApiTests(ProfileFactory f) : IClassFixture<ProfileFactory>
{
    static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    const string Url = "/api/v1/me/practitioner";

    HttpClient As(string sub) => f.ClientFor(sub);
    static string Id() => "demo-" + Guid.NewGuid().ToString("n");

    static async Task<ApiProblem> Problem(HttpResponseMessage r)
    {
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        return (await r.Content.ReadFromJsonAsync<ApiProblem>(J))!;
    }

    static readonly string LongBio = new('b', 220);
    static UpdatePractitionerRequest Full(string? nip = "5260250995") => new("Demo Name", "Senior .NET reviewer", LongBio,
        "https://portfolio.example.test/me", "https://meet.google.com/abc-defg-hij", nip);

    [Fact]
    public async Task Create_returns_201_draft_with_generated_slug()
    {
        var r = await As(Id()).PostAsJsonAsync(Url, new CreatePractitionerRequest("Zażółć Gęślą"));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var p = (await r.Content.ReadFromJsonAsync<PractitionerPrivate>(J))!;
        Assert.Equal(PractitionerStatus.Draft, p.Status);
        Assert.StartsWith("zazolc-gesla", p.Slug);
    }

    [Fact]
    public async Task Duplicate_create_is_409_practitioner_exists()
    {
        var c = As(Id());
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync(Url, new CreatePractitionerRequest("Demo One"))).StatusCode);
        var r = await c.PostAsJsonAsync(Url, new CreatePractitionerRequest("Demo One"));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("practitioner_exists", (await Problem(r)).Code);
    }

    [Fact]
    public async Task Slug_collision_gets_a_numeric_suffix_also_for_an_explicit_slug()
    {
        var name = "Kolizja " + Guid.NewGuid().ToString("n")[..6];
        var a = (await (await As(Id()).PostAsJsonAsync(Url, new CreatePractitionerRequest(name))).Content.ReadFromJsonAsync<PractitionerPrivate>(J))!;
        var b = (await (await As(Id()).PostAsJsonAsync(Url, new CreatePractitionerRequest(name))).Content.ReadFromJsonAsync<PractitionerPrivate>(J))!;
        var c = (await (await As(Id()).PostAsJsonAsync(Url, new CreatePractitionerRequest("Inny", a.Slug))).Content.ReadFromJsonAsync<PractitionerPrivate>(J))!;
        Assert.Equal(a.Slug + "-2", b.Slug);
        Assert.Equal(a.Slug + "-3", c.Slug); // -2 is taken by b
    }

    [Fact]
    public async Task Invalid_explicit_slug_is_422()
    {
        var r = await As(Id()).PostAsJsonAsync(Url, new CreatePractitionerRequest("Demo Two", "Bad Slug"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var p = await Problem(r);
        Assert.Equal("validation_failed", p.Code);
        Assert.Contains("slug", p.Errors!.Keys);
    }

    [Fact]
    public async Task Get_without_a_row_is_404_and_a_second_user_cannot_read_the_first_profile()
    {
        var first = As(Id());
        await first.PostAsJsonAsync(Url, new CreatePractitionerRequest("Prywatny Profil"));
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync(Url)).StatusCode);
        var r = await As(Id()).GetAsync(Url);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("not_found", (await Problem(r)).Code);
    }

    [Fact]
    public async Task Put_replaces_fields_and_stores_nip_validity()
    {
        var c = As(Id());
        await c.PostAsJsonAsync(Url, new CreatePractitionerRequest("Demo Put"));
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync(Url, Full())).StatusCode);
        var p = (await c.GetFromJsonAsync<PractitionerPrivate>(Url, J))!;
        Assert.True(p.NipValid);
        Assert.Equal("5260250995", p.Nip);
        Assert.Equal("Senior .NET reviewer", p.Headline);
    }

    [Theory]
    [InlineData("5260250996")]
    [InlineData("123")]
    public async Task Bad_nip_is_422_with_errors_nip(string nip)
    {
        var c = As(Id());
        await c.PostAsJsonAsync(Url, new CreatePractitionerRequest("Demo Nip"));
        var r = await c.PutAsJsonAsync(Url, Full(nip));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var p = await Problem(r);
        Assert.Equal("validation_failed", p.Code);
        Assert.Contains("nip", p.Errors!.Keys);
    }

    [Theory]
    [InlineData("http://portfolio.example.test/x", "https://meet.google.com/a")]
    [InlineData("https://127.0.0.1/x", "https://meet.google.com/a")]
    [InlineData("https://localhost/x", "https://meet.google.com/a")]
    [InlineData("https://portfolio.example.test/x", "https://evil.example.test/a")]
    public async Task Bad_urls_are_422(string portfolio, string meeting)
    {
        var c = As(Id());
        await c.PostAsJsonAsync(Url, new CreatePractitionerRequest("Demo Url"));
        var r = await c.PutAsJsonAsync(Url, Full() with { PortfolioUrl = portfolio, MeetingUrlDefault = meeting });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("validation_failed", (await Problem(r)).Code);
    }

    [Fact]
    public async Task Meeting_subdomain_of_an_allowed_host_is_accepted()
    {
        var c = As(Id());
        await c.PostAsJsonAsync(Url, new CreatePractitionerRequest("Demo Sub"));
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync(Url, Full() with { MeetingUrlDefault = "https://eu.zoom.us/j/1" })).StatusCode);
    }

    [Theory]
    [InlineData("napisz: jan@example.test")]
    [InlineData("zadzwon 600 100 200")]
    [InlineData("znajdz @jan_dev")]
    public async Task Contact_details_in_headline_are_422(string headline)
    {
        var c = As(Id());
        await c.PostAsJsonAsync(Url, new CreatePractitionerRequest("Demo Contact"));
        var r = await c.PutAsJsonAsync(Url, Full() with { Headline = headline + " plus tekst" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("contact_details_not_allowed", (await Problem(r)).Code);
    }

    [Fact]
    public async Task Submit_with_unmet_items_is_422_listing_them()
    {
        var c = As(Id());
        f.Email.Verified = false;
        try
        {
            await c.PostAsJsonAsync(Url, new CreatePractitionerRequest("Demo Submit"));
            var r = await c.PostAsync(Url + "/submit", null);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            var p = await Problem(r);
            Assert.Equal("submit_incomplete", p.Code);
            var unmet = JsonSerializer.Deserialize<SubmitIncomplete>(await ExtensionJson(r), J)!.UnmetItems;
            Assert.Equal(["headline", "bio", "offering", "github_linked", "nip_valid", "portfolioUrl", "email_verified"], unmet);
        }
        finally { f.Email.Verified = true; }
    }

    static async Task<string> ExtensionJson(HttpResponseMessage r)
    {
        using var d = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return d.RootElement.GetProperty("unmetItems") is var e ? $"{{\"unmetItems\":{e.GetRawText()}}}" : "";
    }

    [Fact]
    public async Task Submit_when_not_Draft_or_Rejected_is_409_invalid_transition()
    {
        // Only Draft and Rejected may submit. No endpoint yet moves a profile to Submitted, so seed one directly.
        var id = Id();
        await As(id).PostAsJsonAsync(Url, new CreatePractitionerRequest("Demo Seeded"));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Gabinety.Directory.Persistence.DirectoryDbContext>();
            db.Practitioners.Single(x => x.UserId == id).Status = PractitionerStatus.Approved;
            await db.SaveChangesAsync();
        }
        var r = await As(id).PostAsync(Url + "/submit", null);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("invalid_transition", (await Problem(r)).Code);
    }

    [Fact]
    public async Task Unauthenticated_is_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync(Url)).StatusCode);

    [Fact]
    public async Task Nip_change_on_an_approved_profile_returns_it_to_Submitted()
    {
        var id = Id();
        var c = As(id);
        await c.PostAsJsonAsync(Url, new CreatePractitionerRequest("Demo Approved"));
        await c.PutAsJsonAsync(Url, Full());
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Gabinety.Directory.Persistence.DirectoryDbContext>();
            db.Practitioners.Single(x => x.UserId == id).Status = PractitionerStatus.Approved;
            await db.SaveChangesAsync();
        }
        await c.PutAsJsonAsync(Url, Full("1234563218"));
        var p = (await c.GetFromJsonAsync<PractitionerPrivate>(Url, J))!;
        Assert.Equal(PractitionerStatus.Submitted, p.Status);
    }
}
