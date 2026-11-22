using Gabinety.ServiceDefaults;

namespace Gabinety.Directory.Clients;

/// <summary>Booking did not settle the practitioner's bookings (not configured, unreachable, 5xx or some refund pending). The message never carries the URL or the key.</summary>
public sealed class BookingUnavailableException(Exception? inner = null) : Exception("Booking could not cancel the future bookings.", inner);

public interface IBookingClient
{
    /// <summary>
    /// <c>POST /internal/v1/practitioners/{id}/cancel-future-bookings</c> (ADR-0012): full refund and cancellation of every future Confirmed booking, holds expired. Idempotent,
    /// so a repeat is safe. Returns when Booking answered 2xx; throws <see cref="BookingUnavailableException"/> otherwise.
    /// </summary>
    Task CancelFutureBookingsAsync(Guid practitionerId, CancellationToken ct);
}

/// <summary>
/// Typed client for Booking. One attempt with a budget of 30 s (a refund is a provider call per booking): the call is idempotent, so the retry is the caller repeating
/// the action, not a hidden loop that holds the administrator's request. Built outside the shared resilience pipeline like the other service clients.
/// </summary>
public sealed class BookingClient(HttpClient http, string? internalKey, TimeSpan? timeout = null) : IBookingClient
{
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);
    readonly TimeSpan _timeout = timeout ?? CallTimeout;

    public async Task CancelFutureBookingsAsync(Guid practitionerId, CancellationToken ct)
    {
        if (http.BaseAddress is null) throw new BookingUnavailableException(); // a safety control that is not wired must be loud, never skipped
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"internal/v1/practitioners/{practitionerId}/cancel-future-bookings");
            if (!string.IsNullOrEmpty(internalKey)) request.Headers.Add("X-Internal-Key", internalKey);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode) throw new BookingUnavailableException();
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            throw new BookingUnavailableException(ex);
        }
    }
}

/// <summary>Registers the client from Backend:Booking and Internal:Keys:Booking (CONTRACTS 10.3). The key is read here and never logged.</summary>
public sealed class BookingClientFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) =>
        builder.Services.AddSingleton<IBookingClient>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var url = config["Backend:Booking"]?.TrimEnd('/');
            var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2), ConnectTimeout = TimeSpan.FromSeconds(3) })
            { BaseAddress = string.IsNullOrEmpty(url) ? null : new Uri(url + "/"), Timeout = Timeout.InfiniteTimeSpan };
            return new BookingClient(http, config["Internal:Keys:Booking"]);
        });

    public void Map(IEndpointRouteBuilder endpoints) { }
}
