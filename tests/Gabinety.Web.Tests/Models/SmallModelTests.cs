using Gabinety.ServiceDefaults;
using Gabinety.Web.Api;
using Gabinety.Web.Auth;
using Gabinety.Web.Components.Layout;
using Gabinety.Web.Components.Pages.Bookings;
using Gabinety.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Gabinety.Web.Tests.Models;

public class ReturnTargetTests
{
    [Theory]
    [InlineData("/moje/rezerwacje", "/moje/rezerwacje")]
    [InlineData("/moje/rezerwacje?status=Confirmed&role=practitioner", "/moje/rezerwacje?status=Confirmed&role=practitioner")]
    [InlineData("/programisci/audyt-kodu?q=a%20b", "/programisci/audyt-kodu?q=a%20b")]
    [InlineData(null, "/")] [InlineData("", "/")] [InlineData("moje", "/")]
    [InlineData("//evil.example", "/")] [InlineData("/\\evil.example", "/")] [InlineData("https://evil.example/x", "/")] [InlineData("http://x.invalid.evil/", "/")]
    [InlineData("/ok\r\nSet-Cookie: a=b", "/")] [InlineData("/a\\b", "/")]
    [InlineData("/api/auth/login", "/")] [InlineData("/api", "/")]
    [InlineData("/logowanie", "/")] [InlineData("/logowanie?redirect=/x", "/")] [InlineData("/wyloguj", "/")] [InlineData("/oauth/callback?code=1", "/")]
    public void Only_a_same_site_page_path_is_a_return_target(string? raw, string expected) => Assert.Equal(expected, ReturnTarget.Safe(raw));

    [Fact]
    public void The_login_link_carries_the_target_in_the_one_return_parameter()
    {
        Assert.Equal("/logowanie", ReturnTarget.LoginUrl("/"));
        Assert.Equal("/logowanie?redirect=%2Fmoje%2Frezerwacje%3Fstatus%3DConfirmed", ReturnTarget.LoginUrl("/moje/rezerwacje?status=Confirmed"));
    }
}

public class ClientAddressTests
{
    static HttpContext Context(string? fly = null, string? xff = null, string? remote = null)
    {
        var c = new DefaultHttpContext();
        if (fly is not null) c.Request.Headers["Fly-Client-IP"] = fly;
        if (xff is not null) c.Request.Headers["X-Forwarded-For"] = xff;
        if (remote is not null) c.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remote);
        return c;
    }

    [Fact] public void The_platform_header_wins_over_everything() => Assert.Equal("203.0.113.7", ClientAddress.Of(Context("203.0.113.7", "198.51.100.1, 198.51.100.2", "10.0.0.1")));

    [Fact] public void Of_X_Forwarded_For_the_LAST_entry_is_used_never_the_first_which_the_visitor_can_write() => Assert.Equal("198.51.100.2", ClientAddress.Of(Context(xff: "1.2.3.4, 198.51.100.2")));

    [Theory]
    [InlineData("not an address")] [InlineData("1.2.3.4:80")] [InlineData("")] [InlineData("1.2.3.4, evil")]
    public void Anything_that_is_not_exactly_one_IP_address_is_ignored(string xff) => Assert.Null(ClientAddress.Of(Context(xff: xff)));

    [Fact] public void An_invalid_platform_header_falls_through_to_the_forwarded_list() => Assert.Equal("198.51.100.2", ClientAddress.Of(Context("garbage", "198.51.100.2")));

    [Fact] public void A_host_reached_directly_uses_the_address_of_the_connection() => Assert.Equal("192.0.2.9", ClientAddress.Of(Context(remote: "192.0.2.9")));

    [Fact] public void An_IPv4_mapped_address_is_normalised() => Assert.Equal("192.0.2.9", ClientAddress.Of(Context(remote: "::ffff:192.0.2.9")));

    [Fact] public void Without_a_request_there_is_no_address() => Assert.Null(ClientAddress.Of(null));
}

public class BrowserOriginGuardDecisionTests
{
    static readonly HashSet<string> Own = ["http://localhost:3001"];

    [Theory]
    [InlineData("POST", "https://evil.example", null, null, null, true)]          // foreign Origin
    [InlineData("GET", "https://evil.example", null, null, null, true)]           // ... on a GET too: the websocket handshake of a circuit is a GET
    [InlineData("POST", "http://localhost:3001", null, null, null, false)]        // own Origin
    [InlineData("POST", "http://localhost:3001/", null, null, null, false)]       // a trailing slash is the same origin
    [InlineData("POST", "http://localhost:3002", null, null, null, true)]         // another port is another origin
    [InlineData("POST", "null", null, null, null, true)]                           // an opaque origin (sandboxed frame, cross-origin redirect)
    [InlineData("POST", null, null, null, null, false)]                            // no Origin: curl, the server-side HttpClient, probes
    [InlineData("POST", null, "cross-site", "cors", "empty", true)]
    [InlineData("POST", null, "cross-site", "navigate", "document", true)]        // a cross-site form post is a navigation too, but it changes something
    [InlineData("GET", null, "cross-site", "websocket", "websocket", true)]
    [InlineData("GET", null, "cross-site", "navigate", "document", false)]        // a link followed from another site
    [InlineData("HEAD", null, "cross-site", "navigate", "document", false)]
    [InlineData("GET", null, "cross-site", "navigate", "iframe", true)]           // a framed page is not a link navigation
    [InlineData("GET", null, "cross-site", "cors", "empty", true)]
    [InlineData("POST", null, "same-origin", "cors", "empty", false)]
    [InlineData("POST", null, "same-site", "cors", "empty", false)]
    [InlineData("POST", null, "none", "navigate", "document", false)]
    public void The_decision_table(string method, string? origin, string? site, string? mode, string? dest, bool refused) =>
        Assert.Equal(refused, BrowserOriginGuard.IsRefused(method, origin, site, mode, dest, Own));

