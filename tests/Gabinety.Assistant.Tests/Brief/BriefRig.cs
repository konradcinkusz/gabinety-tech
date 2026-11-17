using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Gabinety.Assistant.Clients;
using Gabinety.Assistant.Persistence;
using Gabinety.Assistant.Policy;
using Gabinety.Assistant.Tests.Usage;
using Gabinety.Assistant.Usage;
using Gabinety.Contracts.Directory;
using Gabinety.Llm;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Gabinety.Assistant.Tests.Brief;

public sealed class StubTaxonomy(bool down = false) : ITaxonomyProvider
{
    public static readonly TaxonomyDto Full = new("2026-11-v1",
        new[] { "audyt-kodu", "migracja-legacy-dotnet", "integracja-ai", "automatyzacja-procesow", "architektura-systemu", "code-review", "wycena-projektu", "rozwiazanie-awarii" }
            .Select(s => new CategoryDto(s, s)).ToList(),
        new[] { "dotnet", "csharp", "aspnet-core", "entity-framework", "azure", "sql-server", "postgresql", "python", "llm", "kubernetes", "react", "typescript" }
            .Select(s => new CategoryDto(s, s)).ToList());
    public Task<TaxonomyDto> GetAsync(CancellationToken ct) => down ? throw new TaxonomyUnavailableException() : Task.FromResult(Full);
}

/// <summary>Wraps the fake gateway: counts asks, keeps prompts/options, and lets a test override a reply (script returns null = use the fake's).</summary>
public sealed class StubGateway(Func<int, string, string?>? script = null, LlmGatewayState? state = null, IReadOnlyList<LlmModelInfo>? models = null) : ILlmGateway
{
    readonly FakeLlmGateway _inner = new();
    readonly Func<int, string, string?>? _script = script;
    public int Asks;
    public int Disposed;
    public bool TokenWasCancelled;
    public List<string> Prompts { get; } = [];
    public List<LlmConversationOptions> Started { get; } = [];
    public LlmGatewayStatus Status => state is { } s ? new(s, "stub") : _inner.Status;
    public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken ct) => models is null ? _inner.ListModelsAsync(ct) : Task.FromResult(models);
    public async Task<ILlmConversation> StartConversationAsync(LlmConversationOptions options, CancellationToken ct)
    {
        Started.Add(options);
        return new Conv(this, await _inner.StartConversationAsync(options, ct));
    }
    sealed class Conv(StubGateway g, ILlmConversation inner) : ILlmConversation
    {
        public string ModelId => inner.ModelId;
        public async Task<LlmReply> AskAsync(string prompt, CancellationToken ct)
        {
            var n = Interlocked.Increment(ref g.Asks);
            g.Prompts.Add(prompt);
            if (g._script?.Invoke(n, prompt) is { } text) return new LlmReply(text, TimeSpan.FromMilliseconds(5), prompt.Length / 4, text.Length / 4);
            try { return await inner.AskAsync(prompt, ct); }
            catch (OperationCanceledException) { g.TokenWasCancelled = ct.IsCancellationRequested; throw; }
        }
        public ValueTask DisposeAsync() { Interlocked.Increment(ref g.Disposed); return inner.DisposeAsync(); }
    }
}

public sealed class CaptureLogger : ILoggerProvider, ILogger
{
    public List<string> Lines { get; } = [];
    public ILogger CreateLogger(string c) => this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel l) => true;
    public void Log<TState>(LogLevel l, EventId e, TState s, Exception? ex, Func<TState, Exception?, string> f)
    { lock (Lines) Lines.Add(f(s, ex) + " " + ex); }
    public void Dispose() { }
}

/// <summary>An Assistant host: in-memory RSA key as JWKS, fake clock, stub taxonomy, optional gateway and settings.</summary>
public sealed class BriefRig : IDisposable
{
    public const string Salt = "demo-salt-not-a-secret";
    readonly RSA _rsa = RSA.Create(2048);
    public readonly FakeClock Clock = new(DateTimeOffset.UtcNow.AddMinutes(1)); // after the token's nbf, which the host checks against this clock
    public readonly CaptureLogger Logs = new();
    public readonly WebApplicationFactory<Program> Factory;
    public readonly string Sub = "demo-user-" + Guid.NewGuid().ToString("N");
    public string UserHash => new UserHasher(Options.Create(new AssistantOptions { UserHashSalt = Salt })).Hash(Sub);

    public BriefRig(ILlmGateway? gateway = null, ITaxonomyProvider? taxonomy = null, params (string Key, string Value)[] settings)
    {
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Identity:Audience", "assistant-test");
            b.UseSetting("Llm:Provider", "Fake");
            b.UseSetting("Assistant:UserHashSalt", Salt);
            foreach (var (k, v) in settings) b.UseSetting(k, v);
            b.ConfigureLogging(l => l.AddProvider(Logs));
            b.ConfigureServices(s =>
            {
                s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(Clock);
                s.RemoveAll<ITaxonomyProvider>(); s.AddSingleton(taxonomy ?? new StubTaxonomy());
                if (gateway is not null) { s.RemoveAll<ILlmGateway>(); s.AddSingleton(gateway); }
                s.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    var cfg = new OpenIdConnectConfiguration { Issuer = "https://issuer.demo.invalid" };
                    cfg.SigningKeys.Add(new RsaSecurityKey(_rsa) { KeyId = "k1" });
                    o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(cfg);
                });
            });
        });
    }

    public HttpClient Client(bool signedIn = true)
    {
        var c = Factory.CreateClient();
        if (signedIn)
        {
            var t = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = "https://issuer.demo.invalid", Audience = "assistant-test", Expires = DateTime.UtcNow.AddDays(3),
                Claims = new Dictionary<string, object> { ["sub"] = Sub },
                SigningCredentials = new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = "k1" }, SecurityAlgorithms.RsaSha256),
            });
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", t);
        }
        return c;
    }

    public Task<HttpResponseMessage> Post(string text, bool signedIn = true) =>
        Client(signedIn).PostAsJsonAsync("/api/v1/assistant/brief", new { text });

    public async Task<List<UsageRow>> Rows()
    {
        using var scope = Factory.Services.CreateScope();
        var h = UserHash;
        return await scope.ServiceProvider.GetRequiredService<AssistantDbContext>().Usage.Where(u => u.UserHash == h).OrderBy(u => u.At).ToListAsync();
    }

    public void Dispose() { Factory.Dispose(); _rsa.Dispose(); }
}
