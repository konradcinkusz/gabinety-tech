namespace Gabinety.DemoSeeder;

/// <summary>
/// One seeding step: a class in its own file, registered by one line in <see cref="SeedSteps.All"/>. Steps run in ascending <see cref="Order"/>
/// (Directory 100, Booking 200, Reputation 300 by convention) and pass what later steps need through <see cref="SeedContext"/>.
/// A step must be idempotent: run twice, the second run creates and changes nothing.
/// </summary>
public interface ISeedStep
{
    string Name { get; }
    int Order { get; }
    Task RunAsync(SeedContext context, CancellationToken ct);
}
