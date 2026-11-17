using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Gabinety.DemoSeeder;

public sealed record MyPractitioner(Guid Id, string Slug, string Status, string DisplayName, string Headline, string Bio, string? PortfolioUrl,
    string? MeetingUrlDefault, string? Nip, bool GithubLinked, string? GithubLogin);
public sealed record MyOffering(Guid Id, string Title, string Description, int DurationMinutes, long PriceMinor, string ProblemSlug, string[] TechSlugs, bool IsActive);
public sealed record TaxonomyView(string[] Problems, string[] Techs);

/// <summary>The Directory API as the seeder uses it: user calls with a bearer token, admin calls, and the internal pushes with X-Internal-Key. Errors carry the status and the error code, never a body.</summary>
public sealed class DirectoryApi(HttpClient http, SeedOptions options, TimeProvider time)
{
    static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    const int MaxAttempts = SeedRetry.MaxAttempts;

    sealed record Reply(HttpStatusCode Status, JsonElement? Json);

    async Task<Reply> SendAsync(HttpMethod method, string path, string? bearer, bool internalKey, object? body, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(method, path.TrimStart('/'));
            if (bearer is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            if (internalKey) req.Headers.Add("X-Internal-Key", options.DirectoryKey);
            if (body is not null) req.Content = JsonContent.Create(body, options: J);
            using var res = await http.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxAttempts) // the 60 per minute limit (CONTRACTS 10.2): wait for the window, then retry
            {
                await Task.Delay(res.Headers.RetryAfter?.Delta ?? SeedRetry.DefaultWait, time, ct);
                continue;
            }
            JsonElement? json = null;
            if (res.Content.Headers.ContentLength is > 0 || res.Content.Headers.ContentType is not null)
            {
                try { json = await res.Content.ReadFromJsonAsync<JsonElement>(J, ct); } catch (JsonException) { }
            }
            return new Reply(res.StatusCode, json);
        }
    }

    static string Code(Reply r) => r.Json is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()! : "-";

    static SeedException Fail(string what, Reply r)
    {
        var unmet = r.Json is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty("unmetItems", out var u) && u.ValueKind == JsonValueKind.Array
            ? " unmet: " + string.Join(",", u.EnumerateArray().Select(x => x.GetString())) : "";
        return new SeedException($"directory: {what} answered {(int)r.Status} {Code(r)}{unmet}");
    }

    static T Read<T>(Reply r, string what) => r.Json is { } j ? j.Deserialize<T>(J)! : throw Fail(what, r);

    // ---- public

    public async Task<TaxonomyView> TaxonomyAsync(CancellationToken ct)
    {
        var r = await SendAsync(HttpMethod.Get, "/api/v1/taxonomy", null, false, null, ct);
        if (r.Status != HttpStatusCode.OK || r.Json is not { } j) throw Fail("taxonomy", r);
        string[] Slugs(string name) => j.GetProperty(name).EnumerateArray().Select(c => c.GetProperty("slug").GetString()!).ToArray();
        return new TaxonomyView(Slugs("problems"), Slugs("techs"));
    }

    // ---- the practitioner's own

    public async Task<MyPractitioner?> GetMineAsync(DemoUser u, CancellationToken ct)
    {
        var r = await SendAsync(HttpMethod.Get, "/api/v1/me/practitioner", u.AccessToken, false, null, ct);
        return r.Status == HttpStatusCode.NotFound ? null : r.Status == HttpStatusCode.OK ? Read<MyPractitioner>(r, "get profile") : throw Fail("get profile", r);
    }

    public async Task<MyPractitioner> CreateAsync(DemoUser u, string displayName, string slug, CancellationToken ct)
    {
        var r = await SendAsync(HttpMethod.Post, "/api/v1/me/practitioner", u.AccessToken, false, new { displayName, slug }, ct);
        return r.Status == HttpStatusCode.Created ? Read<MyPractitioner>(r, "create profile") : throw Fail("create profile", r);
    }

    public async Task<MyPractitioner> UpdateAsync(DemoUser u, object body, CancellationToken ct)
    {
        var r = await SendAsync(HttpMethod.Put, "/api/v1/me/practitioner", u.AccessToken, false, body, ct);
        return r.Status == HttpStatusCode.OK ? Read<MyPractitioner>(r, "update profile") : throw Fail("update profile", r);
    }

    /// <summary>The two-step bio challenge (CONTRACTS 3.3). The code is passed on to verify by the server's own state; it is never returned to a caller or a log.</summary>
    public async Task LinkGithubAsync(DemoUser u, string login, CancellationToken ct)
    {
        var r = await SendAsync(HttpMethod.Post, "/api/v1/me/practitioner/github-link", u.AccessToken, false, new { githubLogin = login }, ct);
        if (r.Status != HttpStatusCode.OK) throw Fail("github link request", r);
        var v = await SendAsync(HttpMethod.Post, "/api/v1/me/practitioner/github-link/verify", u.AccessToken, false, null, ct);
        if (v.Status != HttpStatusCode.NoContent) throw Fail("github link verify (needs a GitHub lookup that answers in the fake profile)", v);
    }

    public async Task<IReadOnlyList<MyOffering>> OfferingsAsync(DemoUser u, CancellationToken ct)
    {
        var r = await SendAsync(HttpMethod.Get, "/api/v1/me/practitioner/offerings", u.AccessToken, false, null, ct);
        if (r.Status != HttpStatusCode.OK || r.Json is not { } j) throw Fail("list offerings", r);
        return j.GetProperty("items").EnumerateArray().Select(i => new MyOffering(i.GetProperty("id").GetGuid(), i.GetProperty("title").GetString()!,
            i.GetProperty("description").GetString()!, i.GetProperty("durationMinutes").GetInt32(), i.GetProperty("price").GetProperty("amountMinor").GetInt64(),
            i.GetProperty("problemSlug").GetString()!, i.GetProperty("techSlugs").EnumerateArray().Select(t => t.GetString()!).ToArray(), i.GetProperty("isActive").GetBoolean())).ToList();
    }

    public async Task<Guid> CreateOfferingAsync(DemoUser u, object body, CancellationToken ct)
    {
        var r = await SendAsync(HttpMethod.Post, "/api/v1/me/practitioner/offerings", u.AccessToken, false, body, ct);
        return r.Status == HttpStatusCode.Created && r.Json is { } j ? j.GetProperty("id").GetGuid() : throw Fail("create offering", r);
    }

    public async Task UpdateOfferingAsync(DemoUser u, Guid id, object body, CancellationToken ct)
    {
        var r = await SendAsync(HttpMethod.Put, $"/api/v1/me/practitioner/offerings/{id}", u.AccessToken, false, body, ct);
        if (r.Status != HttpStatusCode.OK) throw Fail("update offering", r);
    }

    public async Task SubmitAsync(DemoUser u, CancellationToken ct)
    {
        var r = await SendAsync(HttpMethod.Post, "/api/v1/me/practitioner/submit", u.AccessToken, false, null, ct);
        if (r.Status != HttpStatusCode.NoContent) throw Fail("submit", r);
    }

    // ---- admin

    public async Task AdminAsync(DemoUser admin, Guid id, Trigger trigger, string? reason, CancellationToken ct)
    {
        var r = await SendAsync(HttpMethod.Post, $"/api/v1/admin/practitioners/{id}/{trigger.ToString().ToLowerInvariant()}", admin.AccessToken, false,
            reason is null ? null : new { reason }, ct);
        if (r.Status != HttpStatusCode.NoContent) throw Fail($"admin {trigger}", r);
    }

    /// <returns>how many practitioners the Directory removed (CONTRACTS 10.4).</returns>
    public async Task<int> ResetAsync(CancellationToken ct)
    {
        var r = await SendAsync(HttpMethod.Post, "/internal/v1/demo/reset", null, true, new { }, ct);
        return r is { Status: HttpStatusCode.OK, Json: { } j } ? j.GetProperty("practitioners").GetInt32() : throw Fail("demo reset", r);
    }

    // ---- internal pushes (CONTRACTS 3.6): monotonic, a version not above the stored one is ignored with 204

    public async Task PushReputationAsync(Guid id, int ratingCount, decimal ratingAvg, long version, CancellationToken ct)
    {
        var r = await SendAsync(HttpMethod.Put, $"/internal/v1/practitioners/{id}/reputation", null, true, new { ratingCount, ratingAvg, version }, ct);
        if (r.Status != HttpStatusCode.NoContent) throw Fail("reputation push", r);
    }

    public async Task PushStatsAsync(Guid id, DemoStats s, long version, CancellationToken ct)
    {
        var r = await SendAsync(HttpMethod.Put, $"/internal/v1/practitioners/{id}/booking-stats", null, true,
            new { completedCount = s.Completed, practitionerCancelCount = s.Cancellations, noShowUpheldCount = s.NoShows, openSlots14d = s.OpenSlots, version }, ct);
        if (r.Status != HttpStatusCode.NoContent) throw Fail("booking-stats push", r);
    }
}
