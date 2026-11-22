using System.Net;
using AngleSharp.Dom;
using Gabinety.Web.Api;

namespace Gabinety.Web.Tests;

/// <summary>An administrator's browser against the real host with fake backends: signed in through the real form, the identity fake issuing the Admin role.</summary>
public sealed class AdminHost : IAsyncDisposable
{
    public WebFactory Web { get; } = new();
    public Browser Browser { get; private set; } = default!;

    public static async Task<AdminHost> StartAsync(string[]? roles = null, string email = "demo-admin@demo.example")
    {
        var host = new AdminHost();
        host.Web.Identity.Roles = roles ?? ["Admin"];
        host.Browser = host.Web.NewBrowser();
        await SignIn.As(host.Web, host.Browser, email);
        return host;
    }

    public static async Task<AdminHost> StartAsClientAsync() => await StartAsync([], "demo-client@demo.example");

    /// <summary>Posts the form whose handler is <paramref name="formName"/> on <paramref name="page"/>: its hidden fields (antiforgery token, handler) plus <paramref name="fields"/>, to the page's own address.</summary>
    public async Task<Page> PostAsync(Page page, string path, string formName, params (string Name, string Value)[] fields)
    {
        var form = page.All("form[method=post]").FirstOrDefault(f => f.QuerySelector("input[name=_handler]")?.GetAttribute("value") == formName)
            ?? throw new InvalidOperationException($"no form '{formName}' on {path}; forms: {string.Join(", ", page.All("form[method=post] input[name=_handler]").Select(i => i.GetAttribute("value")))}");
        return await Browser.SendAsync(new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(Hidden(form).Concat(fields.Select(f => KeyValuePair.Create(f.Name, f.Value)))) });
    }

    /// <summary>Posts without taking the hidden fields from a rendered form: for the cases where the form is not on the page (a stale post, a forged one).</summary>
    public async Task<Page> PostRawAsync(string path, IEnumerable<KeyValuePair<string, string>> fields, params (string Name, string Value)[] headers) =>
        await Browser.SendAsync(new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(fields) }, headers);

    public static IEnumerable<KeyValuePair<string, string>> Hidden(IElement form) =>
        form.QuerySelectorAll("input[type=hidden]").Select(i => KeyValuePair.Create(i.GetAttribute("name")!, i.GetAttribute("value") ?? ""));

    public async ValueTask DisposeAsync()
    {
        var thrown = Web.Errors.Where(e => e.Contains("Unhandled exception")).ToList(); // a component that threw is caught by the error boundary: the page "works" and a test that only reads the HTML would not notice
        await Web.DisposeAsync();
        Assert.True(thrown.Count == 0, "a component threw: " + string.Join("\n", thrown.Select(t => t[..Math.Min(400, t.Length)])));
    }

    // ---- fake backend data (synthetic: demo-)
    public static AdminPractitionerRowDto Row(string name = "Anna Demowska", string status = "Submitted", Guid? id = null, bool github = true, bool nip = true, string? reason = null) =>
        new(id ?? Guid.NewGuid(), "demo-" + name.ToLowerInvariant().Replace(' ', '-'), name, "Audyty kodu .NET demo", status, reason, github, github ? "demo-dev" : null, nip,
            new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 24, 12, 0, 9, TimeSpan.Zero));

    /// <summary>The JSON a backend received, parsed: the wire may escape non-ASCII characters, which is the same text.</summary>
    public static System.Text.Json.JsonElement Body(Recorded request) => System.Text.Json.JsonDocument.Parse(request.Body ?? throw new InvalidOperationException("no body")).RootElement.Clone();

    /// <summary>The value (dd) of the fact (dt) with this label in a card's definition list.</summary>
    public static string Fact(IElement root, string label) =>
        Page.Squash(root.QuerySelectorAll("dt").FirstOrDefault(d => d.TextContent.Trim() == label)?.NextElementSibling?.TextContent ?? throw new InvalidOperationException($"no fact '{label}'")).Replace('\u00A0', ' ');

    public static AdminDisputeDto Dispute(Guid? id = null, string kind = "not_as_described", string text = "Konsultacja różniła się od opisu, dane syntetyczne testu.", string client = "demo-client-2", string practitioner = "demo-practitioner-4") =>
        new(id ?? Guid.NewGuid(), client, practitioner, kind, text, new DateTimeOffset(2026, 11, 24, 12, 0, 9, TimeSpan.Zero), null, null, null);

    public static AdminBookingDto BookingOf(Guid id, long price = 18000, string title = "Audyt kodu .NET", string practitionerName = "", Guid? practitionerId = null, string payment = "Succeeded", long refunded = 0) =>
        new(id, "Disputed", new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero), title, new MoneyDto(price, "PLN"), new PractitionerRefDto(practitionerId ?? Guid.NewGuid(), practitionerName, "demo-anna"), null,
            new PaymentViewDto(payment, new MoneyDto(refunded, "PLN")));

    public static AdminPractitionerDetailDto DetailOf(Guid id, string name = "Anna Demowska", string status = "Approved") =>
        new(id, "demo-anna", name, "Audyty kodu .NET demo", "O mnie: **demo**.", status, null, true, "demo-dev", "5260250995", true, "https://demo.example/portfolio",
            new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero), null);

    public static HttpResponseMessage Paged<T>(params T[] items) => FakeBackend.Json(new PageDto<T>(items, null));
    public static HttpResponseMessage Paged<T>(string? next, params T[] items) => FakeBackend.Json(new PageDto<T>(items, next));
    public static HttpResponseMessage NoContent() => new(HttpStatusCode.NoContent);
}
