using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Gabinety.DemoSeeder;

/// <summary>A booking as the seeder reads it through <c>GET /api/v1/bookings/{id}</c> or the client's own list.</summary>
public sealed record BookingInfo(Guid Id, string Status, DateTimeOffset Start, DateTimeOffset End, string? BriefText);

/// <summary>Why a create did not produce a booking: the slot is gone (try the next one) or something else (a failure).</summary>
public sealed record CreateOutcome(BookingInfo? Created, bool SlotTaken);

/// <summary>
/// The Booking API as the seeder uses it: user calls with a bearer token, the admin resolve, and the internal clock and reset with X-Internal-Key.
/// Errors name the status and the error code, never a body (a body can carry a brief or a note).
/// </summary>
public sealed class BookingApi(HttpClient http, SeedOptions options, TimeProvider time)
{
    static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    const int MaxAttempts = SeedRetry.MaxAttempts;

    sealed record Reply(HttpStatusCode Status, JsonElement? Json);

    async Task<Reply> SendAsync(HttpMethod method, string path, string? bearer, bool internalKey, object? body, CancellationToken ct, Guid? idempotencyKey = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(method, path.TrimStart('/'));
            if (bearer is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            if (internalKey) req.Headers.Add("X-Internal-Key", options.BookingKey);
            if (idempotencyKey is { } k) req.Headers.Add("Idempotency-Key", k.ToString());
            if (body is not null) req.Content = JsonContent.Create(body, options: J);
            using var res = await http.SendAsync(req, ct);
            if ((res.StatusCode == HttpStatusCode.TooManyRequests || await IsDependencyUnavailableAsync(res, ct)) && attempt < MaxAttempts) // the 60 per minute limit (CONTRACTS 10.2): wait for the window, then retry
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

    /// <summary>503 dependency_unavailable: Booking could not reach the Directory, which shares its per-address rate limit with the seeder. Waiting for the window is the cure.</summary>
    static async Task<bool> IsDependencyUnavailableAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.StatusCode != HttpStatusCode.ServiceUnavailable) return false;
        try { return (await res.Content.ReadFromJsonAsync<JsonElement>(J, ct)) is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty("code", out var c) && c.GetString() == "dependency_unavailable"; }
        catch (JsonException) { return false; }
    }

    static string Code(Reply r) => r.Json is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()! : "-";
    static SeedException Fail(string what, Reply r) => new($"booking: {what} answered {(int)r.Status} {Code(r)}");
    static JsonElement Need(Reply r, string what, params HttpStatusCode[] ok) =>
        ok.Contains(r.Status) && r.Json is { } j ? j : throw Fail(what, r);

    static BookingInfo ToInfo(JsonElement v) => new(v.GetProperty("id").GetGuid(), v.GetProperty("status").GetString()!, v.GetProperty("startUtc").GetDateTimeOffset(),
        v.GetProperty("endUtc").GetDateTimeOffset(), v.TryGetProperty("brief", out var b) && b.ValueKind == JsonValueKind.Object ? b.GetProperty("text").GetString() : null);

    // ---- the test clock (internal): POST sets an offset and answers the effective now; GET (T-08.02, fake profile) reads the same pair.

    public async Task<(long Offset, DateTimeOffset Now)> GetClockAsync(CancellationToken ct)
    {
        var j = Need(await SendAsync(HttpMethod.Get, "/internal/v1/test/clock", null, true, null, ct), "get clock", HttpStatusCode.OK);
        return (j.GetProperty("offsetMinutes").GetInt64(), j.GetProperty("now").GetDateTimeOffset());
    }

    public async Task<(long Offset, DateTimeOffset Now)> SetClockAsync(long offsetMinutes, CancellationToken ct)
    {
        var r = await SendAsync(HttpMethod.Post, "/internal/v1/test/clock", null, true, new { offsetMinutes }, ct);
        var j = Need(r, "set clock", HttpStatusCode.OK);
        return (j.GetProperty("offsetMinutes").GetInt64(), j.GetProperty("now").GetDateTimeOffset());
    }

    /// <returns>how many bookings the Booking service removed (CONTRACTS 10.4).</returns>
    public async Task<int> ResetAsync(CancellationToken ct)
    {
        var r = await SendAsync(HttpMethod.Post, "/internal/v1/demo/reset", null, true, new { }, ct);
        return Need(r, "demo reset", HttpStatusCode.OK).GetProperty("bookings").GetInt32();
    }

    // ---- availability and slots

