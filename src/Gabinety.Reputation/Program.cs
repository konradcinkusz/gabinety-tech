using Gabinety.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddFeatures();
builder.Services.AddRateLimiting(builder.Configuration);

// Authentication is registered here and only here.
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthentication().AddInternalKeyAuthentication();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Authenticated", p => p.RequireAuthenticatedUser())
    .AddPolicy("Admin", p => p.RequireRole("Admin", "SuperAdmin")) // authservice roles; the claim is the WS-Federation role URI
    .AddPolicy("Internal", p => p.AddAuthenticationSchemes(Security.InternalScheme).RequireAuthenticatedUser())
    .RequireAuthenticatedByDefault(); // deny by default: a public route must say AllowAnonymous() (docs/operations/ROUTE-AUTH.md)
builder.Services.AddOpenApi();

var app = builder.Build();
app.UseOriginGuard();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapFeatures();
app.MapOpenApi("/openapi/v1.json").AllowAnonymous();
app.Run();

public partial class Program;
