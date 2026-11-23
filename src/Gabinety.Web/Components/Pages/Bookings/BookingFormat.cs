using Gabinety.Web.Api;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Bookings;

/// <summary>How a booking is described, in one place so a number or a date reads the same on every page (BLAZOR-WEB-APP section 10). Times are shown in Europe/Warsaw whatever the server's zone (CONTRACTS 0).</summary>
public static class BookingFormat
{
    public static readonly string[] Statuses = [.. Pl.Bookings.Statuses.Keys];
    public static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    public static string StatusLabel(string status) => Pl.Bookings.Statuses.TryGetValue(status, out var label) ? label : Pl.Bookings.UnknownStatus;

    /// <summary><c>niedziela, 28 marca, 13:00–14:00</c></summary>
    public static string When(BookingViewDto b)
    {
        var start = TimeZoneInfo.ConvertTime(b.StartUtc, Warsaw);
        var end = TimeZoneInfo.ConvertTime(b.EndUtc, Warsaw);
        return $"{Pl.Format.Weekdays[(int)start.DayOfWeek]}, {start.Day} {Pl.Format.MonthsGenitive[start.Month - 1]}, {start:HH\\:mm}–{end:HH\\:mm}";
    }

    /// <summary><c>niedziela, 28 marca, 13:00–14:00</c> for any pair of instants (the booking form has a start and a duration, a booking has both).</summary>
    public static string When(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        var start = TimeZoneInfo.ConvertTime(startUtc, Warsaw);
        var end = TimeZoneInfo.ConvertTime(endUtc, Warsaw);
        return $"{Pl.Format.Weekdays[(int)start.DayOfWeek]}, {start.Day} {Pl.Format.MonthsGenitive[start.Month - 1]}, {start:HH\\:mm}–{end:HH\\:mm}";
    }

    /// <summary>The parts that exist, joined by " · ": a missing name never leaves a stray separator at either end (F-03: "· poniedziałek, 23 listopada · 180,00 zł").</summary>
    public static string MetaLine(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    /// <summary>A person's display name when the API sent one, else null (the caller leaves the part out instead of printing a placeholder where a name is the point).</summary>
    public static string? Name(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

    /// <summary>The other party's name when the API sent one, else a neutral label.</summary>
    public static string OtherParty(BookingViewDto b, string role)
    {
        var name = (role == BookingClient.ClientRole ? b.Practitioner.DisplayName : b.Client?.DisplayName)?.Trim();
        return string.IsNullOrEmpty(name) ? (role == BookingClient.ClientRole ? Pl.Bookings.PartyPractitioner : Pl.Bookings.PartyClient) : name;
    }

    /// <summary>What to tell the user about a failed call, from its code (never from the server's title or detail).</summary>
    public static string FailureText(ApiFailure f) => f switch
    {
        { Status: 0 } => Pl.Bookings.ErrorNetwork,
        { Code: "dependency_unavailable" } or { Status: 503 } => Pl.Bookings.ErrorUnavailable,
        { Code: "forbidden" } or { Status: 403 } => Pl.Bookings.ErrorForbidden,
        { Code: "rate_limited" } or { Status: 429 } => Pl.Bookings.ErrorRateLimited,
        _ => Pl.Bookings.ErrorGeneric,
    };

    /// <summary>The states a booking ended in by a cancellation: the header says what happens to the money.</summary>
    public static bool IsCancelled(string status) => status is "CancelledByClient" or "LateCancelledByClient" or "CancelledByPractitioner";

    /// <summary>The link of "Zarezerwuj ponownie": the practitioner's public profile, only when the slug is known and only for a finished booking (completed or cancelled).</summary>
    public static string? RebookHref(string status, string? slug) =>
        (status == "Completed" || IsCancelled(status)) && !string.IsNullOrWhiteSpace(slug) ? "/gabinet/" + Uri.EscapeDataString(slug.Trim()) : null;

    public static string Role(string? raw) => raw == BookingClient.PractitionerRole ? BookingClient.PractitionerRole : BookingClient.ClientRole;

    public static string StatusFilter(string? raw) => raw is not null && Pl.Bookings.Statuses.ContainsKey(raw) ? raw : "";
}
