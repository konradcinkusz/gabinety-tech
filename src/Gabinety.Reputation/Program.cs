using Gabinety.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddFeatures();
builder.Services.AddRateLimiting();

var app = builder.Build();
app.UseOriginGuard();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapFeatures();
app.Run();

public partial class Program;
