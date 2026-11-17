using System.Net;
using System.Net.Http.Json;
using Gabinety.Contracts.Directory;
using Gabinety.ServiceDefaults;

namespace Gabinety.Reputation.Clients;

/// <summary>GET /internal/v1/practitioners/by-user/{userId} (CONTRACTS 3.6).</summary>
public sealed record DirectoryPractitioner(Guid PractitionerId, PractitionerStatus Status);

/// <summary>Directory did not answer in budget (connection failure, timeout or 5xx after the retry). The message never carries the URL or the key.</summary>
public sealed class DirectoryUnavailableException(Exception? inner = null) : Exception("Directory is unavailable.", inner);

public interface IDirectoryClient
{
    /// <summary>Null when the user is not a practitioner (Directory answers 404). Throws <see cref="DirectoryUnavailableException"/> when Directory cannot answer.</summary>
    Task<DirectoryPractitioner?> GetPractitionerByUserAsync(string userId, CancellationToken ct);
}

/// <summary>
/// Typed client for Directory's internal by-user lookup (ADR-0007, SPEC 5.1 budget 3 s, retried once, as Booking's client of the same name). The retry is
/// explicit and the client is built outside the shared resilience pipeline, whose defaults do not fit this budget. (The summary push has its own client.)
/// </summary>
public sealed class DirectoryClient(HttpClient http, string? internalKey, TimeSpan? retryDelay = null, TimeSpan? timeout = null) : IDirectoryClient
{
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3);
    readonly TimeSpan _timeout = timeout ?? CallTimeout, _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(150);

    public async Task<DirectoryPractitioner?> GetPractitionerByUserAsync(string userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = $"internal/v1/practitioners/by-user/{Uri.EscapeDataString(userId)}";
        Exception? last = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var response = await SendOnce(path, ct);
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                if ((int)response.StatusCode >= 500) { if (attempt == 1) { await Pause(ct); continue; } throw new DirectoryUnavailableException(); }
                if (!response.IsSuccessStatusCode) throw new DirectoryUnavailableException(); // 401 and the like: a configuration fault, retrying will not help
                return await response.Content.ReadFromJsonAsync<DirectoryPractitioner>(ct) ?? throw new DirectoryUnavailableException();
            }
            catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException) && !ct.IsCancellationRequested)
            {
                last = ex;
                if (attempt == 1) await Pause(ct);
            }
        }
        throw new DirectoryUnavailableException(last);
    }

    Task Pause(CancellationToken ct) => _retryDelay > TimeSpan.Zero ? Task.Delay(_retryDelay, ct) : Task.CompletedTask;

    async Task<HttpResponseMessage> SendOnce(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrEmpty(internalKey)) request.Headers.Add("X-Internal-Key", internalKey);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);
        return await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token);
    }
}

/// <summary>Registers the client from Backend:Directory and Internal:Keys:Directory (CONTRACTS 10.3). The key is read here and never logged.</summary>
public sealed class DirectoryClientFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) =>
        builder.Services.AddSingleton<IDirectoryClient>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var url = config["Backend:Directory"]?.TrimEnd('/');
            var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2), ConnectTimeout = DirectoryClient.CallTimeout })
            { BaseAddress = string.IsNullOrEmpty(url) ? null : new Uri(url + "/"), Timeout = Timeout.InfiniteTimeSpan };
            return new DirectoryClient(http, config["Internal:Keys:Directory"]);
        });

    public void Map(IEndpointRouteBuilder endpoints) { }
}
