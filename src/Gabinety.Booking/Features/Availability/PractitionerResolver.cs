using System.Collections.Concurrent;
using Gabinety.Booking.Clients;

namespace Gabinety.Booking.Features.Availability;

/// <summary>Maps the token's user id to the practitioner id through Directory, cached per user for 60 s. A non-practitioner is not cached.</summary>
public sealed class PractitionerResolver(IDirectoryClient directory, TimeProvider time)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    readonly ConcurrentDictionary<string, (Guid Id, DateTimeOffset At)> _cache = new();

    /// <summary>Forgets every answer. The demo reset calls it: the Directory is reset and reseeded together with Booking, and the same demo user then has a new practitioner id.</summary>
    public void Clear() => _cache.Clear();

    /// <summary>Null when the user is not a practitioner. Throws DirectoryUnavailableException when Directory cannot answer.</summary>
    public async Task<Guid?> ResolveAsync(string userId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (_cache.TryGetValue(userId, out var hit) && now - hit.At < Ttl) return hit.Id;

        var found = await directory.GetPractitionerByUserAsync(userId, ct);
        if (found is null) { _cache.TryRemove(userId, out _); return null; }
        _cache[userId] = (found.PractitionerId, now);
        return found.PractitionerId;
    }
}
