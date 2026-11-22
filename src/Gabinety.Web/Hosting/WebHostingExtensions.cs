using Gabinety.ServiceDefaults;
using Gabinety.Web.Api;
using Gabinety.Web.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;

namespace Gabinety.Web.Hosting;

/// <summary>The pieces <c>Program.cs</c> names (P9: Program.cs is a manifest, one extension method per concern).</summary>
public static class WebHostingExtensions
{
    static readonly string[] FrameworkPrefixes = ["/_blazor", "/_framework/"];

    /// <summary>
    /// Components and render modes. Static server rendering is the default for every page; <c>@rendermode InteractiveServer</c> is applied per page where interaction is needed
    /// (ADR-0010). <c>App.razor</c> deliberately does NOT put a render mode on <c>Routes</c> and <c>HeadOutlet</c> (BLAZOR-WEB-APP section 3 does): public pages must be real HTML for crawlers
    /// and with JavaScript disabled (journey J1), and a root-level interactive mode would put a circuit under every anonymous visitor. A test pins both halves.
    /// Circuit costs are bounded here (BLAZOR-WEB-APP section 14): per-circuit memory is the capacity limit.
    /// </summary>
    public static WebApplicationBuilder AddWebApp(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var options = WebOptions.From(config);
        builder.Services.AddSingleton(options);
        // The backends' candidates become the configuration service discovery reads (see WebOptions.ServiceConfiguration); the typed clients only know logical names.
        builder.Configuration.AddInMemoryCollection(options.ServiceConfiguration());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddPublicPageCache(); // five minutes, anonymous public GETs only (Hosting/PublicPageCache.cs)
        builder.Services.AddScoped<CircuitHandler, VisitorCircuitHandler>();

        builder.Services.AddRazorComponents().AddInteractiveServerComponents(o =>
        {
            o.DisconnectedCircuitMaxRetained = config.GetValue("Circuit:DisconnectedMaxRetained", 20);
            o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromSeconds(config.GetValue("Circuit:DisconnectedRetentionSeconds", 120));
            o.MaxBufferedUnacknowledgedRenderBatches = config.GetValue("Circuit:MaxBufferedRenderBatches", 5);
        }).AddHubOptions(o => o.MaximumReceiveMessageSize = config.GetValue("Circuit:MaxReceiveBytes", 32 * 1024));
        builder.Services.Replace(ServiceDescriptor.Scoped<AntiforgeryStateProvider, FormOnlyAntiforgeryStateProvider>());
        builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 64 * 1024);
        return builder;
    }

    /// <summary>Order matters: forwarded headers (the framework adds them first when ASPNETCORE_FORWARDEDHEADERS_ENABLED is set), then the guard, before antiforgery and the component endpoints (BLAZOR-WEB-APP section 8).</summary>
    public static WebApplication UseWebPipeline(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<WebOptions>();
        if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseSecurityHeaders(options); // sets headers when the response starts and never answers by itself, so every response, a refusal included, carries them
        app.UseBrowserOriginGuard(); // before everything that acts: a foreign Origin never reaches the circuit, antiforgery or authentication
        app.UseStatusCodePagesWithReExecute("/not-found", "?status={0}", createScopeForStatusCodePages: true); // the status rides along: the page says "Brak dostępu" for 403
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.UseOutputCache(); // after authentication: the policy refuses a request that carries a session
        return app;
    }

    public static WebApplication MapWebEndpoints(this WebApplication app)
    {
        app.MapDefaultEndpoints(); // /health and /alive, anonymous
        // Static assets (the stylesheets, the framework script, and in Development the runtime fallback for files) are public. Through a group, so the convention also reaches the endpoints
        // the static-asset mapping adds beside its own; they are files from wwwroot, never data.
        var assets = app.MapGroup("");
        assets.AllowAnonymous();
        assets.MapStaticAssets();
        app.MapOAuth();
        app.MapAdminEndpoints(); // the ledger download (a stream, not a page)
        app.MapAccountEndpoints(); // /konto/export
        app.MapCalendarEndpoints(); // /moje/rezerwacje/{id}/kalendarz.ics

        app.MapSeoEndpoints(); // sitemap.xml, robots.txt
        // Interactive server rendering is registered for the pages that ask for it. The circuit endpoints (/_blazor/*) and the framework's own (/_framework/*) are anonymous: an interactive page
        // of an anonymous visitor needs them, and the deny-by-default fallback would answer 401 otherwise. Authorization is the page's own ([Authorize] on the page, so NOT a blanket
        // AllowAnonymous on every component endpoint, which would override it) and the API behind it (BLAZOR-WEB-APP section 7: middleware is UX, the API is the boundary).
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode().Add(endpoint =>
        {
            if (endpoint is RouteEndpointBuilder { RoutePattern.RawText: { } pattern } && FrameworkPrefixes.Any(p => pattern.StartsWith(p, StringComparison.Ordinal)))
                endpoint.Metadata.Add(new AllowAnonymousAttribute());
        });
        return app;
    }
}

/// <summary>Captures the visitor address when a circuit opens: a circuit has no HttpContext once it runs (see <see cref="VisitorAddressAccessor"/>).</summary>
public sealed class VisitorCircuitHandler(VisitorAddressAccessor visitor) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        visitor.Capture();
        return Task.CompletedTask;
    }
}
