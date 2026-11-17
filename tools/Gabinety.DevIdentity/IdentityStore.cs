using System.Security.Cryptography;

namespace Gabinety.DevIdentity;

public sealed record DevUser(string Id, string Email, byte[] Salt, byte[] Hash, string[] Roles, DateTimeOffset CreatedAt)
{
    public DateTimeOffset? LastLoginAt { get; set; }
}

/// <summary>In-memory user list and refresh-token ledger. Everything is lost on restart; that is the point of a stand-in.</summary>
public sealed class IdentityStore(TimeProvider time)
{
    // Fixed development value; stated in the README of this tool. Not a secret.
    public const string SeedPassword = "Demo-Dev-Passw0rd!";
    public const string SeedEmailDomain = "demo.example";
    public static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(7); // authservice default (spike section 1)

    private sealed class RefreshEntry(string userId, string family, DateTimeOffset expires)
    {
        public string UserId { get; } = userId;
        public string Family { get; } = family;
        public DateTimeOffset Expires { get; } = expires;
        public bool Used { get; set; }
        public bool Revoked { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, DevUser> _byEmail = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DevUser> _byId = new();
    private readonly Dictionary<string, RefreshEntry> _refresh = new();

    public void Seed()
    {
        Add("demo-client");
        for (var i = 1; i <= 8; i++) Add($"demo-practitioner-{i}");
        Add("demo-admin", "Admin"); // SPEC 6.7: the admin carries the authservice platform role Admin

        void Add(string name, params string[] roles) =>
            TryAdd($"{name}@{SeedEmailDomain}", SeedPassword, roles, null, out _);
    }

    // The subject of a demo user is its user name (demo-client, demo-practitioner-3, ...), because the Booking and Reputation demo reset removes rows whose user id
    // starts with "demo-" (CONTRACTS 10.4). Only the demo address domain qualifies, so a real-looking registration can never land in the namespace.
    // Any other user gets a random opaque id (lost with the process, like the user).
    private static string IdFor(string email)
    {
        var at = email.LastIndexOf('@');
        if (at > 0 && email[(at + 1)..].Equals(SeedEmailDomain, StringComparison.OrdinalIgnoreCase) && email[..at].StartsWith("demo-", StringComparison.OrdinalIgnoreCase))
            return email[..at].ToLowerInvariant();
        return Guid.NewGuid().ToString("N");
    }

    public bool TryAdd(string email, string password, string[] roles, string? id, out DevUser user)
    {
        lock (_gate)
        {
            if (_byEmail.TryGetValue(email, out user!)) return false;
            var salt = RandomNumberGenerator.GetBytes(16);
            user = new DevUser(id ?? IdFor(email), email, salt, Hash(password, salt), roles, time.GetUtcNow());
            _byEmail[email] = user;
            _byId[user.Id] = user;
            return true;
        }
    }

    public DevUser? Authenticate(string email, string password)
    {
        lock (_gate)
        {
            if (!_byEmail.TryGetValue(email, out var user)) return null;
            if (!CryptographicOperations.FixedTimeEquals(user.Hash, Hash(password, user.Salt))) return null;
            user.LastLoginAt = time.GetUtcNow();
            return user;
        }
    }

    public DevUser? Find(string id) { lock (_gate) return _byId.GetValueOrDefault(id); }

    // Development stand-in: a low iteration count keeps start-up fast.
    private static byte[] Hash(string password, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(password, salt, 10_000, HashAlgorithmName.SHA256, 32);

    public string IssueRefresh(DevUser user, string? family = null)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)); // opaque, 64 random bytes base64 (spike section 1)
        lock (_gate) _refresh[token] = new RefreshEntry(user.Id, family ?? Guid.NewGuid().ToString("N"), time.GetUtcNow() + RefreshLifetime);
        return token;
    }

    /// <summary>Rotates on use. A used, revoked or expired token is refused; reusing a used one revokes its whole family (spike section 1).</summary>
    public (DevUser User, string RefreshToken)? Rotate(string token)
    {
        lock (_gate)
        {
            if (!_refresh.TryGetValue(token, out var entry)) return null;
            if (entry.Used || entry.Revoked)
            {
                foreach (var e in _refresh.Values.Where(e => e.Family == entry.Family)) e.Revoked = true;
                return null;
            }
            if (entry.Expires <= time.GetUtcNow() || !_byId.TryGetValue(entry.UserId, out var user)) return null;
            entry.Used = true;
            return (user, IssueRefresh(user, entry.Family));
        }
    }

    /// <summary>Logout revokes every refresh token of the user, not just the current one (spike section 3).</summary>
    public void RevokeAll(string userId)
    {
        lock (_gate) foreach (var e in _refresh.Values.Where(e => e.UserId == userId)) e.Revoked = true;
    }
}
