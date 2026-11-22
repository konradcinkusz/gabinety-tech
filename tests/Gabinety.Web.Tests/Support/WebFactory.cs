using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Gabinety.Web.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Gabinety.Web.Tests;

/// <summary>The real host with every backend replaced by a fake handler: no network, no login (BLAZOR-WEB-APP section 13).</summary>
public sealed class WebFactory : WebApplicationFactory<Program>
{

    public const string TrustedKey = "test-trusted-proxy-key-0123456789abcdef0123456789";
    public FakeBackend Directory { get; } = new();
    public FakeBackend Booking { get; } = new();
    public FakeBackend Reputation { get; } = new();
    public FakeBackend Assistant { get; } = new();
    /// <summary>The identity service. Pass one in to start a second process (a deploy) against the same service.</summary>
    public FakeIdentity Identity { get; private set; } = new();
    /// <summary>A second process (a deploy) talks to the SAME identity service: call before the first request.</summary>
    public WebFactory SharingIdentity(FakeIdentity? identity) { if (identity is not null) Identity = identity; return this; }
    readonly Dictionary<string, string?> _settings = new()
    {
        ["Backend:Directory"] = "http://directory.test", ["Backend:Booking"] = "http://booking.test", ["Backend:Reputation"] = "http://reputation.test",
        ["Backend:Assistant"] = "http://assistant.test", ["Backend:Auth"] = "http://auth.test",
        ["Site:BaseUrl"] = "http://localhost", ["RateLimit:TrustedProxyKey"] = TrustedKey,
    };
    string _environment = "Development";
    /// <summary>What the host logged at Error or above while a test ran (a component that throws is caught by the error boundary and would otherwise be invisible).</summary>
    public List<string> Errors { get; } = [];
    readonly List<Action<IServiceCollection>> _services = [];

    /// <summary>Replaces or adds services of the host under test (a fixed clock, for instance).</summary>
    public WebFactory Configure(Action<IServiceCollection> configure) { _services.Add(configure); return this; }

    public WebFactory With(string key, string? value) { _settings[key] = value; return this; }
    public WebFactory In(string environment) { _environment = environment; return this; }
    readonly List<Microsoft.Extensions.Logging.ILoggerProvider> _logProviders = [];
    /// <summary>Adds a logger provider to the host, so a test can read everything it logs.</summary>
    public WebFactory LogTo(Microsoft.Extensions.Logging.ILoggerProvider provider) { _logProviders.Add(provider); return this; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.ConfigureLogging(l => l.AddProvider(new ErrorSink(Errors)));
        foreach (var (k, v) in _settings) builder.UseSetting(k, v);
        foreach (var provider in _logProviders) builder.ConfigureLogging(l => l.AddProvider(provider));
        builder.ConfigureServices(services =>
        {
            (string Name, FakeBackend Handler)[] all = [(Gabinety.Web.Api.Backend.Directory, Directory), (Gabinety.Web.Api.Backend.Booking, Booking), (Gabinety.Web.Api.Backend.Reputation, Reputation), (Gabinety.Web.Api.Backend.Assistant, Assistant), (Gabinety.Web.Api.Backend.Auth, Identity.Backend)];
            foreach (var (name, handler) in all)
            {
                services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => handler);
            }
            // The kernel's one resilience handler stays; only the wait between attempts is removed so a failing backend does not make a test slow.
            foreach (var configure in _services) configure(services);
            services.PostConfigureAll<HttpStandardResilienceOptions>(o => { o.Retry.Delay = TimeSpan.Zero; o.Retry.UseJitter = false; o.Retry.BackoffType = Polly.DelayBackoffType.Constant; o.Retry.MaxDelay = null; });
        });
    }

    public Browser NewBrowser(bool hostHeaderOnly = false) => new(this);
}

/// <summary>A minimal browser: keeps cookies (the Secure flag ignored, the client speaks plain http), never follows redirects, and posts forms the way Blazor's static forms expect (antiforgery token and handler fields).</summary>
public sealed class Browser(WebFactory factory)
{
    public HttpClient Client { get; } = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
    readonly Dictionary<string, string> _cookies = [];
    public IReadOnlyDictionary<string, string> Cookies => _cookies;
    public List<string> SetCookieLog { get; } = [];
    /// <summary>A cookie the browser already holds (a session from before a deploy, a tampered one).</summary>
    public Browser WithCookie(string name, string value) { _cookies[name] = value; return this; }

