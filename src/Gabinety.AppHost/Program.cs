using System.Security.Cryptography;

// Composition root for development (P1, SPEC 4.1). Not the production topology.
var builder = DistributedApplication.CreateBuilder(args);
var fake = Environment.GetEnvironmentVariable("GABINETY_FAKE") == "1";

// The callee key of the internal endpoints (ADR-0007, CONTRACTS 10.3). One value for every service: this composition root is development only.
// Fake profile: a fixed, documented development value, it protects nothing (every service is on localhost, the data is synthetic). Default profile: it must be given
// (user secrets or Parameters__internal-key), the AppHost asks for it.
var internalKey = fake
    ? builder.AddParameter("internal-key", () => "demo-development-internal-key-not-a-secret", secret: true)
    : builder.AddParameter("internal-key", secret: true);

// The four services and where they listen. Every service learns the others (Backend:*) and the keys to call them (Internal:Keys:*), as CONTRACTS 10.3 names them.
const string DirectoryUrl = "http://localhost:5101", BookingUrl = "http://localhost:5102", ReputationUrl = "http://localhost:5103", AssistantUrl = "http://localhost:5104";
const int AuthservicePort = 5105;
var identityAuthority = fake ? "http://localhost:5100" : $"http://localhost:{AuthservicePort}";
// The audience every service validates; authservice issues it (Jwt__Audience). Same string as deploy/fly/authservice.fly.toml.
const string IdentityAudience = "gabinety";
// The real authservice, pinned by digest: tag v0.3.4, multi-platform index digest (docs/spikes/authservice.md section 4; the same string as deploy/fly/authservice.fly.toml and ThinSliceTests).
const string AuthserviceImage = "ghcr.io/konradcinkusz/authservice";
const string AuthserviceDigest = "sha256:d0309a94f6b6300d4b8091381786c160ae7717a022416debaaa6d8d19a244645";

IResourceBuilder<ProjectResource> Service<TProject>(string name, int port) where TProject : IProjectMetadata, new()
{
    var p = builder.AddProject<TProject>(name, launchProfileName: null)
        .WithHttpEndpoint(port: port, isProxied: false)
        .WithHttpHealthCheck("/health")
        .WithEnvironment("Environment__Kind", "Development") // CONTRACTS 10.4: maps /internal/v1/test/clock and /internal/v1/demo/reset (never set outside a developer's machine)
        .WithEnvironment("Internal__Key", internalKey)
        .WithEnvironment("Backend__Directory", DirectoryUrl).WithEnvironment("Backend__Booking", BookingUrl)
        .WithEnvironment("Backend__Reputation", ReputationUrl).WithEnvironment("Backend__Assistant", AssistantUrl)
        .WithEnvironment("Internal__Keys__Directory", internalKey).WithEnvironment("Internal__Keys__Booking", internalKey)
        .WithEnvironment("Internal__Keys__Reputation", internalKey).WithEnvironment("Internal__Keys__Assistant", internalKey);
    if (fake)
    {
        p = p.WithEnvironment("GABINETY_FAKE", "1")
             .WithEnvironment("RateLimit__PerMinute", "6000") // every caller (seeder, services, web server, journeys) is one localhost address: one shared bucket at 60 (CONTRACTS 10.2)
             .WithEnvironment("Llm__Provider", "Fake")
             .WithEnvironment("Database__Provider", "InMemory")
             .WithEnvironment("Payments__Provider", "Mock")
             .WithEnvironment("Notifications__Provider", "Log")
             .WithEnvironment("Identity__Authority", "http://localhost:5100");
    }
    else
    {
        // Default profile: real PostgreSQL (one database per service, the connection string comes from the reference) and the real authservice as the identity authority.
        p = p.WithEnvironment("Identity__Authority", identityAuthority)
             .WithEnvironment("Identity__Audience", IdentityAudience)
             .WithEnvironment("Database__Provider", "PostgreSQL")
             .WithEnvironment("Notifications__Provider", "Log");
    }
    return p;
}

