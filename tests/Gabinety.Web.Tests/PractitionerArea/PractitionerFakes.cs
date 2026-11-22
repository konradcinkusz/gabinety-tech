using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gabinety.Web.Api;

namespace Gabinety.Web.Tests.PractitionerAreaTests;

/// <summary>
/// A stateful stand-in for the practitioner routes of Directory (CONTRACTS 3.3, 3.5) and Booking (4.2, payments account): enough of the services' own rules (field validation, the checklist,
/// the limit of five active offers, the single-use onboarding link) that the pages are tested through real requests and real answers, not through canned ones. It answers like the services:
/// RFC 9457 with a <c>code</c> and <c>errors</c> keys.
/// </summary>
public sealed class PractitionerWorld
{
    public sealed class Offer { public Guid Id = Guid.NewGuid(); public string Title = "", Description = "", ProblemSlug = "", MeetingUrl = ""; public int Duration; public long Price; public string[] Techs = []; public bool Active = true; }

    public PractitionerPrivateDto? Profile { get; set; }
    public List<Offer> Offers { get; } = [];
    public List<(int Day, string From, string To)> Rules { get; } = [];
    public List<string> Exceptions { get; } = [];
    public string? PaymentState { get; set; } = "none"; // null: the mock provider (404)
    public bool EmailVerified { get; set; } = true;
    public string? PendingGithubLogin { get; set; }
    public bool GithubBioHasCode { get; set; }
    public bool DirectoryDown { get; set; }
    public bool BookingDown { get; set; }
    public bool BookingsFail { get; set; }
    public List<BookingViewDto> Bookings { get; } = [];
    public string OnboardingUrl { get; set; } = "https://connect.stripe.com/setup/e/acct_test/abc";
    public int OnboardingCalls { get; private set; }

    public static readonly JsonSerializerOptions Json = ApiClient.Json;

    PractitionerPrivateDto Current => Profile! with { EmailVerified = EmailVerified };

    public void Attach(WebFactory web)
    {
        web.Directory.Respond = Directory;
        web.Booking.Respond = Booking;
    }

    public void HaveProfile(string status = "Draft", string name = "Demo Programista") =>
        Profile = new PractitionerPrivateDto(Guid.NewGuid(), "demo-programista", status, null, name, "", "", null, null, null, EmailVerified, false, false, null);

    public void CompleteProfile(bool github = true)
    {
        Profile = Profile! with { Headline = "Testy end to end dla zespołów", Bio = new string('b', 220), PortfolioUrl = "https://demo.example.com/", Nip = "5260250274", NipValid = true, GithubLinked = github, GithubLogin = github ? "demo-gh" : null };
        if (Offers.Count == 0) Offers.Add(new Offer { Title = "Przegląd testów", ProblemSlug = "code-review", Duration = 30, Price = 18000 });
    }

    static HttpResponseMessage Problem(HttpStatusCode status, string code, string[]? fields = null, string[]? unmet = null)
    {
        var o = new JsonObject { ["type"] = $"urn:gabinety:error:{code}", ["title"] = "SERVER-TITLE-MUST-NOT-APPEAR", ["status"] = (int)status, ["detail"] = "SERVER-DETAIL-MUST-NOT-APPEAR", ["code"] = code };
        if (fields is not null) { var e = new JsonObject(); foreach (var f in fields) e[f] = new JsonArray("SERVER-MESSAGE-MUST-NOT-APPEAR"); o["errors"] = e; }
        if (unmet is not null) o["unmetItems"] = new JsonArray(unmet.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray());
        return new HttpResponseMessage(status) { Content = new StringContent(o.ToJsonString(), System.Text.Encoding.UTF8, "application/problem+json") };
    }

    static HttpResponseMessage Unit() => new(HttpStatusCode.NoContent);

    static JsonObject? Body(HttpRequestMessage r) => r.Content is null ? null : JsonNode.Parse(r.Content.ReadAsStringAsync().GetAwaiter().GetResult()) as JsonObject;

    static string S(JsonObject? b, string name) => b?[name]?.GetValue<string>() ?? "";

    static bool Contact(string text) => text.Contains('@') || text.Contains("telegram", StringComparison.OrdinalIgnoreCase);

