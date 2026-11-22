using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Gabinety.Contracts.Common;

namespace Gabinety.Contracts.Booking;

/// <summary>A collaboration proposal (ADR proposal 0013): a structured negotiation between a client and a practitioner. Not a consultation: no payment, no fee.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProposalKind { ShortTerm, LongTerm }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProposalStatus { AwaitingPractitioner, AwaitingClient, Accepted, Rejected, Withdrawn, Expired }

/// <summary>Who did something in a round, or who is looking at a proposal. <c>System</c> only appears as the actor of an expiry.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProposalActor { Client, Practitioner, System }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProposalAction { Proposed, Countered, Accepted, Rejected, Withdrawn, Expired }

/// <summary>
/// The negotiable terms. Short term: <c>TotalHours</c> is required. Long term: <c>HoursPerWeek</c> and <c>DurationMonths</c> are required.
/// The rate is per hour, in grosz.
/// </summary>
public sealed record ProposalTerms(MoneyDto Rate, int? HoursPerWeek, int? TotalHours, int? DurationMonths, DateOnly StartDate);

public sealed record CreateProposalRequest(Guid PractitionerId, ProposalKind Kind, ProposalTerms Terms,
    [property: Required, StringLength(1000, MinimumLength = 20)] string Text, [property: StringLength(120)] string? CompanyName = null);

/// <summary><c>Round</c> is the round the caller saw last; a stale number is a 409.</summary>
public sealed record CounterProposalRequest(int Round, ProposalTerms Terms, [property: StringLength(1000)] string? Text = null);

public sealed record ProposalActionRequest(int Round);

public sealed record ProposalRoundDto(int Number, ProposalActor By, ProposalAction Action, ProposalTerms? Terms, string? Text,
    [property: JsonConverter(typeof(UtcInstantConverter))] DateTimeOffset CreatedAt);

/// <summary>The counterpart's e-mail. Present only in the <c>Accepted</c> state; the parties contract outside the platform.</summary>
public sealed record ProposalContact(string? Email);

public sealed record ProposalView(Guid Id, ProposalKind Kind, ProposalStatus Status, ProposalTerms Terms,
    PractitionerRef Practitioner, ClientRef Client, string? CompanyName, IReadOnlyList<ProposalRoundDto> Rounds, int Round,
    ProposalActor ViewerRole,
    [property: JsonConverter(typeof(UtcInstantNullableConverter))] DateTimeOffset? ExpiresAt,
    bool CanAccept, bool CanCounter, bool CanReject, bool CanWithdraw, ProposalContact? Contact = null);

public sealed record ProposalSummary(Guid Id, ProposalKind Kind, ProposalStatus Status, ProposalTerms Terms,
    PractitionerRef Practitioner, ClientRef Client, string? CompanyName, int Round, bool YourTurn,
    [property: JsonConverter(typeof(UtcInstantConverter))] DateTimeOffset LastActionAt,
    [property: JsonConverter(typeof(UtcInstantNullableConverter))] DateTimeOffset? ExpiresAt);
