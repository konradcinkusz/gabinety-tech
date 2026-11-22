using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Gabinety.Billing;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Features.Bookings.View;

/// <summary>
/// GET /api/v1/me/bookings?role=client|practitioner&amp;status=&amp;limit=&amp;cursor= (CONTRACTS 4.2, 0). The caller's own rows for one role, newest
/// <c>start_utc</c> first, ties by id descending, so the order is total. Each item is the single view of that booking for the caller
/// (<see cref="BookingViewFeature.ToView"/>): the same visibility of brief, meeting URL and fee. Two queries per page whatever its size (rows, then their payments).
/// <para>The cursor is opaque: the position of the last row plus the role and status it was made for, and a short checksum. It is not a secret and not
/// a signature: a forged one can only move the position within the caller's own rows; the checksum and the role/status binding catch a cursor that was
/// edited by accident or reused for another listing.</para>
/// </summary>
public sealed class MyBookingsFeature : IServiceFeature
{
    public const int DefaultLimit = 12, MaxLimit = 24;

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/me/bookings", ListAsync).RequireAuthorization("Authenticated");

    static async Task<IResult> ListAsync(string? role, string? status, string? cursor, string? limit, ClaimsPrincipal user, BookingDbContext db,
        PartyNamesLookup names, TimeProvider time, CancellationToken ct)
    {
        var sub = BookingViewFeature.CallerId(user);
        if (sub is null) return Results.Unauthorized();

        var errors = new Dictionary<string, string[]>();
        if (role is not ("client" or "practitioner")) errors["role"] = ["The role is required: client or practitioner."];
        BookingStatus? statusFilter = null;
        if (!string.IsNullOrEmpty(status))
        {
            if (Enum.TryParse<BookingStatus>(status, ignoreCase: false, out var st) && Enum.IsDefined(st) && !char.IsDigit(status[0])) statusFilter = st;
            else errors["status"] = ["The status is not a booking status."];
        }
        var take = DefaultLimit;
        if (limit is not null && !(int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out take) && take is >= 1 and <= MaxLimit))
            errors["limit"] = [$"The limit is 1 to {MaxLimit}."];
        Position? after = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            after = Decode(cursor, role, statusFilter);
            if (after is null) errors["cursor"] = ["The cursor is not valid for this listing."];
        }
        if (errors.Count > 0) return PaymentWebhookFeature.Problem(ErrorCodes.ValidationFailed, "Validation failed", 400, null, errors);

        var asClient = role == "client";
        var q = db.Bookings.AsNoTracking().Where(b => asClient ? b.ClientUserId == sub : b.PractitionerUserId == sub);
        if (statusFilter is { } s) q = q.Where(b => b.Status == s);
        if (after is { } p) q = q.Where(b => b.StartUtc < p.Start || (b.StartUtc == p.Start && b.Id.CompareTo(p.Id) < 0));

        var rows = await q.OrderByDescending(b => b.StartUtc).ThenByDescending(b => b.Id).Take(take + 1).ToListAsync(ct);
        var more = rows.Count > take;
        if (more) rows.RemoveAt(take);

        var ids = rows.Select(b => b.Id).ToList();
        var payments = (await db.Payments.AsNoTracking().Where(pm => ids.Contains(pm.BookingId)).ToListAsync(ct)).ToDictionary(pm => pm.BookingId);
        var admin = BookingViewFeature.IsAdmin(user);
        var nameOf = await names.ForAsync(rows, ct);
        var items = rows.Select(b => BookingViewFeature.ToView(b, payments.GetValueOrDefault(b.Id), sub, admin, time, nameOf[b.Id])).ToList();

        return Results.Ok(more && rows.Count > 0
            ? new MyBookingsPage(items, Encode(new Position(rows[^1].StartUtc, rows[^1].Id), role!, statusFilter))
            : new MyBookingsPage(items, null));
    }

    readonly record struct Position(DateTimeOffset Start, Guid Id);

    static string Payload(Position p, string role, BookingStatus? status) =>
        string.Join('|', role, status?.ToString() ?? "-", p.Start.UtcTicks.ToString(CultureInfo.InvariantCulture), p.Id.ToString("N"));

    static string Check(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)), 0, 6);

    static string Encode(Position p, string role, BookingStatus? status)
    {
        var payload = Payload(p, role, status);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + "." + Check(payload);
    }

    /// <summary>The position, or null for anything that is not a cursor made by <see cref="Encode"/> for this role and status.</summary>
    static Position? Decode(string cursor, string? role, BookingStatus? status)
    {
        try
        {
            var dot = cursor.IndexOf('.');
            if (dot <= 0 || role is null) return null;
            var b64 = cursor[..dot].Replace('-', '+').Replace('_', '/');
            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=')));
            if (!string.Equals(Check(payload), cursor[(dot + 1)..], StringComparison.Ordinal)) return null;
            var parts = payload.Split('|');
            if (parts.Length != 4 || parts[0] != role || parts[1] != (status?.ToString() ?? "-")) return null;
            if (!long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || ticks > DateTimeOffset.MaxValue.Ticks) return null;
            if (!Guid.TryParseExact(parts[3], "N", out var id)) return null;
            return new Position(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }
        catch (FormatException) { return null; }
        catch (ArgumentException) { return null; }
    }
}

/// <summary><c>nextCursor</c> is omitted from the JSON on the last page (CONTRACTS 0).</summary>
public sealed record MyBookingsPage(IReadOnlyList<BookingView> Items,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? NextCursor);
