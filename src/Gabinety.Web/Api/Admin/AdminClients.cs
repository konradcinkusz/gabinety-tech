using System.Globalization;
using System.Text;

namespace Gabinety.Web.Api;

// The admin calls of the three backends. Partial classes of the existing typed clients (one named client and ONE handler chain per backend, so the resilience handler count stays one); every method
// takes the administrator's bearer explicitly, from AdminApi, which reads it from the caller's scope. Every call is made with the user's own token: the services are the boundary (403 for a non-admin).

public sealed partial class DirectoryClient
{
    public Task<ApiResult<PageDto<AdminPractitionerRowDto>>> AdminPractitionersAsync(string? token, string status, string? cursor, int limit, CancellationToken ct = default) =>
        GetAsync<PageDto<AdminPractitionerRowDto>>("/api/v1/admin/practitioners" + Query(("status", status), ("limit", limit.ToString(CultureInfo.InvariantCulture)), ("cursor", cursor)), token, ct);

    public Task<ApiResult<AdminPractitionerDetailDto>> AdminPractitionerAsync(string? token, Guid id, CancellationToken ct = default) =>
        GetAsync<AdminPractitionerDetailDto>($"/api/v1/admin/practitioners/{id}", token, ct);

    public Task<ApiResult<AdminVerificationEventsDto>> AdminVerificationEventsAsync(string? token, Guid id, CancellationToken ct = default) =>
        GetAsync<AdminVerificationEventsDto>($"/api/v1/admin/practitioners/{id}/verification-events", token, ct);

    /// <summary><c>approve</c> and <c>reinstate</c> carry no body; <c>reject</c> and <c>suspend</c> carry the reason (10 to 500 characters, trimmed here: CONTRACTS 3.4).</summary>
    public Task<ApiResult<Unit>> AdminPractitionerActionAsync(string? token, Guid id, string action, string? reason, CancellationToken ct = default) =>
        PostAsync<Unit>($"/api/v1/admin/practitioners/{id}/{action}", reason is null ? null : new { reason = reason.Trim() }, token, ct);

    internal static string Query(params (string Name, string? Value)[] parts) =>
        parts.Where(p => !string.IsNullOrEmpty(p.Value)).ToList() is { Count: > 0 } kept ? "?" + string.Join('&', kept.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value!)}")) : "";
}

public sealed partial class BookingClient
{
    public Task<ApiResult<PageDto<AdminDisputeDto>>> AdminDisputesAsync(string? token, string status, string? cursor, int limit, CancellationToken ct = default) =>
        GetAsync<PageDto<AdminDisputeDto>>("/api/v1/admin/disputes" + DirectoryClient.Query(("status", status), ("limit", limit.ToString(CultureInfo.InvariantCulture)), ("cursor", cursor)), token, ct);

    /// <summary>An administrator may read any booking (CONTRACTS 4.2): the dispute card shows what was booked, the price and the payment.</summary>
    public Task<ApiResult<AdminBookingDto>> AdminBookingAsync(string? token, Guid id, CancellationToken ct = default) =>
        GetAsync<AdminBookingDto>($"/api/v1/bookings/{id}", token, ct);

    /// <summary><c>resolution</c> is <c>release | refund_full | refund_partial</c>; <c>refundMinor</c> only for a partial refund (grosz, strictly between 0 and the price).</summary>
    public Task<ApiResult<Unit>> AdminResolveAsync(string? token, Guid bookingId, string resolution, long? refundMinor, string note, CancellationToken ct = default) =>
        PostAsync<Unit>($"/api/v1/admin/bookings/{bookingId}/resolve", new { resolution, refundMinor, note = note.Trim() }, token, ct);

