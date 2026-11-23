using System.Globalization;
using Gabinety.Web.Auth;

namespace Gabinety.Web.Api;

/// <summary>
/// Booking, collaboration proposals of the signed-in user (ADR proposal 0013). Every call carries the user's bearer; the service decides who may do what and answers 404 to a stranger. The texts of
/// users (the message, the company name) are bodies only: never a URL, a header or a log line. A write is never retried by the resilience handler (SPEC 5 rule 8); the create carries an
/// <c>Idempotency-Key</c>, so the browser's own retry of the same form is safe.
/// </summary>
public sealed class ProposalsClient(HttpClient http, VisitorAddressAccessor visitor, IAccessTokenSource tokens) : ApiClient(http, visitor)
{
    public const string IdempotencyHeader = "Idempotency-Key", ClientRole = "client", PractitionerRole = "practitioner", OpenState = "open", ClosedState = "closed";

    static string Id(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    public async Task<ApiResult<ProposalViewDto>> CreateAsync(Guid idempotencyKey, CreateProposalBody body, CancellationToken ct = default) =>
        await PostAsync<ProposalViewDto>("/api/v1/proposals", body, await tokens.AccessTokenAsync(ct), ct, new Dictionary<string, string> { [IdempotencyHeader] = Id(idempotencyKey) });

    public async Task<ApiResult<ProposalViewDto>> GetAsync(Guid id, CancellationToken ct = default) =>
        await GetAsync<ProposalViewDto>($"/api/v1/proposals/{Id(id)}", await tokens.AccessTokenAsync(ct), ct);

    /// <param name="role"><c>client</c> or <c>practitioner</c>.</param>
    /// <param name="state"><c>open</c>, <c>closed</c> or empty for every state.</param>
    public async Task<ApiResult<PageDto<ProposalSummaryDto>>> ListAsync(string role, string? state, string? cursor, CancellationToken ct = default)
    {
        var parts = new List<string> { $"role={(role == PractitionerRole ? PractitionerRole : ClientRole)}" };
        if (state is OpenState or ClosedState) parts.Add($"state={state}");
        if (!string.IsNullOrEmpty(cursor)) parts.Add($"cursor={Uri.EscapeDataString(cursor)}");
        return await GetAsync<PageDto<ProposalSummaryDto>>("/api/v1/me/proposals?" + string.Join('&', parts), await tokens.AccessTokenAsync(ct), ct);
    }

    public async Task<ApiResult<ProposalViewDto>> AcceptAsync(Guid id, int round, CancellationToken ct = default) =>
        await PostAsync<ProposalViewDto>($"/api/v1/proposals/{Id(id)}/accept", new ProposalRoundBody(round), await tokens.AccessTokenAsync(ct), ct);

    public async Task<ApiResult<ProposalViewDto>> RejectAsync(Guid id, int round, CancellationToken ct = default) =>
        await PostAsync<ProposalViewDto>($"/api/v1/proposals/{Id(id)}/reject", new ProposalRoundBody(round), await tokens.AccessTokenAsync(ct), ct);

    public async Task<ApiResult<ProposalViewDto>> CounterAsync(Guid id, CounterProposalBody body, CancellationToken ct = default) =>
        await PostAsync<ProposalViewDto>($"/api/v1/proposals/{Id(id)}/counter", body, await tokens.AccessTokenAsync(ct), ct);

    public async Task<ApiResult<ProposalViewDto>> WithdrawAsync(Guid id, CancellationToken ct = default) =>
        await PostAsync<ProposalViewDto>($"/api/v1/proposals/{Id(id)}/withdraw", new { }, await tokens.AccessTokenAsync(ct), ct);
}
