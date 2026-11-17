using System.Net;
using Gabinety.Booking.Jobs.StatsPush;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>Stands in for <see cref="IDirectoryStatsClient"/>: records every push; <see cref="Result"/> is the answer, <see cref="Throw"/> simulates a client bug.</summary>
sealed class RecordingStatsClient : IDirectoryStatsClient
{
    public readonly List<(Guid Practitioner, BookingStatsPush Body)> Pushes = new();
    public bool Result = true, Throw;

    public Task<bool> PushAsync(Guid practitionerId, BookingStatsPush body, CancellationToken ct)
    {
        if (Throw) throw new InvalidOperationException("boom");
        lock (Pushes) Pushes.Add((practitionerId, body));
        return Task.FromResult(Result);
    }

    public List<BookingStatsPush> For(Guid id) { lock (Pushes) return Pushes.Where(p => p.Practitioner == id).Select(p => p.Body).ToList(); }
}

/// <summary>Counting handler for the client tests: <see cref="Answer"/> maps the call number (from 1) to a response; nothing sleeps.</summary>
sealed class CountingHandler : HttpMessageHandler
{
    public readonly List<HttpRequestMessage> Requests = new();
    public readonly List<string> Bodies = new();
    public Func<int, HttpResponseMessage> Answer = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
    {
        Requests.Add(r);
        Bodies.Add(r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct));
        return Answer(Requests.Count);
    }
}

static class StatsFx
{
    /// <summary>A booking of the host's practitioner in <paramref name="status"/> (in the past, so it never blocks a future slot).</summary>
    public static async Task<Guid> Seed(CreateHost h, BookingStatus status, string? resolution = null, string? kind = null, int minutes = 60, TimeSpan? startIn = null)
    {
        var id = await Life.Seed(h, status, startIn ?? TimeSpan.FromDays(-3), TimeSpan.FromMinutes(minutes));
        await h.Seed(db =>
        {
            var b = db.Bookings.Single(x => x.Id == id);
            b.DurationMinutes = (short)minutes; b.DisputeResolution = resolution; b.DisputeKind = kind;
        });
        return id;
    }

    public static StatsPushJob Job(CreateHost h, IDirectoryStatsClient client, ILogger<StatsPushJob>? log = null) =>
        new(h.Services.GetRequiredService<IServiceScopeFactory>(), client, h.Clock, log ?? NullLogger<StatsPushJob>.Instance);

    public static async Task<BookingStats> StatsOf(CreateHost h)
    {
        using var scope = h.Services.CreateScope();
        var calc = scope.ServiceProvider.GetRequiredService<StatsCalculator>();
        return (await calc.ComputeAsync(default)).TryGetValue(h.Practitioner, out var s) ? s : throw new InvalidOperationException("practitioner not in the set");
    }
}
