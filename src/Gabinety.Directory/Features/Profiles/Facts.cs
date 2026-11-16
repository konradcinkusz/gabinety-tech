using System.Net.Http.Headers;
using System.Text.Json;

namespace Gabinety.Directory.Features.Profiles;

/// <summary>Facts owned by later tasks (offerings, GitHub linking). Only offerings are reported here; the implementation returns 0 until T-03.0x.</summary>
public interface IChecklistFacts { Task<int> ActiveOfferingCountAsync(Guid practitionerId, CancellationToken ct); }

public sealed class NoOfferingsYet : IChecklistFacts
{
    public Task<int> ActiveOfferingCountAsync(Guid practitionerId, CancellationToken ct) => Task.FromResult(0);
}

/// <summary>Whether authservice has confirmed the caller's e-mail. Never throws: any failure means "not verified".</summary>
public interface IEmailVerifiedLookup { Task<bool> IsVerifiedAsync(string bearerToken, CancellationToken ct); }

/// <summary>GET {Identity:Authority}/api/v1/auth/me with the caller's own bearer token; reads the boolean emailConfirmed (the token has no such claim).</summary>
public sealed class HttpEmailVerifiedLookup(HttpClient http, IConfiguration config) : IEmailVerifiedLookup
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<bool> IsVerifiedAsync(string bearerToken, CancellationToken ct)
    {
        try
        {
            var authority = config["Identity:Authority"];
            if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrEmpty(bearerToken)) return false;
            using var req = new HttpRequestMessage(HttpMethod.Get, authority.TrimEnd('/') + "/api/v1/auth/me");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return false;
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.TryGetProperty("emailConfirmed", out var v) && v.ValueKind == JsonValueKind.True;
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return false; }
    }
}
