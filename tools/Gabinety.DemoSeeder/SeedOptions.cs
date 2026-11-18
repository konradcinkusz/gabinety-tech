using Microsoft.Extensions.Configuration;

namespace Gabinety.DemoSeeder;

/// <summary>Endpoints and keys, from configuration only (command line <c>--Backend:Directory=...</c> or environment <c>Backend__Directory</c>). Nothing is hard-coded but the documented development password.</summary>
public sealed record SeedOptions(string DirectoryUrl, string DirectoryKey, string IdentityAuthority, string DemoPassword)
{
    /// <summary>Booking (T-08.01b): <c>Backend:Booking</c> and <c>Internal:Keys:Booking</c>. Init-only so the four-argument constructor of the skeleton stays as it is.</summary>
    public string BookingUrl { get; init; } = "";
    public string BookingKey { get; init; } = "";

    /// <summary>Reputation (T-08.01c): <c>Backend:Reputation</c> and <c>Internal:Keys:Reputation</c>.</summary>
    public string ReputationUrl { get; init; } = "";
    public string ReputationKey { get; init; } = "";

    /// <summary>The scale of the dataset (T-10.04): <c>--factor N</c> or <c>Seed:Factor</c>. 1 is the ordinary demo; N means 10 * N practitioners and 100 * N reviews in all (<see cref="ScalePlan"/>).</summary>
    public int Factor { get; init; } = 1;

    /// <summary>The fixed development password of tools/Gabinety.DevIdentity (README). It protects nothing; override with Identity:DemoPassword.</summary>
    public const string DevIdentityPassword = "Demo-Dev-Passw0rd!";

    public static SeedOptions From(IConfiguration c)
    {
        var missing = new List<string>();
        string Need(string key) { var v = c[key]; if (string.IsNullOrWhiteSpace(v)) missing.Add(key); return v?.Trim() ?? ""; }
        var directory = FirstUrl(Need("Backend:Directory"));
        var key = Need("Internal:Keys:Directory");
        var authority = Need("Identity:Authority").TrimEnd('/');
        var booking = FirstUrl(c["Backend:Booking"] ?? ""); // optional: without it the Booking step says so and is skipped
        var bookingKey = c["Internal:Keys:Booking"]?.Trim() ?? "";
        var reputation = FirstUrl(c["Backend:Reputation"] ?? ""); // optional: without it the Reputation step says so and is skipped
        var reputationKey = c["Internal:Keys:Reputation"]?.Trim() ?? "";
        var factor = 1;
        if (c["Seed:Factor"] is { Length: > 0 } f && (!int.TryParse(f, out factor) || factor is < 1 or > 1000)) missing.Add("Seed:Factor (an integer from 1 to 1000)");
        if (missing.Count > 0) throw new SeedException("missing configuration: " + string.Join(", ", missing)); // names of keys, never values
        return new SeedOptions(directory, key, authority, c["Identity:DemoPassword"] is { Length: > 0 } p ? p : DevIdentityPassword)
            { Factor = factor, BookingUrl = booking, BookingKey = bookingKey, ReputationUrl = reputation, ReputationKey = reputationKey };
    }

    /// <summary>The web app's Backend:* values are candidate lists; the seeder takes the first.</summary>
    public static string FirstUrl(string list) => list.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()?.TrimEnd('/') ?? "";
}

/// <summary>
/// Refuses to run unless the target is clearly the fake or development profile. Two checks, both must pass:
/// 1. the same predicate the services use to map their demo endpoints (CONTRACTS 10.4): <c>GABINETY_FAKE=1</c> or <c>Environment:Kind=Development</c> in this process;
/// 2. every endpoint is on this machine (localhost or a loopback address), unless <c>--i-know-this-is-demo</c> is given.
/// Why both: the variable describes the shell the seeder runs in, not the system it writes to; a developer with GABINETY_FAKE=1 exported and a staging URL
/// in the configuration would otherwise seed staging. The flag is the deliberate, visible act for a remote or containerised fake profile.
/// </summary>
public static class SeedGuard
{
    public const string Flag = "--i-know-this-is-demo";
    static readonly string[] EndpointKeys = ["Backend:Directory", "Backend:Booking", "Backend:Reputation", "Identity:Authority"];

    /// <returns>null when the run is allowed, else the reason (host names and key names only).</returns>
    public static string? Refusal(IConfiguration c, bool iKnowThisIsDemo)
    {
        var fake = c["GABINETY_FAKE"] == "1" || string.Equals(c["Environment:Kind"], "Development", StringComparison.OrdinalIgnoreCase);
        if (!fake) return "refusing to run: this is not the fake or development profile (set GABINETY_FAKE=1 or Environment:Kind=Development).";
        if (iKnowThisIsDemo) return null;
        foreach (var key in EndpointKeys)
        {
            if (c[key] is not { Length: > 0 } value) continue;
            if (!Uri.TryCreate(SeedOptions.FirstUrl(value), UriKind.Absolute, out var uri) || !IsLocal(uri))
                return $"refusing to run: {key} is not on this machine; add {Flag} if that target really is a demo system.";
        }
        return null;
    }

    static bool IsLocal(Uri u) => u.IsLoopback || u.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
}
