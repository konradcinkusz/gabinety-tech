using Gabinety.ServiceDefaults;
using Gabinety.Web.Api;
using Gabinety.Web.Hosting;

// Composition of the Blazor Web App frontend (ADR-0010). A manifest (P9): every line names a concern, the code is in Hosting/, Api/ and Auth/.
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();         // kernel: telemetry, /health, problem details, service discovery, the one resilience handler
builder.AddWebApp();                  // components, render modes, circuit limits, backend candidates as discovery endpoints
builder.AddWebAuthentication();       // cookie session on the host, tokens server side, deny by default
builder.Services.AddBackendClients(); // typed clients per backend: logical names, the visitor's address, no second resilience handler

var app = builder.Build();
app.UseWebPipeline();                 // origin guard first, security headers, authentication, antiforgery
app.MapWebEndpoints();                // health, static assets, /oauth/callback, the components
app.Run();

public partial class Program;
