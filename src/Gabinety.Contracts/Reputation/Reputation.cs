using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Gabinety.Contracts.Reputation;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReviewStatus { Published, Held, Rejected }

[JsonConverter(typeof(Gabinety.Contracts.Common.SnakeCaseLowerEnumConverter))]
public enum TriageFlag { None, Hold, Unavailable }

public sealed record CreateReviewRequest(Guid BookingId, [property: Range(1, 5)] int Rating,
    [property: Required, StringLength(2000, MinimumLength = 20)] string Text);
public sealed record CreateReviewResponse(Guid ReviewId, ReviewStatus Status);

public sealed record ReplyDto(string Text, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantConverter))] DateTimeOffset RepliedAt);
public sealed record ReviewDto(Guid Id, int Rating, string Text, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantConverter))] DateTimeOffset CreatedAt, ReplyDto? Reply);

public sealed record ReplyRequest([property: Required, StringLength(1000, MinimumLength = 10)] string Text);
public sealed record RejectReviewRequest([property: Required] string Reason);

public sealed record ReputationPush(int RatingCount, double RatingAvg, long Version);

/// <summary>One row of <c>GET /api/v1/admin/suspicious-pairs</c> (T-10.03): a pair with more than two paid bookings and only five-star reviews. Ids and counts only: no review text, no name.</summary>
public sealed record SuspiciousPairDto(string PairKey, string ClientUserId, Guid PractitionerId, int Bookings, int Reviews);
