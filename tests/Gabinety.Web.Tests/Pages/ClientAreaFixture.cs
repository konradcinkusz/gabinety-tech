using System.Net;
using System.Text.Json;
using Gabinety.Web.Api;
using Microsoft.Extensions.Logging;

namespace Gabinety.Web.Tests.Pages;

/// <summary>A signed-in browser against the real host with fake backends, and the answers every page test starts from. No network, no identity service (BLAZOR-WEB-APP section 13).</summary>
public sealed class ClientAreaFixture : IAsyncDisposable
{
    public static readonly Guid OfferingId = Guid.Parse("018f0000-0000-7000-8000-0000000000a1");
    public static readonly Guid BookingId = Guid.Parse("018f0000-0000-7000-8000-0000000000b1");
    public static readonly DateTimeOffset Start = new(2026, 12, 8, 11, 0, 0, TimeSpan.Zero); // 12:00 in Warsaw
    public static string BookingUrl => $"/rezerwacja/{OfferingId}?start={Uri.EscapeDataString("2026-12-08T11:00:00Z")}&gabinet=demo-anna-demowska";

    public WebFactory Web { get; }
    public Browser Browser { get; }
    public LogCapture Logs { get; } = new();

    ClientAreaFixture(WebFactory web, Action<WebFactory>? configure)
    {
        Web = web.LogTo(Logs);
        configure?.Invoke(web);
        Web.Directory.Respond = r => r.RequestUri!.AbsolutePath switch
        {
            "/api/v1/practitioners/demo-anna-demowska" => FakeBackend.Json(Profile()),
            "/api/v1/taxonomy" => FakeBackend.Json(Sample.Taxonomy),
            "/api/v1/me" => FakeBackend.Json(new MeDto("user-1", null, null, false, false)),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        Browser = Web.NewBrowser();
    }

    public static async Task<ClientAreaFixture> SignedIn(Action<WebFactory>? configure = null)
    {
        var fixture = new ClientAreaFixture(new WebFactory(), configure);
        await SignIn.As(fixture.Web, fixture.Browser);
        return fixture;
    }

    public static async Task<ClientAreaFixture> Visitor(Action<WebFactory>? configure = null) => new(new WebFactory(), configure);

    public static PractitionerProfileDto Profile(string name = "Anna Demowska", long price = 25000) =>
        new(Guid.NewGuid(), "demo-anna-demowska", name, "Audyty kodu .NET", null, ["email"], null, true, new MoneyDto(price, "PLN"), "Bio demo", null,
            [new OfferingDto(OfferingId, "Audyt kodu demo", "Opis oferty demo", 60, new MoneyDto(price, "PLN"), "audyt-kodu", ["dotnet"])], 0, new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero));

    public static BookingDetailDto Detail(string status = "Confirmed", string practitioner = "Anna Demowska", string client = "demo-client", string payment = "Succeeded", long refunded = 0,
        bool cancel = false, bool dispute = false, bool noShow = false, bool review = false, string? meeting = null, DateTimeOffset? hold = null, string? brief = "Opis problemu demo, syntetyczny.", CancelOutcomeDto? outcome = null,
        MoneyDto? fee = null, MoneyDto? payout = null) =>
        new(BookingId, status, null, Start, Start.AddHours(1), 60, "Audyt kodu demo", new MoneyDto(25000, "PLN"), new PractitionerRefDto(Guid.NewGuid(), practitioner, "demo-anna-demowska"),
            client.Length == 0 ? null : new ClientRefDto(client), brief is null ? null : new BriefDto(brief, null, false), meeting, hold, new PaymentViewDto(payment, new MoneyDto(refunded, "PLN")), fee, payout,
            cancel, dispute, noShow, review, outcome);

    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>Posts the named form of a page (the framework's hidden <c>_handler</c> field says which): its hidden fields (antiforgery, handler, our own) are copied, then <paramref name="fields"/> are added.</summary>
    public async Task<Page> Post(string path, string formName, params (string Name, string Value)[] fields)
    {
        var page = await Browser.GetAsync(path);
        var form = page.All("form[method=post]").FirstOrDefault(f => f.QuerySelector($"input[name=_handler][value='{formName}']") is not null)
            ?? throw new InvalidOperationException($"no form '{formName}' on {path}: {(int)page.Status} {page.Text[..Math.Min(300, page.Text.Length)]}");
        var given = fields.Select(f => f.Name).ToHashSet();
        // a field the test sets replaces the page's own hidden field of that name (a browser would send the value the page rendered, or the one a user typed)
        var all = form.QuerySelectorAll("input[type=hidden]").Where(i => !given.Contains(i.GetAttribute("name")!)).Select(i => KeyValuePair.Create(i.GetAttribute("name")!, i.GetAttribute("value") ?? ""))
            .Concat(fields.Select(f => KeyValuePair.Create(f.Name, f.Value))).ToList();
        var request = new HttpRequestMessage(HttpMethod.Post, form.GetAttribute("action") ?? path) { Content = new FormUrlEncodedContent(all) };
        return await Browser.SendAsync(request);
    }

    public async ValueTask DisposeAsync() => await Web.DisposeAsync();
}

/// <summary>Everything the host logged, so a test can prove that user free text never reaches a log (SPEC 5 rule 2).</summary>
public sealed class LogCapture : ILoggerProvider
{
    readonly List<string> _lines = [];
    public IReadOnlyList<string> Lines { get { lock (_lines) return [.. _lines]; } }
    public ILogger CreateLogger(string categoryName) => new Sink(this);
    public void Dispose() { }

    sealed class Sink(LogCapture owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._lines) owner._lines.Add(formatter(state, exception) + (exception is null ? "" : "\n" + exception));
        }
    }
}
