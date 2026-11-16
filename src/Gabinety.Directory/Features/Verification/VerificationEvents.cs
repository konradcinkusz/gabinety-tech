using Gabinety.Directory.Persistence;

namespace Gabinety.Directory.Features.Verification;

/// <summary>The only writer of `verification_events`: it ADDS a row to the context and does not save, so the caller's single SaveChanges
/// commits the status change and its event in one transaction. There is deliberately no update or delete method.</summary>
public static class VerificationEvents
{
    public static VerificationEvent Record(DirectoryDbContext db, Guid practitionerId, VerificationEventKind kind, string actorUserId,
        string? note, TimeProvider time, Guid? id = null)
    {
        var e = new VerificationEvent { Id = id ?? Guid.NewGuid(), PractitionerId = practitionerId, Kind = kind, ActorUserId = actorUserId, Note = note, At = time.GetUtcNow() };
        db.VerificationEvents.Add(e);
        return e;
    }
}
