// Development-only identity stand-in (ADR-0003). Filled in by T-01.04.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/health", () => Results.Ok("Healthy"));
app.Run();
