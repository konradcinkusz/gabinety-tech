namespace Gabinety.Web.Api;

// The admin shapes the web host reads (CONTRACTS 3.4, 4.3, 5.2, T-10.03). They are copies like the rest of Dtos.cs (the contract is HTTP); the services declare them beside their endpoints, not in
// Gabinety.Contracts yet, so ContractCopiesTests pins the ones that ARE in Contracts (SuspiciousPairDto) and AdminClientTests pins the wire spelling of the rest. Statuses are strings: a value this copy does
// not know renders as "unknown" instead of failing the page. Free text (bio, reason, dispute text, review text, notes) is shown to administrators only and never logged.

public sealed record AdminPractitionerRowDto(Guid Id, string Slug, string DisplayName, string Headline, string Status, string? StatusReason, bool GithubLinked, string? GithubLogin,
    bool NipValid, DateTimeOffset CreatedAt, DateTimeOffset? SubmittedAt);

public sealed record AdminPractitionerDetailDto(Guid Id, string Slug, string DisplayName, string Headline, string Bio, string Status, string? StatusReason, bool GithubLinked,
    string? GithubLogin, string? Nip, bool NipValid, string? PortfolioUrl, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? SubmittedAt, DateTimeOffset? ApprovedAt);

public sealed record AdminVerificationEventDto(Guid Id, string Kind, string ActorUserId, string? Note, DateTimeOffset At);

public sealed record AdminVerificationEventsDto(IReadOnlyList<AdminVerificationEventDto> Items);

public sealed record AdminDisputeDto(Guid BookingId, string ClientUserId, string PractitionerUserId, string? Kind, string? Text, DateTimeOffset? OpenedAt, DateTimeOffset? ResolvedAt,
    string? Resolution, string? Note);

/// <summary>The part of <c>BookingView</c> (CONTRACTS 4.4) a dispute card shows: what was booked, for how much, the payment. The party names are empty until Directory can look them up (the booking feature says so).</summary>
public sealed record AdminBookingDto(Guid Id, string Status, DateTimeOffset StartUtc, string OfferingTitle, MoneyDto Price, PractitionerRefDto Practitioner, ClientRefDto? Client, PaymentViewDto Payment);


public sealed record AdminReviewDto(Guid Id, Guid PractitionerId, int Rating, string? Text, string Status, IReadOnlyList<string>? HoldReasons, string? TriageFlag, string? TriageModelId,
    DateTimeOffset? CreatedAt, DateTimeOffset? PublishedAt, string? ModeratedBy, string? ModerationNote);

public sealed record SuspiciousPairDto(string PairKey, string ClientUserId, Guid PractitionerId, int Bookings, int Reviews);

/// <summary>One data row of the ledger CSV (<c>booking_id,kind,amount_minor,currency,created_at</c>).</summary>
public sealed record LedgerRowDto(Guid BookingId, string Kind, long AmountMinor, string Currency, DateTimeOffset CreatedAt);

/// <summary>The first rows of a ledger export, and whether there were more.</summary>
public sealed record LedgerPreviewDto(IReadOnlyList<LedgerRowDto> Rows, bool HasMore);
