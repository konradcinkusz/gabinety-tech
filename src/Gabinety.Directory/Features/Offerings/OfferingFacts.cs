using Gabinety.Directory.Features.Profiles;
using Gabinety.Directory.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Directory.Features.Offerings;

/// <summary>The real count behind the submit checklist item `offering`. A singleton (IChecklistFacts is one) that opens its own scope per call.</summary>
public sealed class OfferingFacts(IServiceScopeFactory scopes) : IChecklistFacts
{
    public async Task<int> ActiveOfferingCountAsync(Guid practitionerId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();
        return await db.Set<Offering>().CountAsync(o => o.PractitionerId == practitionerId && o.IsActive, ct);
    }
}
