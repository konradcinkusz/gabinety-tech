namespace Gabinety.Assistant.Tests.Usage;

/// <summary>Settable clock (Microsoft.Extensions.TimeProvider.Testing is not referenced).</summary>
public sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}
