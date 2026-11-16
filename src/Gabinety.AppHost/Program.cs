// Composition root for development (P1, SPEC 4.1). Not the production topology.
var builder = DistributedApplication.CreateBuilder(args);
var fake = Environment.GetEnvironmentVariable("GABINETY_FAKE") == "1";

IResourceBuilder<ProjectResource> Service<TProject>(string name, int port) where TProject : IProjectMetadata, new()
{
    var p = builder.AddProject<TProject>(name, launchProfileName: null)
        .WithHttpEndpoint(port: port, isProxied: false)
        .WithHttpHealthCheck("/health");
    if (fake)
    {
        p = p.WithEnvironment("GABINETY_FAKE", "1")
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

var web = builder.AddJavaScriptApp("web", "../../web", "dev")
    .WithPnpm()
    .WithHttpEndpoint(port: 3000, env: "PORT", isProxied: false)
    .WithEnvironment("Backend__Directory", "http://localhost:5101")
    .WithEnvironment("Backend__Booking", "http://localhost:5102")
    .WithEnvironment("Backend__Reputation", "http://localhost:5103")
    .WithEnvironment("Backend__Assistant", "http://localhost:5104")
    .WithEnvironment("Backend__Auth", fake ? "http://localhost:5100" : "http://localhost:5105")
    .WithEnvironment("Site__BaseUrl", "http://localhost:3000")
    .WaitFor(directory);

builder.Build().Run();
