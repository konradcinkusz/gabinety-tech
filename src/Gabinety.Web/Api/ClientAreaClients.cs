using System.Globalization;
using Gabinety.Web.Auth;

namespace Gabinety.Web.Api;

/// <summary>The rest of the booking calls of a signed-in client or practitioner (CONTRACTS 4; the list is in <c>Clients.cs</c>). Texts of users (the brief, a reason, a dispute) are bodies only: never a URL, a header or a log line.</summary>
public sealed partial class BookingClient
{
    /// <summary>SPEC 5.1: the booking write calls the payment provider (10 s), so it gets 12 s; a retry is off for a POST, the idempotency key makes the browser's own retry safe.</summary>
    public static readonly TimeSpan CreateBudget = TimeSpan.FromSeconds(12);
    public const string IdempotencyHeader = "Idempotency-Key";

    static string Id(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    public async Task<ApiResult<BookingDetailDto>> GetAsync(Guid id, CancellationToken ct = default) =>
        await GetAsync<BookingDetailDto>($"/api/v1/bookings/{Id(id)}", await tokens.AccessTokenAsync(ct), ct);

    public async Task<ApiResult<CreateBookingResponseDto>> CreateAsync(Guid idempotencyKey, CreateBookingRequestDto request, CancellationToken ct = default) =>
        await PostAsync<CreateBookingResponseDto>("/api/v1/bookings", request, await tokens.AccessTokenAsync(ct), ct, new Dictionary<string, string> { [IdempotencyHeader] = Id(idempotencyKey) }, CreateBudget);

    public async Task<ApiResult<BookingDetailDto>> CancelAsync(Guid id, string? reason, CancellationToken ct = default) =>
        await PostAsync<BookingDetailDto>($"/api/v1/bookings/{Id(id)}/cancel", string.IsNullOrWhiteSpace(reason) ? new { } : new { reason = reason.Trim() }, await tokens.AccessTokenAsync(ct), ct);

    /// <param name="kind">The wire name: <c>not_as_described</c>, <c>practitioner_no_show</c> or <c>other</c>.</param>
    public async Task<ApiResult<BookingDetailDto>> DisputeAsync(Guid id, string kind, string text, CancellationToken ct = default) =>
        await PostAsync<BookingDetailDto>($"/api/v1/bookings/{Id(id)}/dispute", new { kind, text = text.Trim() }, await tokens.AccessTokenAsync(ct), ct);

    public async Task<ApiResult<BookingDetailDto>> ReportNoShowAsync(Guid id, CancellationToken ct = default) =>
        await PostAsync<BookingDetailDto>($"/api/v1/bookings/{Id(id)}/report-client-no-show", null, await tokens.AccessTokenAsync(ct), ct);

    /// <summary>The mock checkout's button (fake profile only: the route does not exist elsewhere, a 403 or 404 says so). <paramref name="outcome"/> is <c>succeeded</c> or <c>failed</c>.</summary>
    public async Task<ApiResult<Unit>> SimulatePaymentAsync(Guid bookingId, string outcome, CancellationToken ct = default) =>
        await PostAsync<Unit>("/api/v1/payments/simulate", new { bookingId, outcome }, await tokens.AccessTokenAsync(ct), ct);
}

/// <summary>The optional help with the brief (CONTRACTS 6). 35 s (SPEC 5.1): a language model answers; it is signed in and rate limited per user.</summary>
public sealed class BriefAssistantClient(HttpClient http, VisitorAddressAccessor visitor, IAccessTokenSource tokens) : ApiClient(http, visitor)
{
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(35);

    public async Task<ApiResult<BriefSuggestionDto>> SuggestAsync(string text, CancellationToken ct = default) =>
        await PostAsync<BriefSuggestionDto>("/api/v1/assistant/brief", new { text }, await tokens.AccessTokenAsync(ct), ct, timeout: Budget);
}

/// <summary>Reviews of a signed-in user (CONTRACTS 5.1, 5.2) and the identity facts the practitioner's list needs (<c>GET /api/v1/me</c> of the Directory is on <see cref="MeClient"/>).</summary>
public sealed class ReviewsClient(HttpClient http, VisitorAddressAccessor visitor, IAccessTokenSource tokens) : ApiClient(http, visitor)
{
    public async Task<ApiResult<CreateReviewResponseDto>> CreateAsync(Guid bookingId, int rating, string text, CancellationToken ct = default) =>
        await PostAsync<CreateReviewResponseDto>("/api/v1/reviews", new { bookingId, rating, text = text.Trim() }, await tokens.AccessTokenAsync(ct), ct);

    public async Task<ApiResult<Unit>> ReplyAsync(Guid reviewId, string text, CancellationToken ct = default) =>
        await PostAsync<Unit>($"/api/v1/reviews/{reviewId:D}/reply", new { text = text.Trim() }, await tokens.AccessTokenAsync(ct), ct);

    public Task<ApiResult<PageDto<ReviewDto>>> ListAsync(Guid practitionerId, string? cursor, CancellationToken ct = default) =>
        GetAsync<PageDto<ReviewDto>>($"/api/v1/practitioners/{practitionerId:D}/reviews?limit=20" + (string.IsNullOrEmpty(cursor) ? "" : "&cursor=" + Uri.EscapeDataString(cursor)), null, ct);
}

/// <summary><c>GET /api/v1/me</c> of the Directory: who the signed-in user is and whether they have a practitioner profile.</summary>
public sealed class MeClient(HttpClient http, VisitorAddressAccessor visitor, IAccessTokenSource tokens) : ApiClient(http, visitor)
{
    public async Task<ApiResult<MeDto>> GetAsync(CancellationToken ct = default) => await GetAsync<MeDto>("/api/v1/me", await tokens.AccessTokenAsync(ct), ct);
}
