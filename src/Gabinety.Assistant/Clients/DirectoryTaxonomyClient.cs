using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Contracts.Directory;
using Gabinety.ServiceDefaults;

namespace Gabinety.Assistant.Clients;

/// <summary>The live taxonomy (CONTRACTS 2). Throws <see cref="TaxonomyUnavailableException"/> when there is none to use.</summary>
public interface ITaxonomyProvider { Task<TaxonomyDto> GetAsync(CancellationToken ct); }

/// <summary>No taxonomy: Directory is down (or not configured) and no value younger than 24 h is cached. The message carries no URL.</summary>
public sealed class TaxonomyUnavailableException(Exception? inner = null) : Exception("Taxonomy is unavailable.", inner);

/// <summary>
/// GET /api/v1/taxonomy of Directory (anonymous), cached 10 minutes; when a refresh fails the previous value is used for up to
/// 24 hours (CONTRACTS 6). One 3 s attempt, no retry: the caller's model budget is the larger one.
/// </summary>
public sealed class DirectoryTaxonomyClient(HttpClient http, TimeProvider time) : ITaxonomyProvider
{
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3), Fresh = TimeSpan.FromMinutes(10), MaxStale = TimeSpan.FromHours(24);
    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    readonly SemaphoreSlim _gate = new(1, 1);
    TaxonomyDto? _value;
    DateTimeOffset _fetchedAt;

    public async Task<TaxonomyDto> GetAsync(CancellationToken ct)
    {
        if (Current() is { } fresh) return fresh;
        await _gate.WaitAsync(ct);
        try
        {
            if (Current() is { } again) return again;
            try
            {
                _value = await FetchAsync(ct);
                _fetchedAt = time.GetUtcNow();
                return _value;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or TaxonomyUnavailableException && !ct.IsCancellationRequested)
            {
                if (_value is not null && time.GetUtcNow() - _fetchedAt <= MaxStale) return _value;
                throw new TaxonomyUnavailableException(ex);
            }
        }
        finally { _gate.Release(); }
    }

    TaxonomyDto? Current() => _value is not null && time.GetUtcNow() - _fetchedAt < Fresh ? _value : null;

    async Task<TaxonomyDto> FetchAsync(CancellationToken ct)
    {
        if (http.BaseAddress is null) throw new InvalidOperationException("Backend:Directory is not configured.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallTimeout);
        using var response = await http.GetAsync("api/v1/taxonomy", cts.Token);
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<TaxonomyDto>(Web, cts.Token);
        // An unseeded Directory answers an empty version: not a usable taxonomy.
        if (dto is null || string.IsNullOrEmpty(dto.Version) || dto.Problems.Count == 0) throw new TaxonomyUnavailableException();
        return dto;
    }
}

/// <summary>Registers the client from Backend:Directory (CONTRACTS 10.3), built outside the shared resilience pipeline.</summary>
public sealed class DirectoryTaxonomyClientFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) =>
        builder.Services.AddSingleton<ITaxonomyProvider>(sp =>
        {
            var url = sp.GetRequiredService<IConfiguration>()["Backend:Directory"]?.TrimEnd('/');
            var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2), ConnectTimeout = DirectoryTaxonomyClient.CallTimeout })
            { BaseAddress = string.IsNullOrEmpty(url) ? null : new Uri(url + "/"), Timeout = Timeout.InfiniteTimeSpan };
            return new DirectoryTaxonomyClient(http, sp.GetRequiredService<TimeProvider>());
        });

    public void Map(IEndpointRouteBuilder endpoints) { }
}
