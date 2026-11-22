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

    /// <summary>404 means the payment provider has no connected accounts (the mock of the fake profile): nothing to set up.</summary>
    public Task<ApiResult<PaymentAccountDto>> PaymentAccountAsync(CancellationToken ct = default) => Get<PaymentAccountDto>("/api/v1/me/payments/account", ct);

    public async Task<ApiResult<PaymentOnboardingDto>> StartPaymentOnboardingAsync(CancellationToken ct = default) =>
        await WriteAsync<PaymentOnboardingDto>(HttpMethod.Post, "/api/v1/me/payments/account/onboarding", await tokens.AccessTokenAsync(ct), null, ct);
}
