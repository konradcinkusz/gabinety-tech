using Microsoft.Extensions.Configuration;

namespace Gabinety.ServiceDefaults;

/// <summary>
/// F-07: the OpenAPI document lists every route, <c>/internal/*</c> and <c>/api/v1/admin/*</c> included, so a deployed service does not serve it (CONTRACTS 0: development and CI).
/// Same rule as the other fake-profile routes (CONTRACTS 10.4): <c>GABINETY_FAKE=1</c> or <c>Environment:Kind=Development</c>, read from configuration only, never from
/// <c>ASPNETCORE_ENVIRONMENT</c>. The AppHost sets <c>Environment__Kind=Development</c> on every service, so <c>web/scripts/gen-types.mjs</c> keeps working locally and in CI.
/// </summary>
public static class OpenApiExposure
{
    public static bool IsServed(IConfiguration config) =>
        config["GABINETY_FAKE"] == "1" || string.Equals(config["Environment:Kind"], "Development", StringComparison.OrdinalIgnoreCase);
}
