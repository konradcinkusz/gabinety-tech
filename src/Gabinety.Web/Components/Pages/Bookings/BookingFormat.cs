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

    public static string Role(string? raw) => raw == BookingClient.PractitionerRole ? BookingClient.PractitionerRole : BookingClient.ClientRole;

    public static string StatusFilter(string? raw) => raw is not null && Pl.Bookings.Statuses.ContainsKey(raw) ? raw : "";
}
