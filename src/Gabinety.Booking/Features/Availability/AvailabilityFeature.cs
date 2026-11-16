using System.Globalization;
using System.Security.Claims;
using Gabinety.Booking.Clients;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;

namespace Gabinety.Booking.Features.Availability;

/// <summary>GET and PUT /api/v1/me/availability (CONTRACTS 4.2). PUT replaces rules and blocked days as a whole.</summary>
public sealed class AvailabilityFeature : IServiceFeature
{
    public const int MaxRules = 28, MaxExceptions = 90;

    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<PractitionerResolver>();
        builder.Services.AddScoped<AvailabilityStore>();
    }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var g = endpoints.MapGroup("/api/v1/me/availability").RequireAuthorization("Authenticated");
        g.MapGet("", (ClaimsPrincipal user, PractitionerResolver who, AvailabilityStore store, CancellationToken ct) =>
            AsPractitioner(user, who, async id =>
            {
                var (rules, days) = await store.GetAsync(id, ct);
                return Results.Ok(ToDto(rules, days));
            }, ct));

        g.MapPut("", (AvailabilityDto body, ClaimsPrincipal user, PractitionerResolver who, AvailabilityStore store, CancellationToken ct) =>
            AsPractitioner(user, who, async id =>
            {
                var errors = Validate(body);
                if (errors.Count > 0)
                    return Problem(ApiProblem.Of(ErrorCodes.ValidationFailed, "Validation failed", 422, null,
                        errors.ToDictionary(e => e.Key, e => e.Value.ToArray())));

                var rules = body.Rules.Select(r => new AvailabilityRule
                {
                    Id = Guid.CreateVersion7(), PractitionerId = id, IsoWeekday = (short)r.IsoWeekday,
                    StartLocal = ParseHalfHour(r.StartLocal)!.Value, EndLocal = ParseHalfHour(r.EndLocal)!.Value,
                }).ToList();
                var days = body.Exceptions.Select(e => new AvailabilityException { Id = Guid.CreateVersion7(), PractitionerId = id, DateLocal = e.DateLocal }).ToList();
                await store.ReplaceAsync(id, rules, days, ct);
                return Results.Ok(ToDto(rules.OrderBy(r => r.IsoWeekday).ThenBy(r => r.StartLocal).ToList(), days.OrderBy(d => d.DateLocal).ToList()));
            }, ct));
    }

    /// <summary>Resolves the caller's practitioner id from Directory: 403 when there is none, 503 when Directory cannot answer. Any practitioner status may edit (onboarding needs it).</summary>
    static async Task<IResult> AsPractitioner(ClaimsPrincipal user, PractitionerResolver who, Func<Guid, Task<IResult>> run, CancellationToken ct)
    {
        var userId = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Results.Unauthorized();
        Guid? id;
        try { id = await who.ResolveAsync(userId, ct); }
        catch (DirectoryUnavailableException)
        {
            return Problem(ApiProblem.Of(ErrorCodes.DependencyUnavailable, "Dependency unavailable", 503, "A required service is temporarily unavailable."));
        }
        return id is { } practitioner
            ? await run(practitioner)
            : Problem(ApiProblem.Of(ErrorCodes.Forbidden, "Forbidden", 403, "The caller is not a practitioner."));
    }

    static IResult Problem(ApiProblem p) => Results.Json(p, statusCode: p.Status, contentType: "application/problem+json");

    static AvailabilityDto ToDto(IReadOnlyList<AvailabilityRule> rules, IReadOnlyList<AvailabilityException> days) => new(
        rules.Select(r => new AvailabilityRuleDto(r.IsoWeekday, Hhmm(r.StartLocal), Hhmm(r.EndLocal))).ToList(),
        days.Select(d => new AvailabilityExceptionDto(d.DateLocal)).ToList());

    static string Hhmm(TimeOnly t) => t.ToString("HH':'mm", CultureInfo.InvariantCulture);

    /// <summary>"HH:mm" on :00 or :30, or null. Start can be 00:00 to 23:30, so a midnight end (24:00) is not expressible.</summary>
    static TimeOnly? ParseHalfHour(string? s) =>
        TimeOnly.TryParseExact(s, "HH':'mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) && t.Minute % 30 == 0 ? t : null;

    static Dictionary<string, List<string>> Validate(AvailabilityDto body)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string key, string message) { if (!errors.TryGetValue(key, out var l)) errors[key] = l = []; l.Add(message); }

        if (body.Rules is null || body.Exceptions is null)
        {
            if (body.Rules is null) Add("rules", "rules is required");
            if (body.Exceptions is null) Add("exceptions", "exceptions is required");
            return errors;
        }
        if (body.Rules.Count > MaxRules) Add("rules", $"at most {MaxRules} rules");
        if (body.Exceptions.Count > MaxExceptions) Add("exceptions", $"at most {MaxExceptions} exceptions");
        if (errors.Count > 0) return errors; // too large to be worth walking

        var valid = new List<(int Day, TimeOnly Start, TimeOnly End)>();
        for (var i = 0; i < body.Rules.Count; i++)
        {
            var r = body.Rules[i];
            var ok = true;
            if (r.IsoWeekday is < 1 or > 7) { Add($"rules[{i}].isoWeekday", "must be 1 (Monday) to 7 (Sunday)"); ok = false; }
            var start = ParseHalfHour(r.StartLocal);
            var end = ParseHalfHour(r.EndLocal);
            if (start is null) { Add($"rules[{i}].startLocal", "must be HH:mm on :00 or :30"); ok = false; }
            if (end is null) { Add($"rules[{i}].endLocal", "must be HH:mm on :00 or :30"); ok = false; }
            if (start is { } s && end is { } e && e <= s) { Add($"rules[{i}].endLocal", "must be after startLocal"); ok = false; }
            if (ok) valid.Add((r.IsoWeekday, start!.Value, end!.Value));
        }
        foreach (var day in valid.GroupBy(v => v.Day))
        {
            var ordered = day.OrderBy(v => v.Start).ToList();
            for (var i = 1; i < ordered.Count; i++)
                if (ordered[i].Start < ordered[i - 1].End) { Add("rules", $"rules overlap on weekday {day.Key}"); break; }
        }

        if (body.Exceptions.GroupBy(e => e.DateLocal).Any(g => g.Count() > 1)) Add("exceptions", "dates must be unique");
        return errors;
    }
}
