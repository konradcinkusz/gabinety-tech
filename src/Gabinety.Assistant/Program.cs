using Gabinety.Assistant.Features.OwnerSmoke;
using Gabinety.Llm;
using Gabinety.ServiceDefaults;

// The owner's smoke verb (T-02.05) runs before any web host exists: it is not reachable as a server.
if (SmokeCommand.IsSmoke(args)) return await SmokeCommand.RunAsync(args, Console.Out, Environment.GetEnvironmentVariable);

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddFeatures();
builder.AddLlmGateway(); // Copilot by default, Fake with Llm:Provider=Fake; adds the "llm" health check
builder.Services.AddRateLimiting(builder.Configuration);

// Authentication is registered here and only here.
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthentication().AddInternalKeyAuthentication();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Authenticated", p => p.RequireAuthenticatedUser())
    .AddPolicy("Admin", p => p.RequireRole("Admin", "SuperAdmin")) // authservice roles; the claim is the WS-Federation role URI
    .AddPolicy("Internal", p => p.AddAuthenticationSchemes(Security.InternalScheme).RequireAuthenticatedUser());
builder.Services.AddOpenApi();

var app = builder.Build();
app.UseOriginGuard();
app.UseAuthentication();
app.UseRateLimiter(); // after authentication: the limit is per user id when signed in (CONTRACTS 10.2)
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapFeatures();
app.MapOpenApi("/openapi/v1.json");
app.Run();
return 0;

public partial class Program;
