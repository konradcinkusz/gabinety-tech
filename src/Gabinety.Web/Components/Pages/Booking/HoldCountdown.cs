using System.Globalization;

namespace Gabinety.Web.Components.Pages.Booking;

/// <summary>The 10 minute hold of a slot as the checkout shows it: the time left as <c>m:ss</c>, and the two moments a screen reader is told about (5 minutes and 1 minute, never every second: DESIGN-CONTRACT 7.9).</summary>
public static class HoldCountdown
{
    public static TimeSpan Left(DateTimeOffset expiresAt, DateTimeOffset now) => expiresAt - now is { } d && d > TimeSpan.Zero ? d : TimeSpan.Zero;

    /// <summary><c>m:ss</c>, rounded up to the second, never negative.</summary>
    public static string Clock(TimeSpan left)
    {
        var s = (int)Math.Ceiling(Math.Max(0, left.TotalSeconds));
        return string.Create(CultureInfo.InvariantCulture, $"{s / 60}:{s % 60:00}");
    }

    public static bool Expired(TimeSpan left) => left <= TimeSpan.Zero;

    public enum Phase { Plenty, FiveMinutes, OneMinute, Expired }

    /// <summary>
    /// What a screen reader is told: nothing while there is plenty of time, one sentence when 5 minutes are left, another when 1 minute is left, and the expiry. The status text changes only when the phase
    /// changes, so it is announced once per phase and never every second.
    /// </summary>
    public static Phase PhaseOf(TimeSpan left) => left <= TimeSpan.Zero ? Phase.Expired : left <= TimeSpan.FromMinutes(1) ? Phase.OneMinute : left <= TimeSpan.FromMinutes(5) ? Phase.FiveMinutes : Phase.Plenty;
}
