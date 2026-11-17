using System.Net;
using System.Net.Http.Json;
using Gabinety.ServiceDefaults;

namespace Gabinety.Booking.Jobs.StatsPush;

/// <summary>Body of <c>PUT /internal/v1/practitioners/{id}/booking-stats</c> (CONTRACTS 3.6). A version not greater than the stored one is ignored by Directory.</summary>
public sealed record BookingStatsPush(int CompletedCount, int PractitionerCancelCount, int NoShowUpheldCount, int OpenSlots14d, long Version);

public interface IDirectoryStatsClient
{
    /// <summary>True when Directory accepted the push (any 2xx). False after the retries are spent or on a refusal; never throws except on cancellation.</summary>
    Task<bool> PushAsync(Guid practitionerId, BookingStatsPush body, CancellationToken ct);
}

/// <summary>
/// Typed client for the booking-stats push. SPEC 5.1: 5 s per call, up to 3 times with backoff (the monotonic version makes the PUT idempotent).
/// Three attempts in all, 250 ms then 1 s apart. Nothing here throws on failure and nothing logs the URL, the key or an exception message.
/// </summary>
public sealed class DirectoryStatsClient(HttpClient http, string? internalKey, ILogger<DirectoryStatsClient> log, TimeSpan? timeout = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IDirectoryStatsClient
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);
    static readonly TimeSpan[] Backoff = [TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1)];
    readonly TimeSpan _timeout = timeout ?? CallTimeout;
    readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<bool> PushAsync(Guid practitionerId, BookingStatsPush body, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            string failure;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, $"internal/v1/practitioners/{practitionerId}/booking-stats") { Content = JsonContent.Create(body) };
                if (!string.IsNullOrEmpty(internalKey)) request.Headers.Add("X-Internal-Key", internalKey);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_timeout);
                using var response = await http.SendAsync(request, cts.Token);
                if (response.IsSuccessStatusCode) return true;
                var code = (int)response.StatusCode;
                failure = $"status {code}";
                if (code < 500 && response.StatusCode is not (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests))
                {
                    log.LogWarning("StatsPush refused for practitioner {PractitionerId}: {Failure}; not retried", practitionerId, failure); // 400/401/404: a retry cannot help
                    return false;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                failure = ex.GetType().Name; // never the message: it can carry the URL
            }
            log.LogWarning("StatsPush attempt {Attempt} of {Max} failed for practitioner {PractitionerId}: {Failure}", attempt, MaxAttempts, practitionerId, failure);
            if (attempt < MaxAttempts) await _delay(Backoff[attempt - 1], ct);
        }
        return false;
    }
}

/// <summary>Registers the client from Backend:Directory and Internal:Keys:Directory (CONTRACTS 10.3). The key is read here and never logged.</summary>
static class DirectoryStatsClientRegistration
{
    public static IServiceCollection AddDirectoryStatsClient(this IServiceCollection services) =>
        services.AddSingleton<IDirectoryStatsClient>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var url = config["Backend:Directory"]?.TrimEnd('/');
            var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2), ConnectTimeout = DirectoryStatsClient.CallTimeout })
            { BaseAddress = string.IsNullOrEmpty(url) ? null : new Uri(url + "/"), Timeout = Timeout.InfiniteTimeSpan };
            return new DirectoryStatsClient(http, config["Internal:Keys:Directory"], sp.GetRequiredService<ILogger<DirectoryStatsClient>>());
        });
}
