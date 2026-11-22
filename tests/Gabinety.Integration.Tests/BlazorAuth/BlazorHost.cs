extern alias WebApp;

using System.Net;
using System.Text.RegularExpressions;
using Gabinety.Integration.Tests.AuthContract;
using Gabinety.Integration.Tests.RealAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Gabinety.Integration.Tests.BlazorAuth;

/// <summary>A clock the test can move forward: the access token of the real authservice lives an hour, and a test must not wait for it.</summary>
public sealed class OffsetClock : TimeProvider
{
    long _ticks;
    public TimeSpan Offset { get => new(Interlocked.Read(ref _ticks)); set => Interlocked.Exchange(ref _ticks, value.Ticks); }
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + Offset;
}

/// <summary>
/// The Blazor host (<c>src/Gabinety.Web</c>) IN PROCESS, pointed at a REAL authservice (<c>Backend:Auth</c>, <c>Identity:PublicUrl</c>, issuer and audience of the instance). Nothing about the identity
/// service is faked: the host's own typed clients, token verifier, session cache and cookie run against the real HTTP surface. The other backends are not needed by the auth pages and are
/// pointed at a closed port. Several hosts can share one <paramref name="keysDirectory"/>: that is "a deploy" (a new process, an empty session cache, the same Data Protection key ring and so
/// the same cookies).
/// </summary>
public sealed class BlazorHost : WebApplicationFactory<WebApp::Program>
{
    public const string Origin = RealAuthFixture.WebOrigin;

    readonly AuthInstance _auth;
    readonly string _site;
    readonly bool _ownsKeys;
    /// <summary>Where the sealed cookies' key ring lives: the same value gives the same cookies in another host.</summary>
    public string KeysDirectory { get; }
    public OffsetClock Clock { get; } = new();
    /// <summary>When set, every request the host makes to the identity service goes to this base address instead (the "auth" backend was redeployed, a key was rotated behind the same name).</summary>
    public volatile Uri? AuthTarget;
    readonly bool _switchable;

    public BlazorHost(AuthInstance auth, string? keysDirectory = null, string site = Origin, bool switchable = false)
    {
        _auth = auth;
        _site = site;
        _switchable = switchable;
        _ownsKeys = keysDirectory is null;
        KeysDirectory = keysDirectory ?? Directory.CreateTempSubdirectory("gabinety-web-keys-").FullName;
        Directory.CreateDirectory(KeysDirectory);
        AuthTarget = switchable ? new Uri(auth.Url) : null;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        var settings = new Dictionary<string, string?>
        {
            ["Backend:Auth"] = _auth.Url,
            ["Identity:PublicUrl"] = _auth.Url,
            ["Identity:Issuer"] = RealAuthFixture.Issuer,
            ["Identity:Audience"] = RealAuthFixture.Audience,
            ["Site:BaseUrl"] = _site,
            ["DataProtection:KeysPath"] = KeysDirectory,
            // The pages under test never call these; a closed port makes any accidental call fail loudly instead of reaching something.
            ["Backend:Directory"] = "http://127.0.0.1:1", ["Backend:Booking"] = "http://127.0.0.1:1", ["Backend:Reputation"] = "http://127.0.0.1:1", ["Backend:Assistant"] = "http://127.0.0.1:1",
        };
        foreach (var (k, v) in settings) builder.UseSetting(k, v);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            if (_switchable)
                services.AddHttpClient(WebApp::Gabinety.Web.Api.Backend.Auth).ConfigurePrimaryHttpMessageHandler(() => new RewriteHandler(this));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _ownsKeys) try { Directory.Delete(KeysDirectory, recursive: true); } catch (IOException) { }
    }

    sealed class RewriteHandler(BlazorHost host) : DelegatingHandler(new SocketsHttpHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (host.AuthTarget is { } target && request.RequestUri is { } uri)
                request.RequestUri = new UriBuilder(uri) { Scheme = target.Scheme, Host = target.Host, Port = target.Port }.Uri;
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>A visitor of the host. Each has its own cookie jar and its own address (the header the platform's edge would set), so its own rate-limit bucket at authservice.</summary>
    public WebBrowser Visitor(string? address = null) => new(this, address ?? AuthContractSuite.UniqueClientIp());

    internal HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false, BaseAddress = new Uri(_site + "/") });

    /// <summary>What the host sealed into a <c>gt_session</c> cookie, read with the host's own key ring (the test may look; no browser can).</summary>
    public AuthenticationTicket Unseal(string cookieValue)
    {
        var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        return options.TicketDataFormat.Unprotect(cookieValue) ?? throw new InvalidOperationException("the cookie does not unseal");
    }

    /// <summary>The refresh token inside a session cookie (the one thing the cookie holds that authservice knows).</summary>
    public string RefreshTokenIn(string cookieValue) => Unseal(cookieValue).Properties.GetTokenValue(WebApp::Gabinety.Web.Auth.SessionCookie.RefreshTokenName) ?? throw new InvalidOperationException("no refresh token in the cookie");

    public IReadOnlyList<string> RolesIn(string cookieValue) =>
        [.. Unseal(cookieValue).Principal.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value)];
}

/// <summary>A minimal browser: a cookie jar (the Secure flag is not enforced, the client speaks to an in-process server), no redirects followed, antiforgery handled the way static Blazor forms need it.</summary>
public sealed class WebBrowser
{
    public const string SessionCookie = "gt_session";
    readonly HttpClient _client;
    readonly string _address;
    readonly Dictionary<string, string> _cookies = [];

