using Gabinety.Directory.Persistence;
using Gabinety.Directory.Tests.Offerings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Gabinety.Directory.Tests.Internal;

/// <summary>Collects every formatted log line (and its structured values) of a host, so a test can prove what is never logged.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public System.Collections.Concurrent.ConcurrentQueue<string> Lines { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Capture(Lines);
    public void Dispose() { }

    sealed class Capture(System.Collections.Concurrent.ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lines.Enqueue(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> kv) foreach (var p in kv) lines.Enqueue(p.Key + "=" + p.Value);
        }
    }
}

/// <summary>
/// Directory host with a chosen profile (<c>GABINETY_FAKE</c> / <c>Environment:Kind</c>), the internal key and its OWN in-memory database: the reset removes every
/// demo- practitioner, so it must not share "directorydb" with the parallel tests of the process. No GitHub double: the lookup is whatever the profile registers.
/// A primary handler that throws for every outgoing HTTP call proves whether the host tried the network.
/// </summary>
public sealed class DemoResetHost(IReadOnlyDictionary<string, string> profile) : OfferingFactory
{
    public const string Key = "demo-internal-key-0002";
    readonly string _db = "demoreset-" + Guid.NewGuid().ToString("n");
    public CapturingLoggerProvider Logs { get; } = new();
    public int NetworkAttempts => _attempts;
    int _attempts;

    public static DemoResetHost Fake() => new(new Dictionary<string, string> { ["GABINETY_FAKE"] = "1" });
    public static DemoResetHost Kind(string kind) => new(new Dictionary<string, string> { ["Environment:Kind"] = kind });
    public static DemoResetHost Bare() => new(new Dictionary<string, string>());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Internal:Key", Key);
        foreach (var (k, v) in profile) builder.UseSetting(k, v);
        builder.ConfigureLogging(l => { l.SetMinimumLevel(LogLevel.Trace); l.AddProvider(Logs); });
        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<DbContextOptions<DirectoryDbContext>>();
            s.RemoveAll(typeof(IDbContextOptionsConfiguration<DirectoryDbContext>));
            s.RemoveAll<DirectoryDbContext>();
            s.AddDbContext<DirectoryDbContext>(o => o.UseInMemoryDatabase(_db));
            s.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => new Refusing(() => Interlocked.Increment(ref _attempts))));
        });
    }

    sealed class Refusing(Action seen) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            seen();
            throw new HttpRequestException("the test host refuses every outgoing call");
        }
    }

    public HttpClient Internal(string? key = Key)
    {
        var c = CreateClient();
        if (key is not null) c.DefaultRequestHeaders.Add("X-Internal-Key", key);
        return c;
    }

    public async Task<T> Db<T>(Func<DirectoryDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<DirectoryDbContext>());
    }

    public Task<T> Scoped<T>(Func<IServiceProvider, T> work)
    {
        using var scope = Services.CreateScope();
        return Task.FromResult(work(scope.ServiceProvider));
    }
}
