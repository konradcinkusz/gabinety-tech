using Gabinety.Directory.Features.Internal.DemoReset;
using Gabinety.Directory.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Directory.Features.Verification;

/// <summary>
/// The GitHub lookup of the fake/Development profile (CONTRACTS 10.3/10.4): no network. A login starting with <c>demo-</c> is an existing account whose
/// public bio carries the code of the pending challenge issued for that login (the lookup is told only the login, so it reads the code from the challenge
/// row, which is exactly what a person pasting the code into their bio would produce); a demo login with no pending challenge has an empty bio. Any other
/// login is NOT FOUND. Registered only when <see cref="NonDeployedProfile"/> is active; nothing is logged.
/// </summary>
public sealed class FakeGithubProfileLookup(DirectoryDbContext db) : IGithubProfileLookup
{
    public const string Prefix = "demo-";

    public static void Register(IServiceCollection services) => services.AddScoped<IGithubProfileLookup, FakeGithubProfileLookup>();

    public async Task<GithubProfile> GetBioAsync(string login, CancellationToken ct)
    {
        if (!GithubHandle.IsValid(login)) throw new ArgumentException("not a GitHub handle", nameof(login)); // same contract as the real lookup
        if (!login.StartsWith(Prefix, StringComparison.Ordinal)) return GithubProfile.NotFound;
        var code = await db.Practitioners.AsNoTracking()
            .Where(p => p.GithubChallengeLogin == login && p.GithubChallengeCode != null)
            .OrderByDescending(p => p.GithubChallengeExpiresAt)
            .Select(p => p.GithubChallengeCode).FirstOrDefaultAsync(ct);
        return GithubProfile.Found(code is null ? null : "demo profile " + code);
    }
}
