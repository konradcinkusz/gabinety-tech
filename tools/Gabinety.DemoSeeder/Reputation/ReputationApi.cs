using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Gabinety.DemoSeeder;

/// <summary>What the seeder needs to know about a review it tried to create.</summary>
public enum ReviewOutcome { Created, AlreadyReviewed, NotReviewable }

/// <summary>
/// The Reputation API as the seeder uses it: one user call (<c>POST /api/v1/reviews</c>) with the client's bearer token. Errors name the status and the error code,
/// never a body (a body can carry the review text).
/// </summary>
public sealed class ReputationApi(HttpClient http, TimeProvider time, string internalKey = "")
{
    static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    const int MaxAttempts = SeedRetry.MaxAttempts;

    /// <returns>how many reviews Reputation removed (CONTRACTS 10.4).</returns>
    public async Task<int> ResetAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "internal/v1/demo/reset") { Content = JsonContent.Create(new { }, options: J) };
        req.Headers.Add("X-Internal-Key", internalKey);
        using var res = await http.SendAsync(req, ct);
        var json = await ReadAsync(res, ct);
        return res.StatusCode == HttpStatusCode.OK && json is { ValueKind: JsonValueKind.Object } o ? o.GetProperty("reviews").GetInt32()
            : throw new SeedException($"reputation: demo reset answered {(int)res.StatusCode}");
    }

    public async Task<(ReviewOutcome Outcome, string? Status)> CreateReviewAsync(DemoUser client, Guid bookingId, int rating, string text, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/v1/reviews") { Content = JsonContent.Create(new { bookingId, rating, text }, options: J) };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", client.AccessToken);
            using var res = await http.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxAttempts) // the 60 per minute limit (CONTRACTS 10.2)
            {
                await Task.Delay(res.Headers.RetryAfter?.Delta ?? SeedRetry.DefaultWait, time, ct);
                continue;
            }
            var json = await ReadAsync(res, ct);
            var code = json is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()! : "-";
            if (res.StatusCode == HttpStatusCode.Created)
                return (ReviewOutcome.Created, json is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty("status", out var s) ? s.GetString() : null);
            if (res.StatusCode == HttpStatusCode.Conflict && code == "already_reviewed") return (ReviewOutcome.AlreadyReviewed, null);
            if (res.StatusCode == HttpStatusCode.UnprocessableEntity && code == "not_reviewable") return (ReviewOutcome.NotReviewable, null);
            throw new SeedException($"reputation: create review answered {(int)res.StatusCode} {code}");
        }
    }

    static async Task<JsonElement?> ReadAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.Content.Headers.ContentLength is not > 0 && res.Content.Headers.ContentType is null) return null;
        try { return await res.Content.ReadFromJsonAsync<JsonElement>(J, ct); } catch (JsonException) { return null; }
    }
}