    public async Task<Page> GetAsync(string path, params (string Name, string Value)[] headers) => await SendAsync(new HttpRequestMessage(HttpMethod.Get, path), headers);

    public async Task<Page> SendAsync(HttpRequestMessage request, params (string Name, string Value)[] headers)
    {
        foreach (var (n, v) in headers) request.Headers.TryAddWithoutValidation(n, v);
        if (_cookies.Count > 0) request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
        var response = await Client.SendAsync(request);
        foreach (var raw in response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [])
        {
            SetCookieLog.Add(raw);
            var first = raw.Split(';')[0];
            var eq = first.IndexOf('=');
            var (name, value) = (first[..eq], first[(eq + 1)..]);
            var deleted = raw.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase) || raw.Contains("max-age=0", StringComparison.OrdinalIgnoreCase);
            if (deleted || value.Length == 0) _cookies.Remove(name); else _cookies[name] = value;
        }
        return new Page(response, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Opens <paramref name="formPage"/>, copies its hidden fields (antiforgery token, form handler) and posts <paramref name="fields"/> to <paramref name="postTo"/>.</summary>
    public async Task<Page> PostFormAsync(string formPage, IEnumerable<KeyValuePair<string, string>> fields, string? postTo = null, params (string Name, string Value)[] headers)
    {
        var page = await GetAsync(formPage);
        var form = page.Dom.QuerySelector("form[method=post]") ?? throw new InvalidOperationException($"no post form on {formPage}: {page.Status}");
        var all = form.QuerySelectorAll("input[type=hidden]").Select(i => KeyValuePair.Create(i.GetAttribute("name")!, i.GetAttribute("value") ?? "")).Concat(fields).ToList();
        var request = new HttpRequestMessage(HttpMethod.Post, postTo ?? form.GetAttribute("action") ?? formPage) { Content = new FormUrlEncodedContent(all) };
        return await SendAsync(request, headers);
    }
}

public sealed class Page(HttpResponseMessage response, string html)
{
    public HttpResponseMessage Response { get; } = response;
    public HttpStatusCode Status => Response.StatusCode;
    public string Html { get; } = html;
    /// <summary>Where a redirect goes: the path and query when it stays on the site (the server writes an absolute URL), the whole URL when it leaves it.</summary>
    public string? Location => Response.Headers.Location is { } l ? (l.IsAbsoluteUri && l.Host == "localhost" ? l.PathAndQuery : l.OriginalString) : null;
    public string Text => Dom.Body?.TextContent ?? "";
    IDocument? _dom;
    public IDocument Dom => _dom ??= new HtmlParser().ParseDocument(Html);
    public string? Header(string name) => Response.Headers.TryGetValues(name, out var v) ? string.Join(", ", v) : Response.Content.Headers.TryGetValues(name, out var c) ? string.Join(", ", c) : null;
    public IEnumerable<string> SetCookies => Response.Headers.TryGetValues("Set-Cookie", out var v) ? v : [];

    public IElement? One(string css) => Dom.QuerySelector(css);
    public IReadOnlyList<IElement> All(string css) => [.. Dom.QuerySelectorAll(css)];
    public string TextOf(string css) => One(css)?.TextContent.Trim() ?? "";
    public bool HasAlert => Dom.QuerySelector("[role=alert]") is not null;
    public static string Squash(string s) => Regex.Replace(s, @"\s+", " ").Trim();
}

sealed class ErrorSink(List<string> sink) : Microsoft.Extensions.Logging.ILoggerProvider, Microsoft.Extensions.Logging.ILogger
{
    public Microsoft.Extensions.Logging.ILogger CreateLogger(string category) => this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => level >= Microsoft.Extensions.Logging.LogLevel.Warning;
    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, Microsoft.Extensions.Logging.EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (level < Microsoft.Extensions.Logging.LogLevel.Warning) return;
        lock (sink) sink.Add(formatter(state, exception) + " " + exception);
    }
    public void Dispose() { }
}
