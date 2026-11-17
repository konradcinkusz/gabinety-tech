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
var identityAuthority = fake ? "http://localhost:5100" : "http://localhost:5105";

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
    return p;
}

IResourceBuilder<ProjectResource>? identity = null;
if (fake)
{
    identity = builder.AddProject<Projects.Gabinety_DevIdentity>("devidentity", launchProfileName: null)
        .WithHttpEndpoint(port: 5100, isProxied: false)
        .WithHttpHealthCheck("/health")
        .WithEnvironment("GABINETY_FAKE", "1");
}
else
{
    // Default profile: PostgreSQL in Docker. The pinned authservice image is added once T-01.01 records its name, tag and digest.
    builder.AddPostgres("postgres").WithDataVolume("gabinety-pg").WithHostPort(5432);
}

var directory = Service<Projects.Gabinety_Directory>("directory", 5101);
var booking = Service<Projects.Gabinety_Booking>("booking", 5102).WaitFor(directory);
var reputation = Service<Projects.Gabinety_Reputation>("reputation", 5103).WaitFor(directory).WaitFor(booking);
var assistant = Service<Projects.Gabinety_Assistant>("assistant", 5104); // never waited for by others (P8)

if (identity is not null)
    foreach (var s in new[] { directory, booking, reputation, assistant }) s.WaitFor(identity);

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
    return fake && name == "seeder" ? seeder : seeder.WithExplicitStart();
}
Seeder("seeder");
Seeder("seeder-reset", "--reset");

var web = builder.AddJavaScriptApp("web", "../../web", "dev")
    .WithPnpm()
    .WithHttpEndpoint(port: 3000, env: "PORT", isProxied: false)
    .WithEnvironment("Backend__Directory", DirectoryUrl)
    .WithEnvironment("Backend__Booking", BookingUrl)
    .WithEnvironment("Backend__Reputation", ReputationUrl)
    .WithEnvironment("Backend__Assistant", AssistantUrl)
    .WithEnvironment("Backend__Auth", identityAuthority)
    .WithEnvironment("Site__BaseUrl", "http://localhost:3000")
    .WaitFor(directory);

builder.Build().Run();
