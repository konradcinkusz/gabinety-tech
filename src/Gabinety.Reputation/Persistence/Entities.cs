using Gabinety.Contracts.Reputation;

namespace Gabinety.Reputation.Persistence;

public enum ModerationEventKind { Created, Held, Published, Rejected, Replied }

/// <summary>CONTRACTS 5.1 reviews. One review per booking (unique booking_id).</summary>
public sealed class Review
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid PractitionerId { get; set; }
    public string ClientUserId { get; set; } = "";
    public short Rating { get; set; }
    public string Text { get; set; } = "";
    public ReviewStatus Status { get; set; }
    public List<string> HoldReasons { get; set; } = [];
    public TriageFlag TriageFlag { get; set; } = TriageFlag.None;
    public string? TriageModelId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? ModeratedBy { get; set; }
    public string? ModerationNote { get; set; }
    public string? ReplyText { get; set; }
    public DateTimeOffset? RepliedAt { get; set; }
}

/// <summary>CONTRACTS 5.1 rating_summaries; Version increments on every change, PushedVersion trails it until pushed to Directory.</summary>
public sealed class RatingSummary
{
    public Guid PractitionerId { get; set; }
    public int Count { get; set; }
    public int Sum { get; set; }
    public long Version { get; set; }
    public long PushedVersion { get; set; }
}

/// <summary>CONTRACTS 5.1 moderation_events. Actor is "system", "triage" or a user id.</summary>
public sealed class ModerationEvent
{
    public Guid Id { get; set; }
    public Guid ReviewId { get; set; }
    public ModerationEventKind Kind { get; set; }
    public string Actor { get; set; } = "";
    public string? Note { get; set; }
    public DateTimeOffset At { get; set; }
}
