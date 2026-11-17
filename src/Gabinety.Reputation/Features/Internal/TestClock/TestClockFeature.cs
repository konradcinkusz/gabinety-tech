using System.Text.Json;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Reputation.Features.Internal.TestClock;

/// <summary>
/// True for the profiles that are not a deployed environment (CONTRACTS 10.4): <c>GABINETY_FAKE=1</c> or <c>Environment:Kind=Development</c>.
/// Read from configuration, so the environment variable and a test setting behave the same.
/// </summary>
public static class NonDeployedProfile
{
    public static bool IsActive(IConfiguration config) =>
        config["GABINETY_FAKE"] == "1" || string.Equals(config["Environment:Kind"], "Development", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The signed offset, in minutes, shared by the decorator and the clock endpoint. Thread-safe.</summary>
public sealed class TestClockOffset
{
    long _minutes;
    public long Minutes { get => Interlocked.Read(ref _minutes); set => Interlocked.Exchange(ref _minutes, value); }
}

/// <summary>Adds the offset to the wrapped provider's <c>GetUtcNow()</c>; timers and timestamps are left to the wrapped provider.</summary>
public sealed class OffsetTimeProvider(TimeProvider inner, TestClockOffset offset) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow().AddMinutes(offset.Minutes);
    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;
    public override long TimestampFrequency => inner.TimestampFrequency;
    public override long GetTimestamp() => inner.GetTimestamp();
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => inner.CreateTimer(callback, state, dueTime, period);
}

public static class TestClockServiceExtensions
{
    /// <summary>
    /// Wraps whatever <see cref="TimeProvider"/> is registered at this point (the system one when none is) in an <see cref="OffsetTimeProvider"/>.
    /// Call it once, last: a later <c>RemoveAll&lt;TimeProvider&gt;</c> (a test's manual clock) removes the wrapper with it.
    /// </summary>
    public static IServiceCollection AddTestClock(this IServiceCollection services)
    {
        services.TryAddSingleton<TestClockOffset>();
        var previous = services.LastOrDefault(d => d.ServiceType == typeof(TimeProvider));
        if (previous != null) services.Remove(previous);
        services.AddSingleton<TimeProvider>(sp => new OffsetTimeProvider(Resolve(sp, previous), sp.GetRequiredService<TestClockOffset>()));
        return services;
    }

    static TimeProvider Resolve(IServiceProvider sp, ServiceDescriptor? d) => d switch
    {
        null => TimeProvider.System,
        { ImplementationInstance: TimeProvider i } => i,
        { ImplementationFactory: not null } => (TimeProvider)d.ImplementationFactory(sp),
        { ImplementationType: not null } => (TimeProvider)ActivatorUtilities.CreateInstance(sp, d.ImplementationType),
        _ => TimeProvider.System,
    };
}

/// <summary>
/// POST /internal/v1/test/clock (CONTRACTS 10.4; its GET, added by T-08.02, answers the same <c>{ offsetMinutes, now }</c> and changes nothing): <c>{ offsetMinutes }</c> moves the Reputation service's clock; 0 resets it. Mapped, and the offset
/// registered, only outside a deployed environment (<see cref="NonDeployedProfile"/>).
/// </summary>
public sealed class TestClockFeature : IServiceFeature
{
    /// <summary>Ten years in minutes (365.25 days a year).</summary>
    public const long MaxOffsetMinutes = 5_259_600;

    public void Add(IHostApplicationBuilder builder)
    {
        if (NonDeployedProfile.IsActive(builder.Configuration)) builder.Services.AddTestClock();
    }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        if (!NonDeployedProfile.IsActive(endpoints.ServiceProvider.GetRequiredService<IConfiguration>())) return;
        endpoints.MapPost("/internal/v1/test/clock", SetAsync).RequireAuthorization("Internal");
        endpoints.MapGet("/internal/v1/test/clock", (TestClockOffset offset, TimeProvider clock) =>
            Results.Ok(new { offsetMinutes = offset.Minutes, now = clock.GetUtcNow() })).RequireAuthorization("Internal"); // the read the seeder uses to restore a non-zero baseline
    }

    static async Task<IResult> SetAsync(HttpRequest request, TestClockOffset offset, TimeProvider clock, CancellationToken ct)
    {
        long? minutes = null;
        try
        {
            using var doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("offsetMinutes", out var v)
                && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var m)) minutes = m;
        }
        catch (JsonException) { }

        if (minutes is null or > MaxOffsetMinutes or < -MaxOffsetMinutes)
            return Results.Json(ApiProblem.Of(ErrorCodes.ValidationFailed, "Validation failed", 400, null,
                new Dictionary<string, string[]> { ["offsetMinutes"] = [$"offsetMinutes is required and must be an integer between -{MaxOffsetMinutes} and {MaxOffsetMinutes}"] }),
                statusCode: 400, contentType: "application/problem+json");

        offset.Minutes = minutes.Value;
        return Results.Ok(new { offsetMinutes = offset.Minutes, now = clock.GetUtcNow() });
    }
}