    OfferingPrivateDto View(Offer o) => new(o.Id, o.Title, o.Description, o.Duration, new MoneyDto(o.Price, "PLN"), o.ProblemSlug, o.Techs, o.Active, string.IsNullOrEmpty(o.MeetingUrl) ? null : o.MeetingUrl);

    HttpResponseMessage Directory(HttpRequestMessage r)
    {
        if (DirectoryDown) throw new HttpRequestException("directory is down");
        var path = r.RequestUri!.AbsolutePath;
        var method = r.Method.Method;
        const string me = "/api/v1/me/practitioner";
        if (path == "/api/v1/taxonomy") return FakeBackend.Json(new TaxonomyDto("1", [new CategoryDto("code-review", "Przegląd kodu"), new CategoryDto("migration", "Migracja")], [new CategoryDto("dotnet", ".NET"), new CategoryDto("react", "React"), new CategoryDto("sql", "SQL"), new CategoryDto("go", "Go"), new CategoryDto("rust", "Rust")]));
        if (!path.StartsWith(me)) return new HttpResponseMessage(HttpStatusCode.NotFound);
        if (path == me && method == "POST")
        {
            if (Profile is not null) return Problem(HttpStatusCode.Conflict, "practitioner_exists");
            var name = S(Body(r), "displayName").Trim();
            if (name.Length is < 2 or > 80) return Problem(HttpStatusCode.UnprocessableEntity, "validation_failed", ["displayName"]);
            HaveProfile(name: name);
            return FakeBackend.Json(Current, HttpStatusCode.Created);
        }
        if (Profile is null) return Problem(HttpStatusCode.NotFound, "not_found");
        if (path == me && method == "GET") return FakeBackend.Json(Current);
        if (path == me && method == "PUT")
        {
            var b = Body(r)!;
            var errors = new List<string>();
            var headline = S(b, "headline").Trim();
            if (headline.Length is > 0 and < 10 || headline.Length > 120) errors.Add("headline");
            if (S(b, "bio").Length > 2000) errors.Add("bio");
            var nip = S(b, "nip");
            if (nip.Length > 0 && nip != "5260250274") errors.Add("nip");
            if (S(b, "displayName").Trim().Length is < 2 or > 80) errors.Add("displayName");
            if (errors.Count > 0) return Problem(HttpStatusCode.UnprocessableEntity, "validation_failed", [.. errors]);
            if (Contact(headline) || Contact(S(b, "bio"))) return Problem(HttpStatusCode.UnprocessableEntity, "contact_details_not_allowed");
            Profile = Profile with
            {
                DisplayName = S(b, "displayName").Trim(), Headline = headline, Bio = S(b, "bio").Trim(), PortfolioUrl = b["portfolioUrl"]?.GetValue<string>(),
                MeetingUrlDefault = b["meetingUrlDefault"]?.GetValue<string>(), Nip = b["nip"]?.GetValue<string>(), NipValid = nip == "5260250274",
            };
            return FakeBackend.Json(Current);
        }
        if (path == me + "/submit")
        {
            if (Profile.Status is not ("Draft" or "Rejected")) return Problem(HttpStatusCode.Conflict, "invalid_transition");
            var unmet = new List<string>();
            if (Profile.Headline.Length == 0) unmet.Add("headline");
            if (Profile.Bio.Length < 200) unmet.Add("bio");
            if (!Offers.Any(o => o.Active)) unmet.Add("offering");
            if (!Profile.GithubLinked) unmet.Add("github_linked");
            if (!Profile.NipValid) unmet.Add("nip_valid");
            if (Profile.PortfolioUrl is null) unmet.Add("portfolioUrl");
            if (!EmailVerified) unmet.Add("email_verified");
            if (unmet.Count > 0) return Problem(HttpStatusCode.UnprocessableEntity, "submit_incomplete", unmet: [.. unmet]);
            Profile = Profile with { Status = "Submitted" };
            return Unit();
        }
        if (path == me + "/github-link" && method == "POST")
        {
            var login = S(Body(r), "githubLogin");
            if (login.Length is 0 or > 39) return Problem(HttpStatusCode.UnprocessableEntity, "validation_failed", ["githubLogin"]);
            PendingGithubLogin = login;
            return FakeBackend.Json(new GithubChallengeDto("gabinety-verify-ABCDEFGHJK"));
        }
        if (path == me + "/github-link/verify")
        {
            if (PendingGithubLogin is null || !GithubBioHasCode) return Problem(HttpStatusCode.UnprocessableEntity, "github_not_linked");
            Profile = Profile with { GithubLinked = true, GithubLogin = PendingGithubLogin };
            PendingGithubLogin = null;
            return Unit();
        }
        if (path == me + "/offerings" && method == "GET") return FakeBackend.Json(new { items = Offers.Select(View) });
        if (path == me + "/offerings" && method == "POST")
        {
            var b = Body(r)!;
            var errors = Validate(b);
            if (errors.Count > 0) return Problem(HttpStatusCode.UnprocessableEntity, "validation_failed", [.. errors]);
            if (Contact(S(b, "title")) || Contact(S(b, "description"))) return Problem(HttpStatusCode.UnprocessableEntity, "contact_details_not_allowed");
            if (Offers.Count(o => o.Active) >= 5) return Problem(HttpStatusCode.UnprocessableEntity, "too_many_offerings");
            var o = new Offer(); Apply(o, b); Offers.Add(o);
            return FakeBackend.Json(View(o), HttpStatusCode.Created);
        }
        var parts = path[(me.Length + "/offerings/".Length)..].Split('/');
        if (path.StartsWith(me + "/offerings/") && Guid.TryParse(parts[0], out var id))
        {
            var o = Offers.SingleOrDefault(x => x.Id == id);
            if (o is null) return Problem(HttpStatusCode.NotFound, "not_found");
            if (method == "PUT")
            {
                var b = Body(r)!;
                var errors = Validate(b);
                if (errors.Count > 0) return Problem(HttpStatusCode.UnprocessableEntity, "validation_failed", [.. errors]);
                Apply(o, b);
                return FakeBackend.Json(View(o));
            }
            if (parts.Length == 2 && parts[1] == "deactivate") { o.Active = false; return Unit(); }
            if (parts.Length == 2 && parts[1] == "activate")
            {
                if (!o.Active && Offers.Count(x => x.Active) >= 5) return Problem(HttpStatusCode.UnprocessableEntity, "too_many_offerings");
                o.Active = true;
                return Unit();
            }
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    static List<string> Validate(JsonObject b)
    {
        var errors = new List<string>();
        if (S(b, "title").Trim().Length is < 5 or > 80) errors.Add("title");
        if (S(b, "description").Length > 600) errors.Add("description");
        if (b["durationMinutes"]?.GetValue<int>() is not (30 or 60)) errors.Add("durationMinutes");
        if (b["priceMinor"]?.GetValue<long>() is not (>= 5000 and <= 150000)) errors.Add("priceMinor");
        if (S(b, "problemSlug") is not ("code-review" or "migration")) errors.Add("problemSlug");
        if ((b["techSlugs"] as JsonArray)?.Count > 4) errors.Add("techSlugs");
        return errors;
    }

    static void Apply(Offer o, JsonObject b)
    {
        o.Title = S(b, "title").Trim(); o.Description = S(b, "description").Trim(); o.Duration = b["durationMinutes"]!.GetValue<int>(); o.Price = b["priceMinor"]!.GetValue<long>();
        o.ProblemSlug = S(b, "problemSlug"); o.MeetingUrl = S(b, "meetingUrl"); o.Techs = [.. (b["techSlugs"] as JsonArray ?? []).Select(x => x!.GetValue<string>())];
    }

    HttpResponseMessage Booking(HttpRequestMessage r)
    {
        if (BookingDown) throw new HttpRequestException("booking is down");
        var path = r.RequestUri!.AbsolutePath;
        if (path == "/api/v1/me/availability")
        {
            if (Profile is null) return Problem(HttpStatusCode.Forbidden, "forbidden");
            if (r.Method == HttpMethod.Get) return FakeBackend.Json(Availability());
            var b = Body(r)!;
            var rules = (b["rules"] as JsonArray ?? []).Select(x => (Day: x!["isoWeekday"]!.GetValue<int>(), From: x["startLocal"]!.GetValue<string>(), To: x["endLocal"]!.GetValue<string>())).ToList();
            if (rules.Any(x => string.CompareOrdinal(x.To, x.From) <= 0)) return Problem(HttpStatusCode.UnprocessableEntity, "validation_failed", ["rules[0].endLocal"]);
            Rules.Clear(); Rules.AddRange(rules);
            Exceptions.Clear(); Exceptions.AddRange((b["exceptions"] as JsonArray ?? []).Select(x => x!["dateLocal"]!.GetValue<string>()));
            return FakeBackend.Json(Availability());
        }
        if (path == "/api/v1/me/bookings" && r.Method == HttpMethod.Get)
        {
            if (BookingsFail) return Problem(HttpStatusCode.ServiceUnavailable, "dependency_unavailable");
            return FakeBackend.Json(new PageDto<BookingViewDto>(Bookings, null));
        }
        if (path == "/api/v1/me/payments/account")
        {
            if (Profile is null) return Problem(HttpStatusCode.Forbidden, "forbidden");
            if (PaymentState is null) return Problem(HttpStatusCode.NotFound, "not_found");
            if (r.Method == HttpMethod.Get) return FakeBackend.Json(new PaymentAccountDto("Stripe", PaymentState));
        }
        if (path == "/api/v1/me/payments/account/onboarding" && r.Method == HttpMethod.Post)
        {
            if (PaymentState is null) return Problem(HttpStatusCode.NotFound, "not_found");
            OnboardingCalls++;
            if (PaymentState == "ready") return FakeBackend.Json(new PaymentOnboardingDto("Stripe", "ready", null));
            PaymentState = "pending";
            return FakeBackend.Json(new PaymentOnboardingDto("Stripe", "pending", OnboardingUrl));
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    AvailabilityDto Availability() => new([.. Rules.Select(x => new AvailabilityRuleDto(x.Day, x.From, x.To))], [.. Exceptions.Select(e => new AvailabilityExceptionDto(e))]);
}

public static class PractitionerBrowsers
{
    /// <summary>How many elements have exactly this text and nothing else (what a journey's <c>getByText(text, {exact: true})</c> counts: strict mode needs one).</summary>
    public static int ExactText(this Page page, string text) => page.All("*").Count(e => e.ChildElementCount == 0 && e.TextContent == text);

    /// <summary>A browser signed in through the real form, with the fake world attached to the factory.</summary>
    public static async Task<(WebFactory Web, Browser Browser, PractitionerWorld World)> SignedIn(Action<PractitionerWorld>? setup = null)
    {
        var web = new WebFactory();
        var world = new PractitionerWorld();
        setup?.Invoke(world);
        world.Attach(web);
        var browser = web.NewBrowser();
        await SignIn.As(web, browser);
        return (web, browser, world);
    }

    /// <summary>Opens <paramref name="path"/>, finds the form whose handler is <paramref name="handler"/> and posts it with <paramref name="fields"/> (the hidden fields come from the page).</summary>
    public static async Task<Page> Submit(this Browser browser, string path, string handler, IEnumerable<KeyValuePair<string, string>>? fields = null, string? command = null, Action? afterGet = null)
    {
        var page = await browser.GetAsync(path);
        afterGet?.Invoke();
        var form = page.All("form[method=post]").FirstOrDefault(f => f.QuerySelector("input[name=_handler]")?.GetAttribute("value") == handler)
            ?? throw new InvalidOperationException($"no form '{handler}' on {path} ({(int)page.Status}): {page.Html[..Math.Min(300, page.Html.Length)]}");
        var all = form.QuerySelectorAll("input[name]").Where(i => i.GetAttribute("type") != "checkbox" && i.GetAttribute("type") != "submit").Select(i => KeyValuePair.Create(i.GetAttribute("name")!, i.GetAttribute("value") ?? ""))
            .Where(kv => fields is null || !fields.Any(f => f.Key == kv.Key)).Concat(form.QuerySelectorAll("select").Select(s => KeyValuePair.Create(s.GetAttribute("name")!, s.QuerySelector("option[selected]")?.GetAttribute("value") ?? s.QuerySelector("option")?.GetAttribute("value") ?? "")).Where(kv => fields is null || !fields.Any(f => f.Key == kv.Key)))
            .Concat(fields ?? []).ToList();
        if (command is not null) all.Add(KeyValuePair.Create("cmd", command));
        var action = form.GetAttribute("action");
        var request = new HttpRequestMessage(HttpMethod.Post, string.IsNullOrEmpty(action) ? path : action) { Content = new FormUrlEncodedContent(all) };
        return await browser.SendAsync(request);
    }
}
