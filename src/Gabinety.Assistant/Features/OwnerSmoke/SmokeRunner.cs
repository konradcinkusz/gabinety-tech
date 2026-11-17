using System.Globalization;
using Gabinety.Assistant.Clients;
using Gabinety.Assistant.Features.Brief;
using Gabinety.Assistant.Persistence;
using Gabinety.Assistant.Policy;
using Gabinety.Assistant.Usage;
using Gabinety.Contracts.Directory;
using Gabinety.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Gabinety.Assistant.Features.OwnerSmoke;

/// <summary>
/// The owner's own traffic policy (COPILOT-SDK 7): the owner may use a Copilot model on their own machine. It exists only inside the
/// smoke verb's service provider and is never registered in the web host, so the public policy of §7 is untouched.
/// </summary>
sealed class OwnerTrafficPolicy(ILlmGateway gateway, IOptions<AssistantOptions> options) : ITrafficPolicy
{
    public bool IsAllowed(LlmGatewayState state, LlmModelInfo model) => true;

    public async Task<LlmModelInfo?> SelectModelAsync(LlmGatewayState state, CancellationToken ct)
    {
        var models = (await gateway.ListModelsAsync(ct)).Where(m => m.Enabled).ToList();
        return models.FirstOrDefault(m => options.Value.Model is not null && m.Id == options.Value.Model) ?? models.FirstOrDefault();
    }
}

/// <summary>The seed taxonomy of CONTRACTS 2: the smoke run does not need Directory.</summary>
sealed class SeedTaxonomy : ITaxonomyProvider
{
    static CategoryDto C(string slug, string name) => new(slug, name);
    public Task<TaxonomyDto> GetAsync(CancellationToken ct) => Task.FromResult(new TaxonomyDto("2026-11-v1",
        [C("audyt-kodu", "Audyt kodu"), C("migracja-legacy-dotnet", "Migracja legacy .NET"), C("integracja-ai", "Integracja AI"),
         C("automatyzacja-procesow", "Automatyzacja procesów"), C("architektura-systemu", "Architektura systemu"), C("code-review", "Code review"),
         C("wycena-projektu", "Wycena i plan projektu"), C("rozwiazanie-awarii", "Rozwiązanie awarii")],
        [C("dotnet", ".NET"), C("csharp", "C#"), C("aspnet-core", "ASP.NET Core"), C("entity-framework", "Entity Framework"), C("azure", "Azure"),
         C("sql-server", "SQL Server"), C("postgresql", "PostgreSQL"), C("python", "Python"), C("llm", "LLM i agenci"), C("kubernetes", "Kubernetes"),
         C("react", "React"), C("typescript", "TypeScript")]));
}

/// <summary>
/// Runs the brief feature ONCE on a fixed synthetic Polish brief and prints model id, latency, tokens and AI credits (T-02.05).
/// Nothing but the parsed suggestion's slugs is printed from the brief or the reply. Usage goes to an in-memory database that is dropped.
/// </summary>
public static class SmokeRunner
{
    /// <summary>Synthetic (demo): no real person, firm or project.</summary>
    public const string BriefText = "demo: Mamy starą aplikację .NET Framework 4.8 z Web Forms i chcemy ją przenieść na nowoczesny .NET, bez przerwy w działaniu.";

    public static async Task<int> RunAsync(ILlmGateway gateway, TimeProvider time, TextWriter output, CancellationToken ct, string? preferredModel = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(gateway);
        services.AddSingleton(time);
        services.AddDbContext<AssistantDbContext>(o => o.UseInMemoryDatabase("smoke-" + Guid.NewGuid()));
        services.AddSingleton<ITaxonomyProvider, SeedTaxonomy>();
        services.AddSingleton<ITrafficPolicy, OwnerTrafficPolicy>();
        services.AddSingleton<UserHasher>();
        services.AddScoped<UsageRecorder>();
        services.AddScoped<BudgetGuard>();
        services.AddScoped<BriefService>();
        services.AddOptions<AssistantOptions>().Configure(o => { o.DailyBudgetUsd = decimal.MaxValue; o.Model = preferredModel; });
        services.AddOptions<BriefOptions>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<BriefService>().SuggestAsync("demo-owner", BriefText, ct);
        var row = await scope.ServiceProvider.GetRequiredService<AssistantDbContext>().Usage.SingleAsync(ct);

        string Or(object? v) => v is null ? "unknown" : Convert.ToString(v, CultureInfo.InvariantCulture)!;
        output.WriteLine($"smoke: {BriefService.PromptVersion}");
        output.WriteLine($"model: {row.ModelId}");
        output.WriteLine($"latency_ms: {row.LatencyMs}");
        output.WriteLine($"input_tokens: {Or(row.InputTokens)}");
        output.WriteLine($"output_tokens: {Or(row.OutputTokens)}");
        output.WriteLine($"ai_credits: {Or(row.AiCredits)}");
        output.WriteLine($"outcome: {row.Outcome}");
        if (result is BriefResult.Suggestion s)
        {
            output.WriteLine($"problem: {s.Value.ProblemSlug}");
            output.WriteLine($"tech: {string.Join(",", s.Value.TechSlugs)}");
            return 0;
        }
        return 1;
    }
}
