using System.Text.Json;

namespace Gabinety.Directory.Features.Verification;

/// <summary>Answer of the GitHub profile lookup: the profile exists (with a bio that may be null) or it does not.</summary>
public sealed record GithubProfile(bool Exists, string? Bio)
{
    public static GithubProfile Found(string? bio) => new(true, bio);
    public static GithubProfile NotFound { get; } = new(false, null);
}

/// <summary>GitHub did not answer usefully: timeout, network error, 5xx, 403 or 429 (rate limited).</summary>
public sealed class GithubUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public interface IGithubProfileLookup { Task<GithubProfile> GetBioAsync(string login, CancellationToken ct); }

/// <summary>GET https://api.github.com/users/{login}: public, 5 s, no retry, no token.</summary>
public sealed class HttpGithubProfileLookup(HttpClient http) : IGithubProfileLookup
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<GithubProfile> GetBioAsync(string login, CancellationToken ct)
    {
        if (!GithubHandle.IsValid(login)) throw new ArgumentException("not a GitHub handle", nameof(login)); // never let free text reach the URL
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/users/" + login);
            req.Headers.TryAddWithoutValidation("User-Agent", "gabinety-tech");
            req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            using var res = await http.SendAsync(req, ct);
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return GithubProfile.NotFound;
            if (!res.IsSuccessStatusCode) throw new GithubUnavailableException($"GitHub answered {(int)res.StatusCode}");
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var bio = doc.RootElement.TryGetProperty("bio", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;
            return GithubProfile.Found(bio);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new GithubUnavailableException("GitHub did not answer", ex);
        }
    }
}

public static partial class GithubHandle
{
    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9-]{0,37}[A-Za-z0-9])?\z")]
    private static partial System.Text.RegularExpressions.Regex Pattern();

    /// <summary>1 to 39 characters of A-Z a-z 0-9 and hyphen, not starting or ending with a hyphen.</summary>
    public static bool IsValid(string? s) => s is not null && Pattern().IsMatch(s);
}
