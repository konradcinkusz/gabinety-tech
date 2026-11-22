using System.Collections.Concurrent;

namespace Gabinety.Booking.Features.Proposals;

/// <summary>
/// Creating proposals: 5 per hour and 20 per day per user (owner constants, ADR proposal 0013), sliding windows on <see cref="TimeProvider"/>. Per instance, in memory. A feature limiter, like the
/// Assistant's brief limiter, because the shared kernel is at its size budget. Refused calls are not counted.
/// </summary>
public sealed class ProposalRateLimiter(TimeProvider time)
{
    public const int PerHour = 5, PerDay = 20;
    readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _calls = new();

    /// <summary>Counts the call and returns true when it is allowed; otherwise how long to wait is in <paramref name="retryAfter"/>.</summary>
    public bool TryAcquire(string userId, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        var now = time.GetUtcNow();
        var q = _calls.GetOrAdd(userId, _ => new Queue<DateTimeOffset>());
        lock (q)
        {
            while (q.Count > 0 && now - q.Peek() >= TimeSpan.FromDays(1)) q.Dequeue();
            var lastHour = q.Where(t => now - t < TimeSpan.FromHours(1)).Order().ToList();
            if (lastHour.Count >= PerHour) { retryAfter = TimeSpan.FromHours(1) - (now - lastHour[0]); return false; }
            if (q.Count >= PerDay) { retryAfter = TimeSpan.FromDays(1) - (now - q.Peek()); return false; }
            q.Enqueue(now);
            return true;
        }
    }
}