IResourceBuilder<ProjectResource>? identity = null;
IResourceBuilder<ContainerResource>? authservice = null;
IResourceBuilder<PostgresDatabaseResource>? directoryDb = null, bookingDb = null, reputationDb = null, assistantDb = null;
if (fake)
{
    identity = builder.AddProject<Projects.Gabinety_DevIdentity>("devidentity", launchProfileName: null)
        .WithHttpEndpoint(port: 5100, isProxied: false)
        .WithHttpHealthCheck("/health")
        .WithEnvironment("GABINETY_FAKE", "1");
}
else
{
    // Default profile: PostgreSQL in Docker with the five databases of deploy/fly/init-databases.sql, and the pinned authservice image. Needs a Docker daemon.
    // The superuser password is fixed on purpose: the data volume keeps the password of its first start, and a password generated on every start would lock you out of it.
    // It is a development value (the server is on localhost, the data is synthetic), as the fake profile's internal key.
    var pgPassword = builder.AddParameter("postgres-password", () => "demo-development-postgres-not-a-secret", secret: true);
    var postgres = builder.AddPostgres("postgres", password: pgPassword).WithDataVolume("gabinety-pg").WithHostPort(5432);
    var authDb = postgres.AddDatabase("authdb");
    directoryDb = postgres.AddDatabase("directorydb");
    bookingDb = postgres.AddDatabase("bookingdb");
    reputationDb = postgres.AddDatabase("reputationdb");
    assistantDb = postgres.AddDatabase("assistantdb");

    // The RS256 signing key (PKCS#8, RSA 2048) is a secret, so it is never a file in this repository. Give your own as the parameter authservice-jwt-key
    // (user secrets of the AppHost or Parameters__authservice-jwt-key; scripts/gen-authservice-key.sh makes one outside the work tree), or the AppHost generates one in memory
    // for this run. A new key per run is harmless: the services re-read the JWKS, and tokens of an earlier run are simply refused, so sign in again.
    var configuredKey = builder.Configuration["Parameters:authservice-jwt-key"];
    var signingKey = !string.IsNullOrWhiteSpace(configuredKey) ? configuredKey : RSA.Create(2048).ExportPkcs8PrivateKeyPem();
    // The first administrator: authservice seeds ONE account from InitialAdmin and gives it the platform role SuperAdmin (DbSeeder.SeedInitialAdminAsync), not Admin.
    // Directory, Booking and Reputation treat SuperAdmin as an administrator (role Admin or SuperAdmin). Same address and development password as the stand-in's demo-admin.
    var adminEmail = builder.Configuration["InitialAdmin:Email"] ?? "demo-admin@demo.example";
    var adminPassword = builder.AddParameter("initial-admin-password", () => "Demo-Dev-Passw0rd!", secret: true);

    authservice = builder.AddContainer("authservice", AuthserviceImage)
        .WithImageSHA256(AuthserviceDigest["sha256:".Length..]) // pinned by digest (repository@sha256:...), never a floating tag
        .WithHttpEndpoint(port: AuthservicePort, targetPort: 8080, name: "http", isProxied: false)
        .WithReference(authDb, "DefaultConnection") // ConnectionStrings__DefaultConnection, host = the postgres container
        .WithEnvironment("DATABASE_PROVIDER", "PostgreSQL")
        .WithEnvironment("Database__SchemaMode", "EnsureCreated")
        .WithEnvironment("Jwt__Algorithm", "RS256")
        .WithEnvironment("Jwt__PrivateKeyPem", signingKey.Replace("\r", "").Replace("\n", "\\n")) // one line; authservice repairs the escaped \n
        .WithEnvironment("Jwt__Issuer", "gabinety-development")
        .WithEnvironment("Jwt__Audience", IdentityAudience)
        .WithEnvironment("Jwt__PublicBaseUrl", identityAuthority)
        .WithEnvironment("Auth__RequireConfirmedEmail", "false") // local development: there is no sender, so registration answers 200 with tokens (not 202)
        .WithEnvironment("InitialAdmin__Email", adminEmail)
        .WithEnvironment("InitialAdmin__Password", adminPassword)
        .WithEnvironment("Cors__AllowedOrigins__0", "http://localhost:3000")
        .WithEnvironment("Network__ClientIpHeader", "Fly-Client-IP") // as deploy/fly: the web forwards the visitor's address in it, so visitors do not share authservice's 20 a minute
        // GitHub sign-in stays off here: it needs a real OAuth app (OAuth__GitHub__ClientId and __ClientSecret). docs/operations/AUTH.md.
        .WithHttpHealthCheck("/health/ready")
        .WaitFor(authDb);
}

var directory = Service<Projects.Gabinety_Directory>("directory", 5101);
var booking = Service<Projects.Gabinety_Booking>("booking", 5102).WaitFor(directory);
var reputation = Service<Projects.Gabinety_Reputation>("reputation", 5103).WaitFor(directory).WaitFor(booking);
var assistant = Service<Projects.Gabinety_Assistant>("assistant", 5104); // never waited for by others (P8)

