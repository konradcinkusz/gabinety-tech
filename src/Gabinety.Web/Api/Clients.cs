using System.Globalization;
using Gabinety.Web.Auth;

namespace Gabinety.Web.Api;

/// <summary>The filters of <c>GET /api/v1/practitioners</c> (CONTRACTS 3.2). <c>Sort</c> is the wire value (<c>price_asc</c>, <c>price_desc</c>, <c>next_slot</c>); none means the Directory's rank order.</summary>
public sealed record PractitionerQuery(string? Problem = null, string? Tech = null, string? Q = null, int? Duration = null, long? MaxPriceMinor = null, string? Cursor = null, string? Sort = null, bool Contract = false);

/// <summary>Directory, public reads only (SPEC 5.1: 5 s, once, GET only: see <see cref="ApiResilience"/>).</summary>
public sealed partial class DirectoryClient(HttpClient http, VisitorAddressAccessor visitor) : ApiClient(http, visitor)
{
    public Task<ApiResult<TaxonomyDto>> TaxonomyAsync(CancellationToken ct = default) => GetAsync<TaxonomyDto>("/api/v1/taxonomy", null, ct);

    public Task<ApiResult<SitemapDto>> SitemapAsync(CancellationToken ct = default) => GetAsync<SitemapDto>("/api/v1/sitemap", null, ct);

    public Task<ApiResult<PractitionerProfileDto>> ProfileAsync(string slug, CancellationToken ct = default) =>
        GetAsync<PractitionerProfileDto>($"/api/v1/practitioners/{Uri.EscapeDataString(slug)}", null, ct);

    public Task<ApiResult<RankingRulesDto>> RankingRulesAsync(CancellationToken ct = default) => GetAsync<RankingRulesDto>("/api/v1/ranking-rules", null, ct);

    public Task<ApiResult<PageDto<PractitionerCardDto>>> PractitionersAsync(PractitionerQuery query, CancellationToken ct = default)
    {
        var parts = new List<string>();
        void Add(string name, string? value) { if (!string.IsNullOrEmpty(value)) parts.Add($"{name}={Uri.EscapeDataString(value)}"); }
        Add("problem", query.Problem);
        Add("tech", query.Tech);
        Add("q", query.Q);
        Add("duration", query.Duration?.ToString(CultureInfo.InvariantCulture));
        Add("maxPriceMinor", query.MaxPriceMinor?.ToString(CultureInfo.InvariantCulture));
        Add("cursor", query.Cursor);
        if (query.Contract) parts.Add("contract=1"); // ADR proposal 0013: only practitioners who take collaboration proposals
        Add("sort", query.Sort);
        return GetAsync<PageDto<PractitionerCardDto>>("/api/v1/practitioners" + (parts.Count == 0 ? "" : "?" + string.Join('&', parts)), null, ct);
    }
}

/// <summary>Booking, for the signed-in user: the bearer is read from the caller's scope (<see cref="IAccessTokenSource"/>) and handed to the handler through the request.</summary>
public sealed partial class BookingClient(HttpClient http, VisitorAddressAccessor visitor, IAccessTokenSource tokens) : ApiClient(http, visitor)
{
    public const string ClientRole = "client", PractitionerRole = "practitioner";

    /// <summary>Free slots of one offering between two Warsaw dates (inclusive, CONTRACTS 4.2). Public: no bearer is sent.</summary>
    public Task<ApiResult<SlotsDto>> SlotsAsync(Guid practitionerId, Guid offeringId, string fromDate, string toDate, CancellationToken ct = default) =>
        GetAsync<SlotsDto>($"/api/v1/practitioners/{practitionerId}/slots?offeringId={offeringId}&from={Uri.EscapeDataString(fromDate)}&to={Uri.EscapeDataString(toDate)}", null, ct);

    public async Task<ApiResult<PageDto<BookingViewDto>>> MyBookingsAsync(string role, string? status, string? cursor, CancellationToken ct = default)
    {
        var parts = new List<string> { $"role={(role == PractitionerRole ? PractitionerRole : ClientRole)}" };
        if (!string.IsNullOrEmpty(status)) parts.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrEmpty(cursor)) parts.Add($"cursor={Uri.EscapeDataString(cursor)}");
        return await GetAsync<PageDto<BookingViewDto>>("/api/v1/me/bookings?" + string.Join('&', parts), await tokens.AccessTokenAsync(ct), ct);
    }

    /// <summary>The signed-in user's three switches (CONTRACTS 4.2): a kind without a row is on. Failure is state, never a cause, so the account page can fail soft.</summary>
    public async Task<ApiResult<NotificationPreferencesDto>> NotificationPreferencesAsync(CancellationToken ct = default) =>
        await GetAsync<NotificationPreferencesDto>("/api/v1/me/notification-preferences", await tokens.AccessTokenAsync(ct), ct);

    /// <summary>Sets the kinds it names and answers with the full list. Only the switchable kinds are sent; the service refuses any other with <c>400 validation_failed</c>.</summary>
    public async Task<ApiResult<NotificationPreferencesDto>> SetNotificationPreferencesAsync(IReadOnlyList<NotificationPreferenceDto> items, CancellationToken ct = default) =>
        await WriteAsync<NotificationPreferencesDto>(HttpMethod.Put, "/api/v1/me/notification-preferences", await tokens.AccessTokenAsync(ct), new NotificationPreferencesDto(items), ct);
}

/// <summary>Reputation, public reads.</summary>
public sealed partial class ReputationClient(HttpClient http, VisitorAddressAccessor visitor) : ApiClient(http, visitor)
{
    public Task<ApiResult<PageDto<ReviewDto>>> ReviewsAsync(Guid practitionerId, string? cursor = null, CancellationToken ct = default) =>
        GetAsync<PageDto<ReviewDto>>($"/api/v1/practitioners/{practitionerId}/reviews" + (string.IsNullOrEmpty(cursor) ? "" : "?cursor=" + Uri.EscapeDataString(cursor)), null, ct);
}

/// <summary>Assistant. The page keys off the gateway state (<see cref="AssistantStatusDto.State"/>), not off the health verdict: a working login that shows as unavailable is the recorded failure.</summary>
public sealed class AssistantClient(HttpClient http, VisitorAddressAccessor visitor) : ApiClient(http, visitor)
{
    public Task<ApiResult<AssistantStatusDto>> StatusAsync(CancellationToken ct = default) => GetAsync<AssistantStatusDto>("/api/v1/assistant/status", null, ct);
}
