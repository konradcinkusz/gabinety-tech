namespace Gabinety.DemoSeeder;

/// <summary>Names of the systems a step talks to.</summary>
public static class SeedService
{
    public const string Directory = "Directory", Identity = "Identity", Booking = "Booking", Reputation = "Reputation";
}

/// <summary>The one place that makes HTTP clients, so the tests can hand in in-process ones and steps never build a client themselves.</summary>
public interface ISeedHttp
{
    HttpClient For(string service);
}

public sealed class ConfiguredSeedHttp(SeedOptions options) : ISeedHttp
{
    readonly Dictionary<string, HttpClient> _clients = new();

    public HttpClient For(string service)
    {
        if (_clients.TryGetValue(service, out var existing)) return existing;
        var url = service switch
        {
            SeedService.Directory => options.DirectoryUrl,
            SeedService.Identity => options.IdentityAuthority,
            SeedService.Booking => options.BookingUrl,
            SeedService.Reputation => options.ReputationUrl,
            _ => throw new SeedException($"no endpoint is configured for {service} yet"),
        };
        return _clients[service] = new HttpClient { BaseAddress = new Uri(url + "/"), Timeout = TimeSpan.FromSeconds(30) };
    }
}