    public async Task<List<(int Day, string Start, string End)>> GetAvailabilityAsync(DemoUser user, CancellationToken ct)
    {
        var j = Need(await SendAsync(HttpMethod.Get, "/api/v1/me/availability", user.AccessToken, false, null, ct), "get availability", HttpStatusCode.OK);
        return j.GetProperty("rules").EnumerateArray().Select(r => (r.GetProperty("isoWeekday").GetInt32(), r.GetProperty("startLocal").GetString()!, r.GetProperty("endLocal").GetString()!)).ToList();
    }

    public async Task PutAvailabilityAsync(DemoUser user, IEnumerable<(int Day, string Start, string End)> rules, CancellationToken ct)
    {
        var body = new { rules = rules.Select(r => new { isoWeekday = r.Day, startLocal = r.Start, endLocal = r.End }), exceptions = Array.Empty<object>() };
        Need(await SendAsync(HttpMethod.Put, "/api/v1/me/availability", user.AccessToken, false, body, ct), "put availability", HttpStatusCode.OK);
    }

    public async Task<List<DateTimeOffset>> SlotsAsync(Guid practitionerId, Guid offeringId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var path = $"/api/v1/practitioners/{practitionerId}/slots?offeringId={offeringId}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        var j = Need(await SendAsync(HttpMethod.Get, path, null, false, null, ct), "slots", HttpStatusCode.OK);
        return j.GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("startUtc").GetDateTimeOffset()).OrderBy(s => s).ToList();
    }

    // ---- bookings

    public async Task<CreateOutcome> CreateAsync(DemoUser client, Guid idempotencyKey, Guid offeringId, DateTimeOffset start, string briefText, CancellationToken ct)
    {
        var body = new { offeringId, startUtc = start.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), brief = new { text = briefText, problemSlug = (string?)null, aiAssisted = false } };
        var r = await SendAsync(HttpMethod.Post, "/api/v1/bookings", client.AccessToken, false, body, ct, idempotencyKey);
        if (r.Status == HttpStatusCode.Created && r.Json is { } j) return new CreateOutcome(new BookingInfo(j.GetProperty("bookingId").GetGuid(), j.GetProperty("status").GetString()!, start, default, briefText), false);
        if (r.Status == HttpStatusCode.Conflict && Code(r) == "slot_taken") return new CreateOutcome(null, true);
        throw Fail("create booking", r);
    }

    public async Task<BookingInfo> GetAsync(DemoUser user, Guid id, CancellationToken ct) =>
        ToInfo(Need(await SendAsync(HttpMethod.Get, $"/api/v1/bookings/{id}", user.AccessToken, false, null, ct), "get booking", HttpStatusCode.OK));

    /// <summary>All of the client's own bookings (any status), through the paged list.</summary>
    public async Task<List<BookingInfo>> MyBookingsAsync(DemoUser client, CancellationToken ct)
    {
        var all = new List<BookingInfo>();
        string? cursor = null;
        do
        {
            var path = "/api/v1/me/bookings?role=client&limit=24" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            var j = Need(await SendAsync(HttpMethod.Get, path, client.AccessToken, false, null, ct), "list bookings", HttpStatusCode.OK);
            all.AddRange(j.GetProperty("items").EnumerateArray().Select(ToInfo));
            cursor = j.TryGetProperty("nextCursor", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        } while (cursor is not null);
        return all;
    }

    public async Task SimulatePaymentAsync(DemoUser client, Guid id, string outcome, CancellationToken ct) =>
        Need(await SendAsync(HttpMethod.Post, "/api/v1/payments/simulate", client.AccessToken, false, new { bookingId = id, outcome }, ct), $"simulate payment {outcome}", HttpStatusCode.OK);

    public async Task CancelAsync(DemoUser client, Guid id, string reason, CancellationToken ct) =>
        Need(await SendAsync(HttpMethod.Post, $"/api/v1/bookings/{id}/cancel", client.AccessToken, false, new { reason }, ct), "cancel", HttpStatusCode.OK);

    public async Task DisputeAsync(DemoUser client, Guid id, string kind, string text, CancellationToken ct) =>
        Need(await SendAsync(HttpMethod.Post, $"/api/v1/bookings/{id}/dispute", client.AccessToken, false, new { kind, text }, ct), "dispute", HttpStatusCode.OK);

    public async Task ReportClientNoShowAsync(DemoUser practitioner, Guid id, CancellationToken ct) =>
        Need(await SendAsync(HttpMethod.Post, $"/api/v1/bookings/{id}/report-client-no-show", practitioner.AccessToken, false, null, ct), "report client no-show", HttpStatusCode.OK);

    public async Task ResolveAsync(DemoUser admin, Guid id, string resolution, string note, CancellationToken ct) =>
        Need(await SendAsync(HttpMethod.Post, $"/api/v1/admin/bookings/{id}/resolve", admin.AccessToken, false, new { resolution, note }, ct), "admin resolve", HttpStatusCode.OK);
}
