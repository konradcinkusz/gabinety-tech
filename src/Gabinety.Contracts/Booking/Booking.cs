using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Gabinety.Contracts.Common;

namespace Gabinety.Contracts.Booking;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BookingStatus
{
    PendingPayment, Confirmed, CancelledByClient, LateCancelledByClient, CancelledByPractitioner, Expired,
    Completed, ClientNoShow, Disputed, Resolved,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PaymentStatus { Created, Succeeded, Failed, Refunded, PartiallyRefunded }

[JsonConverter(typeof(Gabinety.Contracts.Common.SnakeCaseLowerEnumConverter))]
public enum DisputeKind { NotAsDescribed, PractitionerNoShow, Other }

[JsonConverter(typeof(Gabinety.Contracts.Common.SnakeCaseLowerEnumConverter))]
public enum Resolution { Release, RefundFull, RefundPartial }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LedgerKind { ClientCharge, Refund, PlatformFee, PractitionerPayout, PayoutReversal }

public sealed record SlotDto([property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantConverter))] DateTimeOffset StartUtc, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantConverter))] DateTimeOffset EndUtc);
public sealed record SlotsResponse(IReadOnlyList<SlotDto> Slots);

public sealed record AvailabilityRuleDto(int IsoWeekday, string StartLocal, string EndLocal);
public sealed record AvailabilityExceptionDto(DateOnly DateLocal);
public sealed record AvailabilityDto(IReadOnlyList<AvailabilityRuleDto> Rules, IReadOnlyList<AvailabilityExceptionDto> Exceptions);

public sealed record BriefDto([property: Required, StringLength(2000, MinimumLength = 20)] string Text, string? ProblemSlug, bool AiAssisted);
public sealed record CreateBookingRequest(Guid OfferingId, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantConverter))] DateTimeOffset StartUtc, BriefDto Brief);
public sealed record CreateBookingResponse(Guid BookingId, BookingStatus Status, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantConverter))] DateTimeOffset HoldExpiresAt, string CheckoutUrl);

public sealed record CancelRequest([property: StringLength(500)] string? Reason = null);
public sealed record DisputeRequest(DisputeKind Kind, [property: Required, StringLength(1000, MinimumLength = 10)] string Text);
public sealed record ResolveRequest(Resolution Resolution, long? RefundMinor, [property: Required, StringLength(500, MinimumLength = 10)] string Note);
public sealed record SimulatePaymentRequest(Guid BookingId, string Outcome);

/// <summary>What cancelling would do for the viewer right now (CONTRACTS 4.4, 9.2). <c>partial</c> is part of the vocabulary; today's rules give only the other two.</summary>
[JsonConverter(typeof(Gabinety.Contracts.Common.SnakeCaseLowerEnumConverter))]
public enum CancelOutcomeKind { FullRefund, Partial, NoRefund }

/// <summary><c>cancelOutcome</c> of a <see cref="BookingView"/>: present only when <c>canCancel</c> is true; <c>refund</c> is the amount returned to the client, in grosz.</summary>
public sealed record CancelOutcomeDto(CancelOutcomeKind Outcome, MoneyDto Refund);

public sealed record PractitionerRef(Guid Id, string DisplayName, string Slug);
public sealed record ClientRef(string DisplayName);
public sealed record PaymentView(PaymentStatus Status, MoneyDto Refunded);

public sealed record BookingView(Guid Id, BookingStatus Status, string? StatusReason, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantConverter))] DateTimeOffset StartUtc, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantConverter))] DateTimeOffset EndUtc,
    int DurationMinutes, string OfferingTitle, MoneyDto Price, PractitionerRef Practitioner, ClientRef? Client, BriefDto? Brief,
    string? MeetingUrl, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantNullableConverter))] DateTimeOffset? HoldExpiresAt, PaymentView Payment, MoneyDto? Fee, MoneyDto? Payout,
    bool CanCancel, bool CanDispute, bool CanReportNoShow, bool CanReview, CancelOutcomeDto? CancelOutcome = null);

public sealed record ReviewabilityDto(bool Reviewable, string ClientUserId, Guid PractitionerId, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantNullableConverter))] DateTimeOffset? CompletedAt);

/// <summary>One row of <c>GET /internal/v1/analytics/pair-counts</c> (T-10.03): a client-practitioner pair and its paid bookings. <c>PairKey</c> is the fee hook's key. Ids and a count only.</summary>
public sealed record PairCountDto(string PairKey, string ClientUserId, Guid PractitionerId, int Bookings);
