extern alias DirectoryApp;

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Gabinety.Integration.Tests.ThinSlice;

/// <summary>
/// First real result (DECISIONS D-20 a): a token issued by the REAL authservice image is accepted by Directory, and the same token with one
/// altered signature character is rejected. The image is pinned by digest (docs/spikes/authservice.md section 4).
/// </summary>
[Trait("tier", "integration")]
public class ThinSliceTests(Xunit.Abstractions.ITestOutputHelper output) : IAsyncLifetime
{
    // ghcr.io/konradcinkusz/authservice:v0.3.4, multi-platform index digest read from the registry on 2026-11-16.
    private const string Image = "ghcr.io/konradcinkusz/authservice@sha256:d0309a94f6b6300d4b8091381786c160ae7717a022416debaaa6d8d19a244645";
    private const string Issuer = "gabinety-thinslice";
    private const string Audience = "gabinety";

    private INetwork _network = null!;
    private PostgreSqlContainer _pg = null!;
    private IContainer _auth = null!;
    private string _authUrl = null!;
    private WebApplicationFactory<DirectoryApp::Program> _directory = null!;

    public async Task InitializeAsync()
    {
        _network = new NetworkBuilder().Build();
        await _network.CreateAsync();
        _pg = new PostgreSqlBuilder("postgres:17-alpine").WithNetwork(_network).WithNetworkAliases("pg").WithDatabase("authdb").Build();
        await _pg.StartAsync();

        // The signing key exists only for this run, in memory (never a file in the repository).
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem();

        _auth = new ContainerBuilder(Image)
            .WithNetwork(_network)
            .WithPortBinding(8080, true)
            .WithEnvironment("ConnectionStrings__DefaultConnection", $"Host=pg;Port=5432;Database=authdb;Username={PostgreSqlBuilder.DefaultUsername};Password={PostgreSqlBuilder.DefaultPassword}")
            .WithEnvironment("DATABASE_PROVIDER", "PostgreSQL")
            .WithEnvironment("Jwt__Algorithm", "RS256")
            .WithEnvironment("Jwt__PrivateKeyPem", pem)
            .WithEnvironment("Jwt__Issuer", Issuer)
            .WithEnvironment("Jwt__Audience", Audience)
            .WithEnvironment("Auth__RequireConfirmedEmail", "false")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPath("/health/ready").ForPort(8080)))
            .Build();
        await _auth.StartAsync();
        _authUrl = $"http://{_auth.Hostname}:{_auth.GetMappedPublicPort(8080)}";

        _directory = new WebApplicationFactory<DirectoryApp::Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Identity:Authority", _authUrl);
            b.UseSetting("Identity:Audience", Audience);
        });
    }

    public async Task DisposeAsync()
    {
        _directory?.Dispose();
        if (_auth != null) await _auth.DisposeAsync();
        if (_pg != null) await _pg.DisposeAsync();
        if (_network != null) await _network.DisposeAsync();
    }

    private async Task<(string accessToken, string subject)> RegisterAndLogin()
    {
        using var http = new HttpClient { BaseAddress = new Uri(_authUrl) };
        var versions = await http.GetFromJsonAsync<JsonElement>("/api/v1/auth/consents/versions");
        var email = $"demo-{Guid.NewGuid():N}@example.test";
        var password = "Demo-pass-1234!";
        var reg = await http.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email, password,
            acceptedTermsVersion = versions.GetProperty("terms").GetString(),
            acceptedPrivacyVersion = versions.GetProperty("privacy").GetString(),
        });
        output.WriteLine($"OBSERVED register status: {(int)reg.StatusCode}");
        Assert.True(reg.IsSuccessStatusCode, $"register answered {(int)reg.StatusCode}: {await reg.Content.ReadAsStringAsync()}");

        var login = await http.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        var claims = JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement;
        var sub = claims.GetProperty("sub").GetString()!;
        // Names and JSON kinds only, never values (the e-mail is synthetic but values are not needed).
        output.WriteLine("OBSERVED claims: " + string.Join(", ", claims.EnumerateObject().Select(p => $"{p.Name}:{p.Value.ValueKind}")));
        var h = token.Split('.')[0].Replace('-', '+').Replace('_', '/');
        h = h.PadRight(h.Length + (4 - h.Length % 4) % 4, '=');
        var header = JsonDocument.Parse(Convert.FromBase64String(h)).RootElement;
        output.WriteLine("OBSERVED header: " + string.Join(", ", header.EnumerateObject().Select(p => p.Name == "kid" ? "kid:present" : $"{p.Name}:{p.Value}")));
        var disco = await http.GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration");
        output.WriteLine($"OBSERVED discovery issuer={disco.GetProperty("issuer").GetString()} jwks_uri_path={new Uri(disco.GetProperty("jwks_uri").GetString()!).AbsolutePath}");
        var jwks = await http.GetFromJsonAsync<JsonElement>("/.well-known/jwks.json");
        output.WriteLine($"OBSERVED jwks keys={jwks.GetProperty("keys").GetArrayLength()}");
        return (token, sub);
    }

    [Fact]
    public async Task Real_authservice_token_is_accepted()
    {
        var (token, sub) = await RegisterAndLogin();
        var client = _directory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var res = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(sub, body.GetProperty("userId").GetString());
        Assert.False(body.GetProperty("isAdmin").GetBoolean());
    }

    [Fact]
    public async Task Altered_token_is_rejected()
    {
        var (token, _) = await RegisterAndLogin();
        var client = _directory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me")).StatusCode); // control: the unaltered token passes

        var parts = token.Split('.');
        var sig = parts[2];
        var flipped = (sig[10] == 'A' ? 'B' : 'A');
        parts[2] = sig[..10] + flipped + sig[11..];
        client.DefaultRequestHeaders.Authorization = new("Bearer", string.Join('.', parts));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }
}
