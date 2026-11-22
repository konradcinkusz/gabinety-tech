using Gabinety.Web.Api;

namespace Gabinety.Web.Components.Pages.Admin;

/// <summary>One dispute as its card shows it: the report, and what the page could read about the booking (offer, price, payment) and the practitioner's name. Missing pieces are null, and the card says so.</summary>
public sealed record DisputeCard(AdminDisputeDto Dispute, AdminBookingDto? Booking, string? PractitionerName)
{
    public Guid Id => Dispute.BookingId;
    public long? PriceGrosz => Booking is { Price.AmountMinor: > 0 } b ? b.Price.AmountMinor : null;
}

public static class DisputeRules
{
    public const string Open = "open", Resolved = "resolved";
    public static readonly string[] Statuses = [Open, Resolved];
    public const int PageSize = 20;
    /// <summary>At most this many booking reads at once for one page of cards (the services limit reads per visitor, SPEC 5.1).</summary>
    public const int ReadParallelism = 5;

    public static string Status(string? raw) => AdminQuery.Choice(raw, Statuses, Open);

    /// <summary>The booking and the practitioner's name for each dispute of a page. A booking that cannot be read leaves its card without those facts: the report and the form still work, the server checks the bounds.</summary>
    public static async Task<IReadOnlyList<DisputeCard>> LoadAsync(AdminApi api, IReadOnlyList<AdminDisputeDto> disputes, CancellationToken ct = default)
    {
        using var gate = new SemaphoreSlim(ReadParallelism);
        var bookings = await Task.WhenAll(disputes.Select(async d =>
        {
            await gate.WaitAsync(ct);
            try { return await api.Booking(d.BookingId, ct); }
            finally { gate.Release(); }
        }));
        var views = bookings.Select(b => b.IsOk ? b.Value : null).ToList();
        var missingNames = views.Where(b => b is not null && string.IsNullOrWhiteSpace(b.Practitioner.DisplayName)).Select(b => b!.Practitioner.Id);
        var names = await api.PractitionerNames(missingNames, ct: ct);
        return [.. disputes.Select((d, i) => new DisputeCard(d, views[i], views[i] is { } b ? (!string.IsNullOrWhiteSpace(b.Practitioner.DisplayName) ? b.Practitioner.DisplayName : names.GetValueOrDefault(b.Practitioner.Id)) : null))];
    }
}
