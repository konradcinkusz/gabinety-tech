using System.Net.Http.Json;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;

namespace Gabinety.Reputation.Features.Analytics;

/// <summary>Booking did not answer in budget or answered something unusable. The message never carries the URL, the key or the body.</summary>
public sealed class BookingUnavailableForAnalyticsException(Exception? inner = null) : Exception("Booking is unavailable.", inner);

public interface IPairCountsClient
{
    /// <summary>GET /internal/v1/analytics/pair-counts: one page of pairs with at least <paramref name="minBookings"/> paid bookings.</summary>
    Task<Page<PairCountDto>> GetAsync(int minBookings, string? cursor, int limit, CancellationToken ct);
}

/// <summary>Typed client for Booking's pair counts (ADR-0007): internal key, 3 s, no retry (an administrator reloads). Failure is one exception type.</summary>
public sealed class PairCountsClient(HttpClient http, string? internalKey, TimeSpan? timeout = null) : IPairCountsClient
{
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3);

    public async Task<Page<PairCountDto>> GetAsync(int minBookings, string? cursor, int limit, CancellationToken ct)
    {
        var query = $"minBookings={minBookings}&limit={limit}" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"internal/v1/analytics/pair-counts?{query}");
        if (!string.IsNullOrEmpty(internalKey)) request.Headers.Add("X-Internal-Key", internalKey);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? CallTimeout);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token);
            if (!response.IsSuccessStatusCode) throw new BookingUnavailableForAnalyticsException();
            return await response.Content.ReadFromJsonAsync<Page<PairCountDto>>(cts.Token) ?? throw new BookingUnavailableForAnalyticsException();
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException) && !ct.IsCancellationRequested)
        {
            throw new BookingUnavailableForAnalyticsException(ex);
        }
    }
}

/// <summary>Registers the client from Backend:Booking and Internal:Keys:Booking (CONTRACTS 10.3). The key is read here and never logged.</summary>
public sealed class PairCountsClientFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) =>
        builder.Services.AddSingleton<IPairCountsClient>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var url = config["Backend:Booking"]?.TrimEnd('/');
            var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2), ConnectTimeout = PairCountsClient.CallTimeout })
            { BaseAddress = string.IsNullOrEmpty(url) ? null : new Uri(url + "/"), Timeout = Timeout.InfiniteTimeSpan };
            return new PairCountsClient(http, config["Internal:Keys:Booking"]);
        });

    public void Map(IEndpointRouteBuilder endpoints) { }
}
