using Gabinety.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddFeatures();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapFeatures();
app.Run();

public partial class Program;
