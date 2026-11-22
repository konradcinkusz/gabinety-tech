using Gabinety.Web.Auth;

namespace Gabinety.Web.Api;

/// <summary>
/// What the admin pages call: the three typed clients with the administrator's bearer, read once per call from the caller's scope (<see cref="IAccessTokenSource"/>: the request's scope in a static
/// page). Nobody signed in is a 401 without a call to a backend. It adds no authorization of its own: the page is behind the Admin policy and every service checks the role again (403).
/// </summary>
public sealed class AdminApi(DirectoryClient directory, BookingClient booking, ReputationClient reputation, IAccessTokenSource tokens)
{
    static readonly ApiFailure Unauthenticated = new(401, "unauthorized");

    async Task<ApiResult<T>> With<T>(Func<string, Task<ApiResult<T>>> call, CancellationToken ct) =>
        await tokens.AccessTokenAsync(ct) is { Length: > 0 } token ? await call(token) : ApiResult<T>.Fail(Unauthenticated);

    // ---- verification (Directory)
    public Task<ApiResult<PageDto<AdminPractitionerRowDto>>> Practitioners(string status, string? cursor, int limit, CancellationToken ct = default) =>
        With(t => directory.AdminPractitionersAsync(t, status, cursor, limit, ct), ct);
    public Task<ApiResult<AdminPractitionerDetailDto>> Practitioner(Guid id, CancellationToken ct = default) => With(t => directory.AdminPractitionerAsync(t, id, ct), ct);
    public Task<ApiResult<AdminVerificationEventsDto>> VerificationEvents(Guid id, CancellationToken ct = default) => With(t => directory.AdminVerificationEventsAsync(t, id, ct), ct);
    public Task<ApiResult<Unit>> PractitionerAction(Guid id, string action, string? reason, CancellationToken ct = default) =>
        With(t => directory.AdminPractitionerActionAsync(t, id, action, reason, ct), ct);

    // ---- disputes and the ledger (Booking)
    public Task<ApiResult<PageDto<AdminDisputeDto>>> Disputes(string status, string? cursor, int limit, CancellationToken ct = default) =>
        With(t => booking.AdminDisputesAsync(t, status, cursor, limit, ct), ct);
    public Task<ApiResult<AdminBookingDto>> Booking(Guid id, CancellationToken ct = default) => With(t => booking.AdminBookingAsync(t, id, ct), ct);
    public Task<ApiResult<Unit>> Resolve(Guid bookingId, string resolution, long? refundMinor, string note, CancellationToken ct = default) =>
        With(t => booking.AdminResolveAsync(t, bookingId, resolution, refundMinor, note, ct), ct);
    public Task<ApiResult<LedgerPreviewDto>> LedgerPreview(string from, string to, int max, CancellationToken ct = default) =>
        With(t => booking.AdminLedgerPreviewAsync(t, from, to, max, ct), ct);

    /// <summary>The ledger export, still open: the caller (the download endpoint) passes the body on and disposes it. A failure carries the status and code only.</summary>
    public async Task<(ApiStream? Stream, ApiFailure? Failure)> LedgerCsv(string from, string to, CancellationToken ct = default) =>
        await tokens.AccessTokenAsync(ct) is { Length: > 0 } token ? await booking.AdminLedgerCsvAsync(token, from, to, ct) : (null, Unauthenticated);

    // ---- reviews and suspicious pairs (Reputation)
    public Task<ApiResult<PageDto<AdminReviewDto>>> Reviews(string status, string? cursor, int limit, CancellationToken ct = default) =>
        With(t => reputation.AdminReviewsAsync(t, status, cursor, limit, ct), ct);
    public Task<ApiResult<Unit>> PublishReview(Guid id, CancellationToken ct = default) => With(t => reputation.AdminPublishReviewAsync(t, id, ct), ct);
    public Task<ApiResult<Unit>> RejectReview(Guid id, string reason, CancellationToken ct = default) => With(t => reputation.AdminRejectReviewAsync(t, id, reason, ct), ct);
    public Task<ApiResult<PageDto<SuspiciousPairDto>>> SuspiciousPairs(string? cursor, int limit, CancellationToken ct = default) =>
        With(t => reputation.AdminSuspiciousPairsAsync(t, cursor, limit, ct), ct);

    /// <summary>Display names of practitioners by id (one Directory read each, in parallel, at most <paramref name="max"/>): a name a page cannot get is simply absent and the page shows a short reference.</summary>
    public async Task<IReadOnlyDictionary<Guid, string>> PractitionerNames(IEnumerable<Guid> ids, int max = 20, CancellationToken ct = default)
    {
        var distinct = ids.Distinct().Take(max).ToList();
        var found = await Task.WhenAll(distinct.Select(async id => (id, result: await Practitioner(id, ct))));
        return found.Where(f => f.result.IsOk && !string.IsNullOrWhiteSpace(f.result.Value!.DisplayName)).ToDictionary(f => f.id, f => f.result.Value!.DisplayName);
    }
}
