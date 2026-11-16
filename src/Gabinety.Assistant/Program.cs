using Gabinety.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddFeatures();
builder.Services.AddRateLimiting();

var app = builder.Build();
app.UseOriginGuard();
app.UseRateLimiter();
app.MapDefaultEndpoints();
app.MapFeatures();
app.Run();

public partial class Program;
