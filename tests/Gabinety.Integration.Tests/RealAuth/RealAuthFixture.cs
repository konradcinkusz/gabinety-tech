using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Gabinety.Integration.Tests.AuthContract;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Gabinety.Integration.Tests.RealAuth;

/// <summary>One running authservice: its base URL and a way to get a client with its own rate-limit bucket.</summary>
public sealed class AuthInstance(string name, string url, string signingKeyPem, Func<ValueTask> stop)
{
    public string Name { get; } = name;
    public string Url { get; } = url;
    /// <summary>The PKCS#8 private key this instance signs with (the test generated it; it never leaves the run).</summary>
    public string SigningKeyPem { get; } = signingKeyPem;
    public ValueTask StopAsync() => stop();

    /// <summary>
    /// A client for this instance. authservice limits its auth endpoints to 20 requests a minute per client address and every test runs from this one machine, so each client
    /// names an address of its own in the header the instance trusts (<c>Network__ClientIpHeader</c>). Redirects are not followed: the GitHub start answers with one.
    /// </summary>
    public HttpClient Client(string? clientIp = null)
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(Url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add(AuthContractSuite.ClientIpHeader, clientIp ?? AuthContractSuite.UniqueClientIp());
        return http;
    }
}

/// <summary>
/// The REAL authservice (the image pinned by digest, the one deploy/fly/authservice.fly.toml and the AppHost run) on a real PostgreSQL, started once for every test class of the
/// collection "real-authservice". Two instances share the database server: <see cref="Main"/> (the local-development posture: no e-mail confirmation, an InitialAdmin, GitHub sign-in
/// configured with dummy credentials) and <see cref="Confirm"/> (e-mail confirmation required). <see cref="StartAsync"/> starts more (key rotation).
/// Needs Docker. Nothing is stored outside the run: the signing keys are generated in memory and every address is synthetic.
/// </summary>
public sealed class RealAuthFixture : IAsyncLifetime
{
    // ghcr.io/konradcinkusz/authservice:v0.3.4, multi-platform index digest (docs/spikes/authservice.md section 4). Same string as the fly toml, the AppHost and ThinSliceTests.
    public const string Image = "ghcr.io/konradcinkusz/authservice@sha256:d0309a94f6b6300d4b8091381786c160ae7717a022416debaaa6d8d19a244645";
    public const string Issuer = "gabinety-realauth";
    public const string Audience = "gabinety";
    // The first administrator authservice seeds (InitialAdmin__*): a SuperAdmin, not an Admin (DbSeeder). Synthetic, protects nothing.
    public const string OwnerEmail = "owner@example.test";
    public const string OwnerPassword = "Owner-Pass-1234!";
    // GitHub sign-in is enabled by the presence of both values; they are dummies, so only the START of the flow can run (a real round trip needs a real OAuth app).
    public const string WebOrigin = "http://localhost:3000";

    // Local-verification switch for a machine WITHOUT Docker: run the published AuthService.dll as a process against a PostgreSQL you started (docs/operations/AUTH.md section 9).
    // CI never sets these: the containers are the proof.
    const string LocalDllVariable = "REALAUTH_LOCAL_DLL", LocalPgVariable = "REALAUTH_LOCAL_PG";

    INetwork? _network;
    PostgreSqlContainer? _pg;
    readonly List<AuthInstance> _started = [];

