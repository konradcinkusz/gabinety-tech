using System.Net;
using System.Net.Http.Json;
using Gabinety.ServiceDefaults;

namespace Gabinety.Reputation.Jobs.SummaryPush;

/// <summary>Body of <c>PUT /internal/v1/practitioners/{id}/reputation</c> (CONTRACTS 3.6). A version not greater than the stored one is ignored by Directory.</summary>
public sealed record ReputationPush(int RatingCount, decimal RatingAvg, long Version);

/// <summary><see cref="Accepted"/>: any 2xx. <see cref="NotFound"/>: Directory does not know the practitioner (never retried by the client). <see cref="Failed"/>: retries spent, or a refusal a retry cannot fix.</summary>
public enum PushResult { Accepted, NotFound, Failed }

public interface IDirectoryReputationClient
{
    /// <summary>Never throws except on cancellation.</summary>
    Task<PushResult> PushAsync(Guid practitionerId, ReputationPush body, CancellationToken ct);
}

/// <summary>
/// Typed client for the reputation push. SPEC 5.1: 5 s per call, up to 3 times in a row (the monotonic version makes the PUT idempotent), then again on the
/// next job run. Three attempts in all, 250 ms then 1 s apart. Nothing here throws on failure and nothing logs the URL, the key or an exception message.
/// </summary>
public sealed class DirectoryReputationClient(HttpClient http, string? internalKey, ILogger<DirectoryReputationClient> log, TimeSpan? timeout = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IDirectoryReputationClient
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);
    static readonly TimeSpan[] Backoff = [TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1)];
    readonly TimeSpan _timeout = timeout ?? CallTimeout;
    readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<PushResult> PushAsync(Guid practitionerId, ReputationPush body, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            string failure;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, $"internal/v1/practitioners/{practitionerId}/reputation") { Content = JsonContent.Create(body) };
                if (!string.IsNullOrEmpty(internalKey)) request.Headers.Add("X-Internal-Key", internalKey);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_timeout);
                using var response = await http.SendAsync(request, cts.Token);
                if (response.IsSuccessStatusCode) return PushResult.Accepted;
                var code = (int)response.StatusCode;
                if (response.StatusCode == HttpStatusCode.NotFound) return PushResult.NotFound;
                failure = $"status {code}";
                if (code < 500 && response.StatusCode is not (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests))
                {
                    log.LogWarning("SummaryPush refused for practitioner {PractitionerId}: {Failure}; not retried", practitionerId, failure); // 400/401: a retry cannot help
                    return PushResult.Failed;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException && !ct.IsCancellationRequested)
            {
                failure = ex.GetType().Name; // never the message: it can carry the URL
            }
            log.LogWarning("SummaryPush attempt {Attempt} of {Max} failed for practitioner {PractitionerId}: {Failure}", attempt, MaxAttempts, practitionerId, failure);
            if (attempt < MaxAttempts) await _delay(Backoff[attempt - 1], ct);
        }
        return PushResult.Failed;
    }
}

/// <summary>Registers the client from Backend:Directory and Internal:Keys:Directory (CONTRACTS 10.3). The key is read here and never logged.</summary>
static class DirectoryReputationClientRegistration
{
    public static IServiceCollection AddDirectoryReputationClient(this IServiceCollection services) =>
        services.AddSingleton<IDirectoryReputationClient>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var url = config["Backend:Directory"]?.TrimEnd('/');
            var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2), ConnectTimeout = DirectoryReputationClient.CallTimeout })
            { BaseAddress = string.IsNullOrEmpty(url) ? null : new Uri(url + "/"), Timeout = Timeout.InfiniteTimeSpan };
            return new DirectoryReputationClient(http, config["Internal:Keys:Directory"], sp.GetRequiredService<ILogger<DirectoryReputationClient>>());
        });
}
