using Gabinety.Reputation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Reputation.Features.Reviews.Summary;

/// <summary>
/// rating_summaries writes (CONTRACTS 5.1, 5.3 step 3). Callers run it inside the transaction that changes the review, so count, sum and
/// version move together with the review. On PostgreSQL it is one atomic upsert (two reviews of one practitioner cannot lose an update);
/// elsewhere (InMemory) callers serialise through <see cref="InMemoryGate"/>.
/// </summary>
public static class RatingSummaryStore
{
    /// <summary>Process-wide gate for providers without row locks. Unused on PostgreSQL.</summary>
    public static readonly SemaphoreSlim InMemoryGate = new(1, 1);

    /// <summary>Adds <paramref name="countDelta"/> and <paramref name="sumDelta"/> and increments version by one; creates the row when absent.</summary>
    public static async Task ApplyAsync(ReputationDbContext db, Guid practitionerId, int countDelta, int sumDelta, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO rating_summaries ("practitioner_id", "count", "sum", "version", "pushed_version")
                VALUES ({practitionerId}, {countDelta}, {sumDelta}, 1, 0)
                ON CONFLICT ("practitioner_id") DO UPDATE SET
                    "count" = rating_summaries."count" + {countDelta},
                    "sum" = rating_summaries."sum" + {sumDelta},
                    "version" = rating_summaries."version" + 1
                """, ct);
            return;
        }
        var row = await db.RatingSummaries.SingleOrDefaultAsync(x => x.PractitionerId == practitionerId, ct);
        if (row is null) db.RatingSummaries.Add(new RatingSummary { PractitionerId = practitionerId, Count = countDelta, Sum = sumDelta, Version = 1 });
        else { row.Count += countDelta; row.Sum += sumDelta; row.Version++; }
        await db.SaveChangesAsync(ct);
    }
}