if (identity is not null)
    foreach (var s in new[] { directory, booking, reputation, assistant }) s.WaitFor(identity);
if (authservice is not null)
    foreach (var s in new[] { directory, booking, reputation, assistant }) s.WaitFor(authservice);
if (directoryDb is not null)
{
    directory.WithReference(directoryDb).WaitFor(directoryDb);
    booking.WithReference(bookingDb!).WaitFor(bookingDb!);
    reputation.WithReference(reputationDb!).WaitFor(reputationDb!);
    assistant.WithReference(assistantDb!).WaitFor(assistantDb!);
}

// The demo seeder (T-08.01, tools/Gabinety.DemoSeeder): runs once after the services are healthy. In the fake profile it starts by itself; in the default profile only on request
// (start "seeder" in the dashboard). "seeder-reset" removes the demo- data of all services and seeds again. Neither is waited for: the web app and the journeys read what is there.
IResourceBuilder<ProjectResource> Seeder(string name, params string[] args)
{
    var seeder = builder.AddProject<Projects.Gabinety_DemoSeeder>(name, launchProfileName: null)
        .WithArgs(args)
        .WithEnvironment("Environment__Kind", "Development") // the seeder's own guard (SeedGuard); it also refuses any target that is not on this machine
        .WithEnvironment("Backend__Directory", DirectoryUrl).WithEnvironment("Backend__Booking", BookingUrl).WithEnvironment("Backend__Reputation", ReputationUrl)
        .WithEnvironment("Internal__Keys__Directory", internalKey).WithEnvironment("Internal__Keys__Booking", internalKey).WithEnvironment("Internal__Keys__Reputation", internalKey)
        .WithEnvironment("Identity__Authority", identityAuthority)
        .WaitFor(directory).WaitFor(booking).WaitFor(reputation);
    if (identity is not null) seeder.WaitFor(identity);
    if (authservice is not null) seeder.WaitFor(authservice);
    return fake && name == "seeder" ? seeder : seeder.WithExplicitStart();
}
Seeder("seeder");
Seeder("seeder-reset", "--reset");

// The web frontend (ADR-0010: the Blazor Web App, the only one since the cutover): the backend addresses, port 3000, the kernel's /health. It is given NO internal key: that is for
// services calling services (ADR-0007). Not in the default profile's WaitFor chain of anything: nothing depends on a frontend.
var webBlazor = builder.AddProject<Projects.Gabinety_Web>("web-blazor", launchProfileName: null)
    .WithHttpEndpoint(port: 3000, isProxied: false)
    .WithHttpHealthCheck("/health")
    // Aspire starts a project WITHOUT ASPNETCORE_ENVIRONMENT here (launchProfileName: null), so it would run as Production from an unpublished build, where the pre-compressed static assets
    // (the stylesheets, blazor.web.js) cannot be found and answer 500 to every browser (they send Accept-Encoding; curl and in-process tests do not). A developer's AppHost is Development.
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("Backend__Directory", DirectoryUrl)
    .WithEnvironment("Backend__Booking", BookingUrl)
    .WithEnvironment("Backend__Reputation", ReputationUrl)
    .WithEnvironment("Backend__Assistant", AssistantUrl)
    .WithEnvironment("Backend__Auth", identityAuthority)
    .WithEnvironment("Site__BaseUrl", "http://localhost:3000")
    // The session cookie is sealed with Data Protection (stateless sessions, ADR-0010): the key ring is persisted per user so a restart of the host does not sign a developer out. On Fly it is a volume.
    .WithEnvironment("DataProtection__KeysPath", Path.Combine(Path.GetTempPath(), $"gabinety-web-keys-{Environment.UserName}"))

    // The journeys change data and read the public pages straight after (approve, then open the profile): the five-minute output cache of the public pages is off in this host.
    .WithEnvironment("OutputCache__Enabled", "false")
    .WaitFor(directory);
// The mock checkout (/checkout/mock/{id}) exists only in the fake profile (security review F-01): the host decides that with the Booking service's own rule (GABINETY_FAKE=1 or Environment:Kind=Development,
// and no real payment provider), so the fake profile must say so, or the booking form ends in "Nie udało się przejść do płatności" and journeys J3 and J4 cannot reach the checkout.
if (fake) webBlazor.WithEnvironment("GABINETY_FAKE", "1");
if (authservice is not null) webBlazor.WaitFor(authservice); // sign-in needs the real authservice in the default profile

builder.Build().Run();
