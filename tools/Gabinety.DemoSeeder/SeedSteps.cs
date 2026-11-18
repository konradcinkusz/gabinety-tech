namespace Gabinety.DemoSeeder;

/// <summary>The single registration list. Adding a step is one line here plus the step's own file.</summary>
public static class SeedSteps
{
    public static IReadOnlyList<ISeedStep> All { get; } = new ISeedStep[]
    {
        new DirectorySeedStep(),
        new BookingSeedStep(),
        new ReputationSeedStep(),
        new ScaleSeedStep(),
    }.OrderBy(s => s.Order).ToArray();
}
