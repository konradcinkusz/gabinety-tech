using System.Collections.Concurrent;
using Gabinety.Booking.Clients;

namespace Gabinety.Booking.Notifications;

/// <summary>
/// Finds the address of a notification's recipient from facts the service already holds, never from an authservice call (there is no "address by user id" there):
/// the CLIENT's address is the snapshot on the booking row (taken from the validated access token when the booking was created, <c>ContactLookup.ClientEmailSnapshot</c>);
/// the PRACTITIONER's address is a Directory fact (<c>GET /internal/v1/practitioners/{userId}/contact</c>, a snapshot of the token they used on their profile).
/// <para>Directory answers are cached for <see cref="CacheFor"/> (positive answers only, so a practitioner who just gave an address is found on the next attempt).
/// A Directory outage, timeout or refusal is NOT an exception here: it is a null address, which the dispatcher counts as a failed attempt and retries.
/// Cancellation is the only exception that escapes. PRIVACY: nothing here logs an address or a user id; the outcome class only.</para>
/// </summary>
public sealed class ContactResolver(IDirectoryClient directory, TimeProvider time, ILogger<ContactResolver> log) : IContactResolver
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    const int MaxCached = 1000;
    readonly ConcurrentDictionary<string, (string Address, DateTimeOffset Until)> _cache = new();

    public async Task<string?> ResolveAddressAsync(ContactLookup recipient, CancellationToken ct)
    {
        if (!recipient.IsPractitioner) return UserEmail.Normalize(recipient.ClientEmailSnapshot);

        var now = time.GetUtcNow();
        if (_cache.TryGetValue(recipient.UserId, out var hit) && hit.Until > now) return hit.Address;

        try
        {
            var contact = await directory.GetPractitionerContactAsync(recipient.UserId, ct);
            var address = UserEmail.Normalize(contact?.Email);
            if (address is null) { _cache.TryRemove(recipient.UserId, out _); return null; }
            if (_cache.Count >= MaxCached) _cache.Clear(); // a few thousand practitioners at most: a reset is cheaper than an eviction policy
            _cache[recipient.UserId] = (address, now + CacheFor);
            return address;
        }
        catch (DirectoryUnavailableException)
        {
            log.LogWarning("Practitioner address not resolved: Directory is unavailable; the notification is retried");
            return null;
        }
    }
}
