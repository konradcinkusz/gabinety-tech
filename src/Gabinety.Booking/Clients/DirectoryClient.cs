using System.Net;
using System.Net.Http.Json;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.ServiceDefaults;

namespace Gabinety.Booking.Clients;

/// <summary>GET /internal/v1/practitioners/by-user/{userId} (CONTRACTS 3.6).</summary>
public sealed record DirectoryPractitioner(Guid PractitionerId, PractitionerStatus Status);

/// <summary>GET /internal/v1/offerings/{id} (CONTRACTS 3.6).</summary>
public sealed record DirectoryOffering(Guid OfferingId, Guid PractitionerId, string PractitionerUserId, PractitionerStatus PractitionerStatus,
    string Title, int DurationMinutes, MoneyDto Price, string? MeetingUrl, bool IsActive);

/// <summary>GET /internal/v1/practitioners/{id}: display name and slug for the booking views (ADR-0012 section 6).</summary>
public sealed record DirectoryPractitionerName(Guid PractitionerId, string DisplayName, string Slug);
/// <summary>GET /internal/v1/practitioners/{userId}/contact: the practitioner's e-mail, a snapshot of the token they last used on their profile. Personal data: never logged.</summary>
public sealed record DirectoryContact(string? Email);

/// <summary>Directory did not answer in budget (connection failure, timeout or 5xx after the retry). The message never carries the URL or the key.</summary>
public sealed class DirectoryUnavailableException(Exception? inner = null) : Exception("Directory is unavailable.", inner);

public interface IDirectoryClient
{
    /// <summary>Null when Directory answers 404.</summary>
    Task<DirectoryOffering?> GetOfferingAsync(Guid offeringId, CancellationToken ct);
    /// <summary>Null when the user is not a practitioner (404).</summary>
    Task<DirectoryPractitioner?> GetPractitionerByUserAsync(string userId, CancellationToken ct);
    /// <summary>Null when the practitioner is unknown (404).</summary>
    Task<DirectoryPractitionerName?> GetPractitionerNameAsync(Guid practitionerId, CancellationToken ct);
    /// <summary>The e-mail snapshot of a practitioner's user. Null when Directory answers 404 (unknown user, or no address held yet).</summary>
    Task<DirectoryContact?> GetPractitionerContactAsync(string userId, CancellationToken ct);
    /// <summary>One probe of /alive, no retry; false on any failure or non-200.</summary>
    Task<bool> IsAliveAsync(CancellationToken ct);
}

/// <summary>
/// Typed client for Directory (ADR-0007, SPEC 5.1: 3 s, once). The retry is explicit here and the client is built outside the
/// shared resilience pipeline, whose defaults (several retries, 10 s per attempt) do not fit this budget.
/// </summary>
public sealed class DirectoryClient(HttpClient http, string? internalKey, TimeSpan? retryDelay = null, TimeSpan? timeout = null) : IDirectoryClient
{
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3);
    readonly TimeSpan _timeout = timeout ?? CallTimeout, _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(150);

    public Task<DirectoryOffering?> GetOfferingAsync(Guid offeringId, CancellationToken ct) =>
        GetAsync<DirectoryOffering>($"internal/v1/offerings/{offeringId}", ct);

    public Task<DirectoryPractitioner?> GetPractitionerByUserAsync(string userId, CancellationToken ct) =>
        GetAsync<DirectoryPractitioner>($"internal/v1/practitioners/by-user/{Uri.EscapeDataString(userId)}", ct);

    public Task<DirectoryPractitionerName?> GetPractitionerNameAsync(Guid practitionerId, CancellationToken ct) =>
        GetAsync<DirectoryPractitionerName>($"internal/v1/practitioners/{practitionerId}", ct);
    public Task<DirectoryContact?> GetPractitionerContactAsync(string userId, CancellationToken ct) =>
        GetAsync<DirectoryContact>($"internal/v1/practitioners/{Uri.EscapeDataString(userId)}/contact", ct);

    public async Task<bool> IsAliveAsync(CancellationToken ct)
    {
        try
        {
            using var response = await SendOnce("alive", ct);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return false; }
    }

    async Task<T?> GetAsync<T>(string path, CancellationToken ct) where T : class
    {
        ct.ThrowIfCancellationRequested();
        Exception? last = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var response = await SendOnce(path, ct);
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                if ((int)response.StatusCode >= 500) { if (attempt == 1) { await Pause(ct); continue; } throw new DirectoryUnavailableException(); }
                if (!response.IsSuccessStatusCode) throw new DirectoryUnavailableException(); // 401 and the like: a configuration fault, retrying will not help
                return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new DirectoryUnavailableException();
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
