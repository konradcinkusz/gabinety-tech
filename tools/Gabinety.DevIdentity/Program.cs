// Development-only identity stand-in (ADR-0003): the subset of the authservice HTTP surface the web app uses,
// on an in-memory user list. Wire names are those of docs/spikes/authservice.md.
using Gabinety.DevIdentity;

if (Environment.GetEnvironmentVariable("GABINETY_FAKE") != "1")
{
    Console.Error.WriteLine("Gabinety.DevIdentity refuses to start: GABINETY_FAKE=1 is not set. " +
        "It is a development stand-in that accepts known passwords and must never run as a real identity provider (ADR-0003).");
    return 1;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => { var store = new IdentityStore(sp.GetRequiredService<TimeProvider>()); store.Seed(); return store; });
builder.Services.AddSingleton(sp => new TokenIssuer(sp.GetRequiredService<TimeProvider>(),
    builder.Configuration["Jwt:Issuer"] ?? "AuthService", builder.Configuration["Jwt:Audience"] ?? "AuthService")); // authservice defaults
var app = builder.Build();

app.MapGet("/health", () => Results.Ok("Healthy"));
app.MapDevIdentityEndpoints(builder.Configuration["Jwt:PublicBaseUrl"]);
app.Run();
return 0;

// Lets WebApplicationFactory<Program> host this app in process (tests/Gabinety.Integration.Tests/AuthContract).
public partial class Program;
