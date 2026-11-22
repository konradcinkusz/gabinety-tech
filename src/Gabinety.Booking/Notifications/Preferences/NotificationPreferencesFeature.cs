using System.Security.Claims;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Notifications.Preferences;

/// <summary>
/// GET and PUT /api/v1/me/notification-preferences (CONTRACTS 4.2): the caller's own switches for the three switchable kinds, nobody else's.
/// GET lists all three, a kind without a row as enabled. PUT sets the kinds it names (a kind it leaves out keeps its value) and answers with the full list;
/// a transactional kind, an unknown or repeated kind, or an empty list is <c>400 validation_failed</c> and nothing is written. The dispatcher reads the rows at send time.
/// </summary>
public sealed class NotificationPreferencesFeature : IServiceFeature
{
    const int Attempts = 3;

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var g = endpoints.MapGroup("/api/v1/me/notification-preferences").RequireAuthorization("Authenticated");
        g.MapGet("", GetAsync);
        g.MapPut("", PutAsync);
    }

    static async Task<IResult> GetAsync(ClaimsPrincipal user, BookingDbContext db, CancellationToken ct)
    {
        var sub = BookingViewFeature.CallerId(user);
        if (sub is null) return Results.Unauthorized();
        return Results.Ok(await ReadAsync(db, sub, ct));
    }

    static async Task<IResult> PutAsync(NotificationPreferencesDto? body, ClaimsPrincipal user, BookingDbContext db, CancellationToken ct)
    {
        var sub = BookingViewFeature.CallerId(user);
        if (sub is null) return Results.Unauthorized();

        var errors = new Dictionary<string, string[]>();
        var wanted = new Dictionary<NotificationKind, bool>();
        if (body?.Items is not { Count: > 0 } items) errors["items"] = ["items must list at least one kind"];
        else
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] is not { } item) { errors[$"items[{i}]"] = ["the item is required"]; continue; }
                if (NotificationPreferenceRules.ParseSwitchable(item.Kind) is not { } kind)
                    errors[$"items[{i}].kind"] = [$"kind must be one of {string.Join(", ", NotificationPreferenceRules.Switchable)}"];
                else if (!wanted.TryAdd(kind, item.Enabled)) errors[$"items[{i}].kind"] = [$"{kind} is listed more than once"];
            }
        if (errors.Count > 0) return PaymentWebhookFeature.Problem(ErrorCodes.ValidationFailed, "Validation failed", 400, null, errors);

        // Two first PUTs of one user can race on the primary key: the loser reads again and updates instead.
        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();
            var rows = await db.NotificationPreferences.Where(p => p.UserId == sub).ToDictionaryAsync(p => p.Kind, ct);
            foreach (var (kind, enabled) in wanted)
                if (rows.TryGetValue(kind, out var row)) row.Enabled = enabled;
                else db.NotificationPreferences.Add(new NotificationPreference { UserId = sub, Kind = kind, Enabled = enabled });
            try { await db.SaveChangesAsync(ct); break; }
            catch (DbUpdateException) when (attempt < Attempts) { }
        }
        return Results.Ok(await ReadAsync(db, sub, ct));
    }

    static async Task<NotificationPreferencesDto> ReadAsync(BookingDbContext db, string userId, CancellationToken ct)
    {
        var off = await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == userId && !p.Enabled).Select(p => p.Kind).ToListAsync(ct);
        return new(NotificationPreferenceRules.Switchable.Select(k => new NotificationPreferenceDto(k.ToString(), !off.Contains(k))).ToList());
    }
}
