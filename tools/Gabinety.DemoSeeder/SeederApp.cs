using Microsoft.Extensions.Configuration;

namespace Gabinety.DemoSeeder;

/// <summary>
/// Gabinety.DemoSeeder (T-08.01): fills a running fake-profile system with demo data through its public, user, admin and internal HTTP APIs, never a database.
/// <c>dotnet run --project tools/Gabinety.DemoSeeder -- --Backend:Directory=http://localhost:5101 --Internal:Keys:Directory=... --Identity:Authority=http://localhost:5100</c>
/// <c>--reset</c> first removes the demo namespace (Reputation, Booking, Directory) and then seeds again; <c>--reset-only</c> only removes. Exit codes: 0 done, 1 a step failed, 2 refused or misconfigured.
/// </summary>
public enum SeedMode { Seed, ResetThenSeed, ResetOnly }

public static class SeederApp
{
    public const string ResetFlag = "--reset", ResetOnlyFlag = "--reset-only";

    /// <summary>What one run does: fill, remove the demo namespace and then fill, or only remove.</summary>
    public static (SeedMode Mode, string[] Remaining) ParseMode(string[] args)
    {
        var mode = args.Contains(ResetOnlyFlag) ? SeedMode.ResetOnly : args.Contains(ResetFlag) ? SeedMode.ResetThenSeed : SeedMode.Seed;
        return (mode, Factor(args.Where(a => a != ResetFlag && a != ResetOnlyFlag).ToArray())); // flags without a value must not reach the command-line provider
    }

    public const string FactorFlag = "--factor";

    /// <summary><c>--factor 200</c> and <c>--factor=200</c> become the configuration key <c>--Seed:Factor=200</c> (the command-line provider reads that form).</summary>
    static string[] Factor(string[] args)
    {
        var result = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == FactorFlag && i + 1 < args.Length) result.Add("--Seed:Factor=" + args[++i]);
            else if (args[i].StartsWith(FactorFlag + "=", StringComparison.Ordinal)) result.Add("--Seed:Factor=" + args[i][(FactorFlag.Length + 1)..]);
            else result.Add(args[i]);
        }
        return result.ToArray();
    }

    public static async Task<int> Main(string[] args)
    {
        var (mode, rest) = ParseMode(args);
        var flag = rest.Contains(SeedGuard.Flag);
        var config = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(rest.Where(a => a != SeedGuard.Flag).ToArray()).Build();
        if (SeedGuard.Refusal(config, flag) is { } why) { Console.Error.WriteLine(why); return 2; }
        SeedOptions options;
        try { options = SeedOptions.From(config); }
        catch (SeedException e) { Console.Error.WriteLine(e.Message); return 2; }
        return await RunAsync(options, new ConfiguredSeedHttp(options), new ConsoleSeedLog(), TimeProvider.System, CancellationToken.None, mode: mode);
    }

    /// <summary>The run itself, with its collaborators handed in (the tests use in-process HTTP clients). The guard has already been passed.</summary>
    public static async Task<int> RunAsync(SeedOptions options, ISeedHttp http, ISeedLog log, TimeProvider time, CancellationToken ct, SeedContext? into = null, SeedMode mode = SeedMode.Seed)
    {
        var context = into ?? new SeedContext(options, http, log, time);
        try
        {
            if (mode != SeedMode.Seed)
            {
                foreach (var step in SeedSteps.All.Reverse()) // later steps hold what refers to earlier ones: Reputation, then Booking, then Directory
                {
                    var removed = await step.ResetAsync(context, ct);
                    log.Info(removed < 0 ? $"reset {step.Name}: skipped (no endpoint configured)" : $"reset {step.Name}: removed {removed}");
                }
                if (mode == SeedMode.ResetOnly) return 0;
            }
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