    [Fact]
    public void The_configured_origins_are_the_site_and_the_extra_list_only_as_scheme_and_authority()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Site:BaseUrl"] = "https://gabinety.example/some/path", ["AllowedOrigins"] = "https://www.gabinety.example, not a url, ftp://x", ["AllowedOrigins:0"] = "https://b.gabinety.example",
        }).Build();
        Assert.Equivalent(new[] { "https://gabinety.example", "https://www.gabinety.example", "https://b.gabinety.example" }, BrowserOriginGuard.ConfiguredOrigins(config));
    }
}

public class WebOptionsTests
{
    [Fact]
    public void Backends_are_ordered_candidate_lists_and_become_service_discovery_endpoints()
    {
        var o = WebOptions.From(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Backend:Directory"] = "http://localhost:5101/, https://directory.internal:8443", ["Backend:Auth"] = "ftp://nope, not-a-url", ["Site:BaseUrl"] = "https://gabinety.example/",
        }).Build());
        Assert.Equal(["http://localhost:5101", "https://directory.internal:8443"], o.Backends["directory"]);
        Assert.Empty(o.Backends["auth"]);
        Assert.Equal("https://gabinety.example", o.SiteBaseUrl);
        Assert.True(o.SecureCookies);
        Assert.Equal([KeyValuePair.Create<string, string?>("Services:directory:http:0", "http://localhost:5101"), KeyValuePair.Create<string, string?>("Services:directory:https:1", "https://directory.internal:8443")], o.ServiceConfiguration());
    }

    [Fact] public void Defaults_are_the_fake_profile_address_and_no_proxy_key() { var o = WebOptions.From(new ConfigurationBuilder().Build()); Assert.Equal("http://localhost:3000", o.SiteBaseUrl); Assert.Null(o.TrustedProxyKey); Assert.False(o.SecureCookies); }
}

public class FlashKeyTests
{
    [Fact] public void A_known_key_shows_its_own_text() => Assert.Equal("Wylogowano.", FlashKey.Resolve("http://localhost/?flash=signed-out"));

    [Theory] [InlineData("http://localhost/?flash=<script>alert(1)</script>")] [InlineData("http://localhost/?flash=Twoje%20konto%20zosta%C5%82o%20zablokowane")] [InlineData("http://localhost/")] [InlineData("/relative")]
    public void Text_from_the_query_string_is_never_rendered(string uri) => Assert.Null(FlashKey.Resolve(uri));
}

public class BookingFormatTests
{
    [Fact]
    public void Times_are_shown_in_Warsaw_in_winter_and_in_summer()
    {
        Assert.Equal("wtorek, 1 grudnia, 13:00–14:00", BookingFormat.When(Sample.Booking())); // 12:00Z, UTC+1
        var summer = Sample.Booking() with { StartUtc = new DateTimeOffset(2026, 7, 14, 12, 30, 0, TimeSpan.Zero), EndUtc = new DateTimeOffset(2026, 7, 14, 13, 0, 0, TimeSpan.Zero) };
        Assert.Equal("wtorek, 14 lipca, 14:30–15:00", BookingFormat.When(summer)); // UTC+2
    }

    [Fact] public void Every_contract_status_has_a_polish_label_and_an_unknown_one_says_so() { foreach (var s in BookingFormat.Statuses) Assert.NotEqual("Status nieznany", BookingFormat.StatusLabel(s)); Assert.Equal("Status nieznany", BookingFormat.StatusLabel("Whatever")); Assert.Equal(10, BookingFormat.Statuses.Length); }

    [Fact]
    public void The_other_party_is_named_or_neutral()
    {
        var b = Sample.Booking();
        Assert.Equal("Anna Demowska", BookingFormat.OtherParty(b, "client"));
        Assert.Equal("demo-client", BookingFormat.OtherParty(b, "practitioner"));
        Assert.Equal("Klient", BookingFormat.OtherParty(b with { Client = null }, "practitioner"));
        Assert.Equal("Programista", BookingFormat.OtherParty(b with { Practitioner = b.Practitioner with { DisplayName = " " } }, "client"));
    }

    [Theory]
    [InlineData(0, "unavailable", "Brak połączenia z usługą rezerwacji. Spróbuj ponownie za chwilę.")]
    [InlineData(503, "dependency_unavailable", "Rezerwacje są chwilowo niedostępne. Spróbuj ponownie za chwilę.")]
    [InlineData(429, "rate_limited", "Zbyt wiele zapytań. Spróbuj ponownie za chwilę.")]
    [InlineData(403, "forbidden", "Nie masz dostępu do tych rezerwacji.")]
    [InlineData(500, "http_500", "Nie udało się wczytać rezerwacji.")]
    public void A_failure_is_told_from_its_code_never_from_the_servers_text(int status, string code, string text) => Assert.Equal(text, BookingFormat.FailureText(new ApiFailure(status, code)));

    [Theory] [InlineData("practitioner", "practitioner")] [InlineData("client", "client")] [InlineData("admin", "client")] [InlineData(null, "client")]
    public void The_role_is_one_of_two(string? raw, string expected) => Assert.Equal(expected, BookingFormat.Role(raw));
}