    public AuthInstance Main { get; private set; } = null!;
    public AuthInstance Confirm { get; private set; } = null!;
    public bool LocalMode => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(LocalDllVariable));

    public async Task InitializeAsync()
    {
        if (!LocalMode)
        {
            _network = new NetworkBuilder().Build();
            await _network.CreateAsync();
            _pg = new PostgreSqlBuilder("postgres:17-alpine").WithNetwork(_network).WithNetworkAliases("pg").Build();
            await _pg.StartAsync();
        }
        Main = await StartAsync("main", new Dictionary<string, string>
        {
            ["InitialAdmin__Email"] = OwnerEmail,
            ["InitialAdmin__Password"] = OwnerPassword,
            ["OAuth__GitHub__ClientId"] = "dummy-github-client-id",
            ["OAuth__GitHub__ClientSecret"] = "dummy-github-client-secret",
            ["OAuth__CallbackBaseUrl"] = "http://auth.test.invalid",
            ["OAuth__PostLoginRedirectBaseUrl"] = WebOrigin,
            ["OAuth__PostLoginRedirectAllowedBaseUrls__0"] = WebOrigin,
        });
        Confirm = await StartAsync("confirm", new Dictionary<string, string> { ["Auth__RequireConfirmedEmail"] = "true" });
    }

    public async Task DisposeAsync()
    {
        foreach (var instance in _started.AsEnumerable().Reverse()) await instance.StopAsync();
        if (_pg != null) await _pg.DisposeAsync();
        if (_network != null) await _network.DisposeAsync();
    }

    /// <summary>Starts one more authservice on its own database. <paramref name="signingKeyPem"/> is generated when null; the instance is stopped with the fixture.</summary>
    public async Task<AuthInstance> StartAsync(string name, IDictionary<string, string>? settings = null, string? signingKeyPem = null, string? database = null)
    {
        database ??= name + "db";
        if (signingKeyPem is null)
        {
            using var rsa = RSA.Create(2048);
            signingKeyPem = rsa.ExportPkcs8PrivateKeyPem();
        }
        var env = new Dictionary<string, string>
        {
            ["DATABASE_PROVIDER"] = "PostgreSQL",
            ["Jwt__Algorithm"] = "RS256",
            ["Jwt__PrivateKeyPem"] = signingKeyPem,
            ["Jwt__Issuer"] = Issuer,
            ["Jwt__Audience"] = Audience,
            ["Auth__RequireConfirmedEmail"] = "false",
            ["Network__ClientIpHeader"] = AuthContractSuite.ClientIpHeader,
        };
        foreach (var (k, v) in settings ?? new Dictionary<string, string>()) env[k] = v;

        var instance = LocalMode ? await StartLocalAsync(name, database, env, signingKeyPem) : await StartContainerAsync(name, database, env, signingKeyPem);
        _started.Add(instance);
        return instance;
    }

    async Task<AuthInstance> StartContainerAsync(string name, string database, Dictionary<string, string> env, string pem)
    {
        await EnsureDatabaseAsync(_pg!.GetConnectionString(), database);
        env["ConnectionStrings__DefaultConnection"] = $"Host=pg;Port=5432;Database={database};Username={PostgreSqlBuilder.DefaultUsername};Password={PostgreSqlBuilder.DefaultPassword}";
        var builder = new ContainerBuilder(Image).WithNetwork(_network!).WithPortBinding(8080, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPath("/health/ready").ForPort(8080)));
        foreach (var (k, v) in env) builder = builder.WithEnvironment(k, v);
        var container = builder.Build();
        await container.StartAsync();
        return new AuthInstance(name, $"http://{container.Hostname}:{container.GetMappedPublicPort(8080)}", pem, () => container.DisposeAsync());
    }

    async Task<AuthInstance> StartLocalAsync(string name, string database, Dictionary<string, string> env, string pem)
    {
        var pg = Environment.GetEnvironmentVariable(LocalPgVariable) ?? "Host=localhost;Port=5499;Username=postgres";
        await EnsureDatabaseAsync(pg + ";Database=postgres", database);
        env["ConnectionStrings__DefaultConnection"] = $"{pg};Database={database}";
        var port = FreePort();
        var dll = Environment.GetEnvironmentVariable(LocalDllVariable)!;
        var start = new ProcessStartInfo("dotnet", $"\"{dll}\"") { WorkingDirectory = Path.GetDirectoryName(dll)!, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        foreach (var (k, v) in env) start.Environment[k] = v;
        var process = Process.Start(start)!;
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        var url = $"http://127.0.0.1:{port}";
        using var http = new HttpClient();
        for (var i = 0; i < 120; i++)
        {
            try { if ((await http.GetAsync(url + "/health/ready")).StatusCode == HttpStatusCode.OK) break; } catch (HttpRequestException) { }
            await Task.Delay(500);
        }
        return new AuthInstance(name, url, pem, () => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } return ValueTask.CompletedTask; });
    }

    /// <summary>Creates the database when it is not there yet (a restarted instance, as in the rotation test, reuses its database).</summary>
    static async Task EnsureDatabaseAsync(string adminConnectionString, string database)
    {
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", admin);
        exists.Parameters.AddWithValue("name", database);
        if (await exists.ExecuteScalarAsync() is null) await new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin).ExecuteNonQueryAsync();
    }

    static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

[CollectionDefinition(Name)]
public sealed class RealAuthCollection : ICollectionFixture<RealAuthFixture>
{
    public const string Name = "real-authservice";
}