    internal WebBrowser(BlazorHost host, string address) { _client = host.NewClient(); _address = address; }

    public IReadOnlyDictionary<string, string> Cookies => _cookies;
    public string? Session => _cookies.GetValueOrDefault(SessionCookie);
    public WebBrowser WithCookie(string name, string value) { _cookies[name] = value; return this; }

    public Task<WebPage> Get(string path) => Send(new HttpRequestMessage(HttpMethod.Get, path));

    public async Task<WebPage> Send(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("Fly-Client-IP", _address);
        if (_cookies.Count > 0) request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
        var response = await _client.SendAsync(request);
        var setCookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToList() : [];
        foreach (var raw in setCookies)
        {
            var first = raw.Split(';')[0];
            var eq = first.IndexOf('=');
            var (name, value) = (first[..eq], first[(eq + 1)..]);
            var gone = raw.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase) || raw.Contains("max-age=0", StringComparison.OrdinalIgnoreCase);
            if (gone || value.Length == 0) _cookies.Remove(name); else _cookies[name] = value;
        }
        return new WebPage(response, await response.Content.ReadAsStringAsync(), setCookies);
    }

    /// <summary>Opens <paramref name="page"/>, takes the hidden fields of the form named <paramref name="formName"/> (antiforgery token, handler) and posts them with <paramref name="fields"/> to the same address.</summary>
    public async Task<WebPage> Submit(string page, string formName, params (string Name, string Value)[] fields)
    {
        var form = await Get(page);
        Assert.True(form.Status == HttpStatusCode.OK, $"{page} did not render: {(int)form.Status}");
        var hidden = form.HiddenFieldsOf(formName);
        var body = hidden.Where(h => fields.All(f => f.Name != h.Key)).Concat(fields.Select(f => KeyValuePair.Create(f.Name, f.Value))).ToList();
        return await Send(new HttpRequestMessage(HttpMethod.Post, page) { Content = new FormUrlEncodedContent(body) });
    }

    public Task<WebPage> SignIn(string email, string password, string returnTo = "/") =>
        Submit(returnTo == "/" ? "/logowanie" : $"/logowanie?redirect={Uri.EscapeDataString(returnTo)}", "login", ("Model.Email", email), ("Model.Password", password));

    /// <summary>Signs in and insists on it: a redirect and a session cookie.</summary>
    public async Task<WebBrowser> SignedIn(string email, string password)
    {
        var page = await SignIn(email, password);
        Assert.True(page.Status == HttpStatusCode.Found && Session is not null, $"sign-in failed: {(int)page.Status} {page.Alert}");
        return this;
    }
}

public sealed class WebPage(HttpResponseMessage response, string html, IReadOnlyList<string> setCookies)
{
    public HttpResponseMessage Response { get; } = response;
    public HttpStatusCode Status => Response.StatusCode;
    public string Html { get; } = html;
    public IReadOnlyList<string> SetCookies { get; } = setCookies;
    /// <summary>The path and query of a redirect to this site, the whole address of one that leaves it.</summary>
    public string? Location => Response.Headers.Location is { } l ? (l.IsAbsoluteUri && l.Host == "localhost" ? l.PathAndQuery : l.OriginalString) : null;
    /// <summary>The page as text a user reads (tags kept, entities decoded), for "the message says".</summary>
    public string Text => WebUtility.HtmlDecode(Html);
    public string? SetCookie(string name) => SetCookies.FirstOrDefault(c => c.StartsWith(name + "=", StringComparison.Ordinal));
    public string? Header(string name) => Response.Headers.TryGetValues(name, out var v) ? string.Join(", ", v) : Response.Content.Headers.TryGetValues(name, out var c) ? string.Join(", ", c) : null;

    /// <summary>The text of the first alert on the page (every state of the auth pages is one), or null.</summary>
    public string? Alert
    {
        get
        {
            var m = Regex.Match(Html, @"role=""alert""[^>]*>(?<t>.*?)</div>", RegexOptions.Singleline);
            return m.Success ? Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(m.Groups["t"].Value, "<[^>]+>", " ")), @"\s+", " ").Trim() : null;
        }
    }

    public string H1 => Regex.Match(Html, @"<h1[^>]*>(?<t>.*?)</h1>", RegexOptions.Singleline) is { Success: true } m ? WebUtility.HtmlDecode(Regex.Replace(m.Groups["t"].Value, "<[^>]+>", "")).Trim() : "";

    public Dictionary<string, string> HiddenFieldsOf(string formName)
    {
        foreach (Match form in Regex.Matches(Html, @"<form\b.*?</form>", RegexOptions.Singleline))
        {
            var fields = new Dictionary<string, string>();
            foreach (Match input in Regex.Matches(form.Value, @"<input\b[^>]*>", RegexOptions.Singleline))
            {
                if (Attr(input.Value, "type") != "hidden" || Attr(input.Value, "name") is not { } name) continue;
                fields[name] = WebUtility.HtmlDecode(Attr(input.Value, "value") ?? "");
            }
            if (fields.GetValueOrDefault("_handler") == formName) return fields;
        }
        throw new InvalidOperationException($"no form named {formName} on the page");
    }

    static string? Attr(string tag, string name) => Regex.Match(tag, $@"\b{name}=""(?<v>[^""]*)""") is { Success: true } m ? m.Groups["v"].Value : null;
}
