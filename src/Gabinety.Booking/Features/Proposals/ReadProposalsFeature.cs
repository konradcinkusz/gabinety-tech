using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Gabinety.Booking.Features.Proposals;

/// <summary>
/// GET /api/v1/proposals/{id} and GET /api/v1/me/proposals?role=client|practitioner&amp;state=open|closed&amp;limit=&amp;cursor= (ADR proposal 0013). A party sees the proposal; anyone else gets 404, which
/// tells a stranger nothing about whether it exists. The list is the caller's own rows for one role, most recent activity first, ties by id descending, so the order is total. The cursor is opaque:
/// the position of the last row plus the role and state it was made for and a short checksum (the same design as the bookings list). Admins have no access in this version.
/// </summary>
public sealed class ReadProposalsFeature : IServiceFeature
{
    public const int DefaultLimit = 12, MaxLimit = 24;

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/proposals/{id:guid}", GetAsync).RequireAuthorization("Authenticated");
        endpoints.MapGet("/api/v1/me/proposals", ListAsync).RequireAuthorization("Authenticated");
    }

    static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, BookingDbContext db, ProposalViewBuilder views, CancellationToken ct)
    {
        var sub = CreateProposalFeature.Sub(user);
        if (sub is null) return Results.Unauthorized();
        var p = await db.Proposals.AsNoTracking().Include(x => x.Rounds).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null || ProposalViewBuilder.RoleOf(p, sub) is not { } role) return CreateProposalFeature.Problem(ErrorCodes.NotFound, "Not found", 404);
        return Results.Ok(await views.ViewAsync(p, role, ct));
    }

    static async Task<IResult> ListAsync(string? role, string? state, string? cursor, string? limit, ClaimsPrincipal user, BookingDbContext db,
        ProposalViewBuilder views, CancellationToken ct)
    {
        var sub = CreateProposalFeature.Sub(user);
        if (sub is null) return Results.Unauthorized();

        var errors = new Dictionary<string, string[]>();
        if (role is not ("client" or "practitioner")) errors["role"] = ["The role is required: client or practitioner."];
        if (state is not (null or "" or "open" or "closed")) errors["state"] = ["The state is open or closed."];
        var take = DefaultLimit;
        if (limit is not null && !(int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out take) && take is >= 1 and <= MaxLimit))
            errors["limit"] = [$"The limit is 1 to {MaxLimit}."];
        var stateKey = string.IsNullOrEmpty(state) ? "-" : state;
        Position? after = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            after = Decode(cursor, role, stateKey);
            if (after is null) errors["cursor"] = ["The cursor is not valid for this listing."];
        }
        if (errors.Count > 0) return CreateProposalFeature.Problem(ErrorCodes.ValidationFailed, "Validation failed", 400, errors);

        var asClient = role == "client";
        var q = db.Proposals.AsNoTracking().Where(p => asClient ? p.ClientUserId == sub : p.PractitionerUserId == sub);
        if (state == "open") q = q.Where(p => p.Status == ProposalStatus.AwaitingPractitioner || p.Status == ProposalStatus.AwaitingClient);
        else if (state == "closed") q = q.Where(p => p.Status != ProposalStatus.AwaitingPractitioner && p.Status != ProposalStatus.AwaitingClient);
        if (after is { } a) q = q.Where(p => p.LastActionAt < a.At || (p.LastActionAt == a.At && p.Id.CompareTo(a.Id) < 0));

        var rows = await q.OrderByDescending(p => p.LastActionAt).ThenByDescending(p => p.Id).Take(take + 1).ToListAsync(ct);
        var more = rows.Count > take;
        if (more) rows.RemoveAt(take);

        var names = await views.NamesAsync(rows, ct);
        var viewer = asClient ? ProposalActor.Client : ProposalActor.Practitioner;
        var items = rows.Select(p => ProposalViewBuilder.Summary(p, viewer, names[p.Id].Practitioner, names[p.Id].Client)).ToList();
        return Results.Ok(new ProposalsPage(items, more && rows.Count > 0 ? Encode(new Position(rows[^1].LastActionAt, rows[^1].Id), role!, stateKey) : null));
    }

    readonly record struct Position(DateTimeOffset At, Guid Id);

    static string Payload(Position p, string role, string state) =>
        string.Join('|', role, state, p.At.UtcTicks.ToString(CultureInfo.InvariantCulture), p.Id.ToString("N"));

    static string Check(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)), 0, 6);

    static string Encode(Position p, string role, string state)
    {
        var payload = Payload(p, role, state);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + "." + Check(payload);
    }

    /// <summary>The position, or null for anything that is not a cursor made by <see cref="Encode"/> for this role and state.</summary>
    static Position? Decode(string cursor, string? role, string state)
    {
        try
        {
            var dot = cursor.IndexOf('.');
            if (dot <= 0 || role is null) return null;
            var b64 = cursor[..dot].Replace('-', '+').Replace('_', '/');
            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=')));
            if (!string.Equals(Check(payload), cursor[(dot + 1)..], StringComparison.Ordinal)) return null;
            var parts = payload.Split('|');
            if (parts.Length != 4 || parts[0] != role || parts[1] != state) return null;
            if (!long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || ticks > DateTimeOffset.MaxValue.Ticks) return null;
            if (!Guid.TryParseExact(parts[3], "N", out var id)) return null;
            return new Position(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }
        catch (FormatException) { return null; }
        catch (ArgumentException) { return null; }
    }
}

/// <summary><c>nextCursor</c> is omitted from the JSON on the last page (CONTRACTS 0).</summary>
public sealed record ProposalsPage(IReadOnlyList<ProposalSummary> Items,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? NextCursor);
