using Gabinety.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gabinety.Assistant.Features.OwnerSmoke;

/// <summary>
/// <c>dotnet run --project src/Gabinety.Assistant -- smoke</c> (T-02.05). A command-line verb, NOT an HTTP endpoint: Program.cs
/// dispatches to it before any web host is built, and its own host has no web server. Exit codes: 0 ok, 1 the run failed, 2 refused.
/// It spends one real call of the configured model (AI credits on a Copilot model).
/// </summary>
public static class SmokeCommand
{
    public const string Verb = "smoke";
    public const string RefusedInCi = "smoke: refusing to run when CI is set (a live call spends real credits).";
    public const string RefusedFake = "smoke: Llm:Provider is Fake; a smoke run needs a real model.";

    public static bool IsSmoke(string[] args) => args.Length > 0 && args[0] == Verb;

    /// <summary>The verb's host: configuration and the gateway only. No Kestrel, no routes.</summary>
    public static IHost BuildHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args.Skip(1).ToArray());
        builder.AddLlmGateway();
        return builder.Build();
    }

    public static async Task<int> RunAsync(string[] args, TextWriter output, Func<string, string?> getEnv, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(getEnv("CI"))) { output.WriteLine(RefusedInCi); return 2; }

        using var host = BuildHost(args);
        var config = host.Services.GetRequiredService<IConfiguration>();
        if (string.Equals(config["Llm:Provider"], "Fake", StringComparison.OrdinalIgnoreCase)) { output.WriteLine(RefusedFake); return 2; }

        await host.StartAsync(ct); // starts the Copilot client
        try
        {
            var gateway = host.Services.GetRequiredService<ILlmGateway>();
            return await SmokeRunner.RunAsync(gateway, host.Services.GetRequiredService<TimeProvider>(), output, ct, config["Assistant:Model"]);
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }
}
