using System.Globalization;
using Gabinety.Web.Auth;

namespace Gabinety.Web.Api;

/// <summary>Directory, the practitioner's own profile and offers (CONTRACTS 3.3, 3.5). Every call carries the user's bearer; the service decides who may do what.</summary>
public sealed class PractitionerClient(HttpClient http, VisitorAddressAccessor visitor, IAccessTokenSource tokens) : ApiClient(http, visitor)
{
    const string Me = "/api/v1/me/practitioner";

    async Task<ApiResult<T>> Get<T>(string path, CancellationToken ct) => await GetAsync<T>(path, await tokens.AccessTokenAsync(ct), ct);

    async Task<ApiResult<T>> Write<T>(HttpMethod method, string path, object? body, CancellationToken ct) => await WriteAsync<T>(method, path, await tokens.AccessTokenAsync(ct), body, ct);

    /// <summary>404 means the account has no practitioner profile yet.</summary>
    public Task<ApiResult<PractitionerPrivateDto>> ProfileAsync(CancellationToken ct = default) => Get<PractitionerPrivateDto>(Me, ct);

    public Task<ApiResult<PractitionerPrivateDto>> CreateProfileAsync(string displayName, CancellationToken ct = default) =>
        Write<PractitionerPrivateDto>(HttpMethod.Post, Me, new CreatePractitionerBody(displayName), ct);

    public Task<ApiResult<PractitionerPrivateDto>> SaveProfileAsync(UpdatePractitionerBody body, CancellationToken ct = default) => Write<PractitionerPrivateDto>(HttpMethod.Put, Me, body, ct);

    /// <summary>The contract availability (ADR proposal 0013): a separate resource, because the profile PUT replaces every field it knows.</summary>
    public Task<ApiResult<ContractAvailabilitySettingsDto>> ContractAvailabilityAsync(CancellationToken ct = default) => Get<ContractAvailabilitySettingsDto>(Me + "/contract-availability", ct);

    public Task<ApiResult<ContractAvailabilitySettingsDto>> SaveContractAvailabilityAsync(ContractAvailabilitySettingsDto body, CancellationToken ct = default) =>
        Write<ContractAvailabilitySettingsDto>(HttpMethod.Put, Me + "/contract-availability", body, ct);

    public Task<ApiResult<Unit>> SubmitAsync(CancellationToken ct = default) => Write<Unit>(HttpMethod.Post, Me + "/submit", null, ct);

    public Task<ApiResult<GithubChallengeDto>> RequestGithubLinkAsync(string githubLogin, CancellationToken ct = default) =>
        Write<GithubChallengeDto>(HttpMethod.Post, Me + "/github-link", new GithubLinkBody(githubLogin), ct);

    public Task<ApiResult<Unit>> VerifyGithubLinkAsync(CancellationToken ct = default) => Write<Unit>(HttpMethod.Post, Me + "/github-link/verify", null, ct);

    public async Task<ApiResult<IReadOnlyList<OfferingPrivateDto>>> OfferingsAsync(CancellationToken ct = default)
    {
        var r = await Get<OfferingsDto>(Me + "/offerings", ct);
        return r.IsOk ? ApiResult<IReadOnlyList<OfferingPrivateDto>>.Ok(r.Value!.Items) : ApiResult<IReadOnlyList<OfferingPrivateDto>>.Fail(r.Failure!);
    }

    /// <summary>Creates the offer (<paramref name="id"/> null) or replaces it.</summary>
    public Task<ApiResult<OfferingPrivateDto>> SaveOfferingAsync(Guid? id, OfferingBody body, CancellationToken ct = default) =>
        Write<OfferingPrivateDto>(id is null ? HttpMethod.Post : HttpMethod.Put, Me + "/offerings" + (id is null ? "" : "/" + id), body, ct);

    public Task<ApiResult<Unit>> DeactivateOfferingAsync(Guid id, CancellationToken ct = default) => Write<Unit>(HttpMethod.Post, $"{Me}/offerings/{id}/deactivate", null, ct);

    public Task<ApiResult<Unit>> ActivateOfferingAsync(Guid id, CancellationToken ct = default) => Write<Unit>(HttpMethod.Post, $"{Me}/offerings/{id}/activate", null, ct);
}

/// <summary>Booking, the practitioner's weekly availability (CONTRACTS 4.2) and payments account (PAYMENTS-GO-LIVE).</summary>
public sealed class PractitionerSchedulingClient(HttpClient http, VisitorAddressAccessor visitor, IAccessTokenSource tokens) : ApiClient(http, visitor)
{
    async Task<ApiResult<T>> Get<T>(string path, CancellationToken ct) => await GetAsync<T>(path, await tokens.AccessTokenAsync(ct), ct);

    /// <summary>403 means the account is not a practitioner (yet).</summary>
    public Task<ApiResult<AvailabilityDto>> AvailabilityAsync(CancellationToken ct = default) => Get<AvailabilityDto>("/api/v1/me/availability", ct);

    public async Task<ApiResult<AvailabilityDto>> SaveAvailabilityAsync(AvailabilityDto body, CancellationToken ct = default) =>
        await WriteAsync<AvailabilityDto>(HttpMethod.Put, "/api/v1/me/availability", await tokens.AccessTokenAsync(ct), body, ct);

    /// <summary>The practitioner's earnings (CONTRACTS 4.2). Both dates are Warsaw days, inclusive; without them the service answers the current Warsaw month. 403 means the account is not a practitioner.</summary>
    public Task<ApiResult<EarningsDto>> EarningsAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (from is { } f) parts.Add("from=" + f.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (to is { } t) parts.Add("to=" + t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return Get<EarningsDto>("/api/v1/me/earnings" + (parts.Count == 0 ? "" : "?" + string.Join('&', parts)), ct);
    }

    /// <summary>
    /// The practitioner's bookings that start on Warsaw days from <paramref name="from"/> to <paramref name="to"/> (both inclusive, CONTRACTS 4.2 wave 3), every page of the cursor.
    /// A page that never ends is a failure, not a loop: the cap is far above a week of one practitioner's bookings.
    /// </summary>
    public async Task<ApiResult<IReadOnlyList<BookingViewDto>>> CalendarBookingsAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var items = new List<BookingViewDto>();
        string? cursor = null;
        for (var page = 0; page < MaxBookingPages; page++)
        {
            var path = $"/api/v1/me/bookings?role=practitioner&from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
                + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            var r = await Get<PageDto<BookingViewDto>>(path, ct);
            if (!r.IsOk) return ApiResult<IReadOnlyList<BookingViewDto>>.Fail(r.Failure!);
            items.AddRange(r.Value!.Items);
            if (r.Value.NextCursor is null) return ApiResult<IReadOnlyList<BookingViewDto>>.Ok(items);
            cursor = r.Value.NextCursor;
        }
        return ApiResult<IReadOnlyList<BookingViewDto>>.Fail(new ApiFailure(200, ApiFailure.InvalidResponse));
    }

    const int MaxBookingPages = 20;

    /// <summary>404 means the payment provider has no connected accounts (the mock of the fake profile): nothing to set up.</summary>
    public Task<ApiResult<PaymentAccountDto>> PaymentAccountAsync(CancellationToken ct = default) => Get<PaymentAccountDto>("/api/v1/me/payments/account", ct);

    public async Task<ApiResult<PaymentOnboardingDto>> StartPaymentOnboardingAsync(CancellationToken ct = default) =>
        await WriteAsync<PaymentOnboardingDto>(HttpMethod.Post, "/api/v1/me/payments/account/onboarding", await tokens.AccessTokenAsync(ct), null, ct);
}
