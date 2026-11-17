using Microsoft.Extensions.Configuration;

namespace Gabinety.DemoSeeder;

/// <summary>
/// Gabinety.DemoSeeder (T-08.01): fills a running fake-profile system with demo data through its public, user, admin and internal HTTP APIs, never a database.
/// <c>dotnet run --project tools/Gabinety.DemoSeeder -- --Backend:Directory=http://localhost:5101 --Internal:Keys:Directory=... --Identity:Authority=http://localhost:5100</c>
/// Exit codes: 0 done, 1 a step failed, 2 refused or misconfigured.
/// </summary>
public static class SeederApp
{
    public static async Task<int> Main(string[] args)
    {
        var flag = args.Contains(SeedGuard.Flag);
        var config = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args.Where(a => a != SeedGuard.Flag).ToArray()).Build();
        if (SeedGuard.Refusal(config, flag) is { } why) { Console.Error.WriteLine(why); return 2; }
        SeedOptions options;
        try { options = SeedOptions.From(config); }
        catch (SeedException e) { Console.Error.WriteLine(e.Message); return 2; }
        return await RunAsync(options, new ConfiguredSeedHttp(options), new ConsoleSeedLog(), TimeProvider.System, CancellationToken.None);
    }

    /// <summary>The run itself, with its collaborators handed in (the tests use in-process HTTP clients). The guard has already been passed.</summary>
    public static async Task<int> RunAsync(SeedOptions options, ISeedHttp http, ISeedLog log, TimeProvider time, CancellationToken ct, SeedContext? into = null)
    {
        var context = into ?? new SeedContext(options, http, log, time);
        try
        {
            foreach (var step in SeedSteps.All)
            {
                log.Info($"step {step.Name}: start");
                await step.RunAsync(context, ct);
                log.Info($"step {step.Name}: done");
            }
            return 0;
        }
        catch (SeedException e) { log.Info("seeding failed: " + e.Message); return 1; }
        catch (HttpRequestException e) { log.Info($"seeding failed: a system is not reachable ({e.GetType().Name})"); return 1; }
    }
}