    /// <summary>The ledger export as a stream (<c>GET /api/v1/admin/ledger.csv</c>): days are UTC days, both ends inclusive, at most 366 days. The caller disposes the stream.</summary>
    public Task<(ApiStream? Stream, ApiFailure? Failure)> AdminLedgerCsvAsync(string? token, string from, string to, CancellationToken ct = default) =>
        OpenStreamAsync("/api/v1/admin/ledger.csv" + DirectoryClient.Query(("from", from), ("to", to)), "text/csv", token, ct);

    /// <summary>
    /// The first <paramref name="max"/> rows of the export, for the table on the ledger page (the whole period is the download). Reads the stream only as far as needed and then abandons it, so a year of
    /// rows is never read for a preview. A line that is not a row of the contract's shape is skipped, not guessed at.
    /// </summary>
    public async Task<ApiResult<LedgerPreviewDto>> AdminLedgerPreviewAsync(string? token, string from, string to, int max, CancellationToken ct = default)
    {
        var (stream, failure) = await AdminLedgerCsvAsync(token, from, to, ct);
        if (stream is null) return ApiResult<LedgerPreviewDto>.Fail(failure!);
        using (stream)
        {
            try
            {
                using var reader = new StreamReader(stream.Body, new UTF8Encoding(false));
                var rows = new List<LedgerRowDto>();
                var hasMore = false;
                _ = await reader.ReadLineAsync(ct); // the header
                while (await reader.ReadLineAsync(ct) is { } line)
                {
                    if (LedgerCsv.TryParseRow(line) is not { } row) continue;
                    if (rows.Count == max) { hasMore = true; break; }
                    rows.Add(row);
                }
                return ApiResult<LedgerPreviewDto>.Ok(new LedgerPreviewDto(rows, hasMore));
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return ApiResult<LedgerPreviewDto>.Fail(ApiFailure.NoAnswer);
            }
        }
    }
}

public sealed partial class ReputationClient
{
    public Task<ApiResult<PageDto<AdminReviewDto>>> AdminReviewsAsync(string? token, string status, string? cursor, int limit, CancellationToken ct = default) =>
        GetAsync<PageDto<AdminReviewDto>>("/api/v1/admin/reviews" + DirectoryClient.Query(("status", status), ("limit", limit.ToString(CultureInfo.InvariantCulture)), ("cursor", cursor)), token, ct);

    public Task<ApiResult<Unit>> AdminPublishReviewAsync(string? token, Guid id, CancellationToken ct = default) => PostAsync<Unit>($"/api/v1/admin/reviews/{id}/publish", null, token, ct);

    /// <summary>The reason is 10 to 500 characters after trimming (ADR-0012 decision 2).</summary>
    public Task<ApiResult<Unit>> AdminRejectReviewAsync(string? token, Guid id, string reason, CancellationToken ct = default) =>
        PostAsync<Unit>($"/api/v1/admin/reviews/{id}/reject", new { reason = reason.Trim() }, token, ct);

    public Task<ApiResult<PageDto<SuspiciousPairDto>>> AdminSuspiciousPairsAsync(string? token, string? cursor, int limit, CancellationToken ct = default) =>
        GetAsync<PageDto<SuspiciousPairDto>>("/api/v1/admin/suspicious-pairs" + DirectoryClient.Query(("limit", limit.ToString(CultureInfo.InvariantCulture)), ("cursor", cursor)), token, ct);
}

/// <summary>Reads one data line of the ledger CSV (CONTRACTS 4.3): five columns, none quoted (a GUID, a kind, an integer, a currency, a UTC instant).</summary>
public static class LedgerCsv
{
    public const string Header = "booking_id,kind,amount_minor,currency,created_at";

    public static LedgerRowDto? TryParseRow(string line)
    {
        var c = line.Split(',');
        return c.Length == 5 && Guid.TryParse(c[0], out var booking) && c[1].Length is > 0 and <= 40 && long.TryParse(c[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount)
            && c[3].Length is > 0 and <= 8 && DateTimeOffset.TryParse(c[4], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
            ? new LedgerRowDto(booking, c[1], amount, c[3], at) : null;
    }
}
