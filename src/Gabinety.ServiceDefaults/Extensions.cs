using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Gabinety.ServiceDefaults;

/// <summary>A service feature: a folder Features/&lt;Name&gt;/ with one implementation (SPEC 6.0).</summary>
public interface IServiceFeature
{
    void Add(IHostApplicationBuilder builder);
    void Map(IEndpointRouteBuilder endpoints);
}

public static class Extensions
{
    /// <summary>Telemetry, health, service discovery, resilience, problem details (P2, P15).</summary>
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        var otel = builder.Services.AddOpenTelemetry();
        otel.WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation());
        otel.WithTracing(t => t
            .AddAspNetCoreInstrumentation(o => o.Filter = ctx =>
                !ctx.Request.Path.StartsWithSegments("/health") && !ctx.Request.Path.StartsWithSegments("/alive"))
            .AddHttpClientInstrumentation());
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            otel.UseOtlpExporter();
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health").AllowAnonymous(); // probes carry no token; the fallback policy must not catch them
        app.MapHealthChecks("/alive", new() { Predicate = _ => false }).AllowAnonymous();
        return app;
    }

    /// <summary>Instantiates every IServiceFeature of the calling assembly, lets it register its services, and keeps it for mapping (P9, P10).</summary>
    public static TBuilder AddFeatures<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        foreach (var t in FeatureTypes(System.Reflection.Assembly.GetCallingAssembly()))
        {
            var feature = (IServiceFeature)Activator.CreateInstance(t)!;
            feature.Add(builder);
            builder.Services.AddSingleton(feature);
        }
        return builder;
    }

    public static WebApplication MapFeatures(this WebApplication app)
    {
        foreach (var f in app.Services.GetServices<IServiceFeature>()) f.Map(app);
        return app;
    }

    private static IEnumerable<Type> FeatureTypes(System.Reflection.Assembly a) =>
        a.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IServiceFeature).IsAssignableFrom(t));
}
