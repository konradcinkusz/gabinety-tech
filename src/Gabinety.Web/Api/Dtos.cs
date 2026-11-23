namespace Gabinety.Web.Api;

// The web host's own copies of the contracts it reads (BLAZOR-WEB-APP section 4 and 5, P3): the contract is HTTP, so this project does not reference Gabinety.Contracts (SPEC 3.2:
// the web references the kernel and nothing else). Only the fields the pages use are copied; an extra field in an answer is ignored. A test in tests/Gabinety.Web.Tests serializes the
// real Contracts types with the services' own JSON options and reads them into these records (and back), so a renamed field is a failing test and not a blank column.
// Strings stand for enums (badges, statuses): a value this copy does not know still renders, as "unknown", instead of failing the whole page.

public sealed record MoneyDto(long AmountMinor, string Currency);

public sealed record CategoryDto(string Slug, string NamePl);

public sealed record TaxonomyDto(string Version, IReadOnlyList<CategoryDto> Problems, IReadOnlyList<CategoryDto> Techs);

public sealed record RatingDto(double Average, int Count);

/// <summary>
/// A list card (CONTRACTS 3.2). <c>TechSlugs</c> (the union over the active offerings, at most 6) and <c>NextFreeSlotUtc</c> (start of the earliest bookable slot in the next 14 days) are
/// wave 3 additions: absent from an older Directory, so they default to none.
/// </summary>
public sealed record PractitionerCardDto(Guid Id, string Slug, string DisplayName, string Headline, string? GithubLogin, IReadOnlyList<string> Badges,
    RatingDto? Rating, bool IsNew, MoneyDto FromPrice, IReadOnlyList<string> ProblemSlugs, IReadOnlyList<int> Durations,
    IReadOnlyList<string>? TechSlugs = null, DateTimeOffset? NextFreeSlotUtc = null, ContractAvailabilityDto? Contract = null);

/// <summary>What a practitioner advertises for contract work (ADR proposal 0013). Absent means "not available"; <c>AvailableFrom</c> absent means available now.</summary>
public sealed record ContractAvailabilityDto(DateOnly? AvailableFrom, int HoursPerWeek, MoneyDto? Rate);

/// <summary>A cursor page (CONTRACTS 0); <c>NextCursor</c> is absent on the last page.</summary>
public sealed record PageDto<T>(IReadOnlyList<T> Items, string? NextCursor);

public sealed record SitemapCategoryDto(string Path, int PractitionerCount, bool Indexable);

public sealed record SitemapPractitionerDto(string Slug, DateTimeOffset LastModified);

/// <summary><c>Practitioners</c> is optional so a caller that only needs the categories (the list pages) is unaffected by it.</summary>
public sealed record SitemapDto(IReadOnlyList<SitemapCategoryDto> Categories, IReadOnlyList<SitemapPractitionerDto>? Practitioners = null);

public sealed record OfferingDto(Guid Id, string Title, string Description, int DurationMinutes, MoneyDto Price, string ProblemSlug, IReadOnlyList<string> TechSlugs);

/// <summary>The public profile (CONTRACTS 3.2): the card's fields and the extras side by side (flat on the wire).</summary>
public sealed record PractitionerProfileDto(Guid Id, string Slug, string DisplayName, string Headline, string? GithubLogin, IReadOnlyList<string> Badges, RatingDto? Rating, bool IsNew,
    MoneyDto FromPrice, string Bio, string? PortfolioUrl, IReadOnlyList<OfferingDto> Offerings, int CompletedCount, DateTimeOffset MemberSince, ContractAvailabilityDto? Contract = null);

public sealed record RankingWeightsDto(double Rating, double Reliability, double Availability, double Completeness);

public sealed record RankingConstantsDto(int M, double Mu, int MinReviewsToShowRating, int SlotsForFullAvailability);

public sealed record RankingRulesDto(string Version, DateTimeOffset EffectiveFrom, RankingWeightsDto Weights, RankingConstantsDto Constants);

public sealed record SlotDto(DateTimeOffset StartUtc, DateTimeOffset EndUtc);

public sealed record SlotsDto(IReadOnlyList<SlotDto> Slots);

public sealed record PractitionerRefDto(Guid Id, string DisplayName, string Slug);

public sealed record ClientRefDto(string DisplayName);

/// <summary>The part of <c>BookingView</c> (CONTRACTS 4.4) the list shows. <c>Status</c> is the wire name (<c>PendingPayment</c>, <c>Confirmed</c>, ...).</summary>
public sealed record BookingViewDto(Guid Id, string Status, DateTimeOffset StartUtc, DateTimeOffset EndUtc, int DurationMinutes, string OfferingTitle,
    MoneyDto Price, PractitionerRefDto Practitioner, ClientRefDto? Client);

public sealed record ReplyDto(string Text, DateTimeOffset RepliedAt);

public sealed record ReviewDto(Guid Id, int Rating, string Text, DateTimeOffset CreatedAt, ReplyDto? Reply);

/// <summary>The gateway state of the Assistant (CONTRACTS 6): <c>Ready | Fake | NotAuthenticated | Unavailable | Disabled</c>. A page keys off this, not off the health verdict.</summary>
public sealed record AssistantStatusDto(string State, IReadOnlyList<string> Features);

// ---- the booking flow and the client area (CONTRACTS 3.2, 4.2, 4.4, 5, 6)

public sealed record BriefDto(string Text, string? ProblemSlug, bool AiAssisted);

public sealed record CreateBookingRequestDto(Guid OfferingId, [property: System.Text.Json.Serialization.JsonConverter(typeof(UtcInstantWriter))] DateTimeOffset StartUtc, BriefDto Brief);

/// <summary>Body of <c>POST /api/v1/bookings/{id}/reschedule</c> (CONTRACTS 4.2): the new start, a UTC instant written with <c>Z</c>.</summary>
public sealed record RescheduleRequestDto([property: System.Text.Json.Serialization.JsonConverter(typeof(UtcInstantWriter))] DateTimeOffset StartUtc);

/// <summary>The service insists on a UTC instant written with <c>Z</c> (CONTRACTS 0): <c>2026-12-08T11:00:00Z</c>, never <c>+00:00</c> or a local offset.</summary>
public sealed class UtcInstantWriter : System.Text.Json.Serialization.JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) => reader.GetDateTimeOffset();

    public override void Write(System.Text.Json.Utf8JsonWriter writer, DateTimeOffset value, System.Text.Json.JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary><c>CheckoutUrl</c> is where the browser goes next: the mock checkout of this site, or the payment provider's page (Stripe Checkout) which returns to the site afterwards.</summary>
public sealed record CreateBookingResponseDto(Guid BookingId, string Status, DateTimeOffset HoldExpiresAt, string CheckoutUrl);

public sealed record PaymentViewDto(string Status, MoneyDto Refunded);

/// <summary>
/// <c>BookingView</c> (CONTRACTS 4.4) as the detail, the checkout and the confirmation read it.
/// </summary>
public sealed record BookingDetailDto(Guid Id, string Status, string? StatusReason, DateTimeOffset StartUtc, DateTimeOffset EndUtc, int DurationMinutes, string OfferingTitle, MoneyDto Price,
    PractitionerRefDto Practitioner, ClientRefDto? Client, BriefDto? Brief, string? MeetingUrl, DateTimeOffset? HoldExpiresAt, PaymentViewDto Payment, MoneyDto? Fee, MoneyDto? Payout,
    bool CanCancel, bool CanDispute, bool CanReportNoShow, bool CanReview, CancelOutcomeDto? CancelOutcome = null, bool CanReschedule = false);

/// <summary><c>cancelOutcome</c> (CONTRACTS 4.4, 9.2): what cancelling would do for the viewer right now, computed by the service on its clock. Present only when <c>canCancel</c>. <c>Outcome</c> is <c>full_refund</c>, <c>partial</c> or <c>no_refund</c>; <c>Refund</c> is what the client gets back.</summary>
public sealed record CancelOutcomeDto(string Outcome, MoneyDto Refund);

/// <summary><c>POST /api/v1/assistant/brief</c> (CONTRACTS 6). Only a suggestion: it is shown, never applied by itself.</summary>
public sealed record BriefSuggestionDto(string ProblemSlug, IReadOnlyList<string> TechSlugs, string Summary, IReadOnlyList<string> ClarifyingQuestions, int SuggestedDurationMinutes, string Confidence);

public sealed record CreateReviewResponseDto(Guid ReviewId, string Status);

public sealed record MeDto(string UserId, Guid? PractitionerId, string? PractitionerStatus, bool IsAdmin, bool IsModerator);

/// <summary>One switch of <c>/api/v1/me/notification-preferences</c> (CONTRACTS 4.2): <c>Kind</c> is <c>Reminder24h</c>, <c>Reminder1h</c> or <c>ReviewRequest</c>. The transactional kinds are never listed.</summary>
public sealed record NotificationPreferenceDto(string Kind, bool Enabled);
public sealed record NotificationPreferencesDto(IReadOnlyList<NotificationPreferenceDto> Items);

// ---- the practitioner's own area (CONTRACTS 3.3, 3.5, 4.2; docs/operations/PAYMENTS-GO-LIVE.md)

/// <summary><c>GET /api/v1/me/practitioner</c>; <c>Status</c> is <c>Draft | Submitted | Approved | Rejected | Suspended</c>.</summary>
public sealed record PractitionerPrivateDto(Guid Id, string Slug, string Status, string? StatusReason, string DisplayName, string Headline, string Bio, string? PortfolioUrl,
    string? MeetingUrlDefault, string? Nip, bool EmailVerified, bool GithubLinked, bool NipValid, string? GithubLogin);

public sealed record CreatePractitionerBody(string DisplayName);

/// <summary>An empty optional field is omitted, not sent as null (CONTRACTS 0): the attribute is on the body, not on the shared options.</summary>
public sealed record UpdatePractitionerBody(string DisplayName, string Headline, string Bio,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? PortfolioUrl,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? MeetingUrlDefault,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Nip);

public sealed record OfferingPrivateDto(Guid Id, string Title, string Description, int DurationMinutes, MoneyDto Price, string ProblemSlug, IReadOnlyList<string> TechSlugs, bool IsActive, string? MeetingUrl);

public sealed record OfferingsDto(IReadOnlyList<OfferingPrivateDto> Items);

public sealed record OfferingBody(string Title, string Description, int DurationMinutes, long PriceMinor, string ProblemSlug, IReadOnlyList<string> TechSlugs,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? MeetingUrl);

public sealed record GithubLinkBody(string GithubLogin);

public sealed record GithubChallengeDto(string Code);

public sealed record AvailabilityRuleDto(int IsoWeekday, string StartLocal, string EndLocal);

public sealed record AvailabilityExceptionDto(string DateLocal);

public sealed record AvailabilityDto(IReadOnlyList<AvailabilityRuleDto> Rules, IReadOnlyList<AvailabilityExceptionDto> Exceptions);

/// <summary><c>GET /api/v1/me/payments/account</c>: <c>State</c> is <c>none | pending | ready</c>. A provider with no connected accounts (the mock) answers 404.</summary>
public sealed record PaymentAccountDto(string Provider, string State);

/// <summary><c>POST /api/v1/me/payments/account/onboarding</c>: <c>OnboardingUrl</c> is single use and short lived; it is never stored or logged.</summary>
public sealed record PaymentOnboardingDto(string Provider, string State, string? OnboardingUrl);

/// <summary>
/// <c>GET /api/v1/me/earnings</c> (CONTRACTS 4.2, T-11.15): the Warsaw-day range answered (both days inclusive) and its four sums in grosz, plus the number of bookings behind the first three.
/// The default range is the current Warsaw month, which the service decides; the page passes dates only for a month it was asked for.
/// </summary>
public sealed record EarningsDto(DateOnly From, DateOnly To, MoneyDto PaidOut, MoneyDto Pending, MoneyDto Disputed, MoneyDto Fees, int Count);

// ---- contract availability and collaboration proposals (ADR proposal 0013)

/// <summary><c>GET/PUT /api/v1/me/practitioner/contract-availability</c>. <c>Status</c> is <c>not_available | available_now | available_from</c>. Null fields are sent as null: the service reads them as "none".</summary>
public sealed record ContractAvailabilitySettingsDto(string Status, DateOnly? AvailableFrom, int? HoursPerWeek, long? RateMinor);

/// <summary>The negotiable terms, in the wire shape of the service. A short-term proposal has <c>TotalHours</c>; a long-term one <c>HoursPerWeek</c> and <c>DurationMonths</c>.</summary>
public sealed record ProposalTermsDto(MoneyDto Rate, int? HoursPerWeek, int? TotalHours, int? DurationMonths, DateOnly StartDate);

/// <summary><c>By</c> is <c>Client | Practitioner | System</c>; <c>Action</c> is <c>Proposed | Countered | Accepted | Rejected | Withdrawn | Expired</c>.</summary>
public sealed record ProposalRoundDto(int Number, string By, string Action, ProposalTermsDto? Terms, string? Text, DateTimeOffset CreatedAt);

/// <summary>The counterpart's address: present only once the proposal is accepted; <c>Email</c> is null when none is known right now.</summary>
public sealed record ProposalContactDto(string? Email);

/// <summary><c>Kind</c> is <c>ShortTerm | LongTerm</c>; <c>Status</c> is <c>AwaitingPractitioner | AwaitingClient | Accepted | Rejected | Withdrawn | Expired</c>; <c>ViewerRole</c> is <c>Client | Practitioner</c>.</summary>
public sealed record ProposalViewDto(Guid Id, string Kind, string Status, ProposalTermsDto Terms, PractitionerRefDto Practitioner, ClientRefDto Client, string? CompanyName,
    IReadOnlyList<ProposalRoundDto> Rounds, int Round, string ViewerRole, DateTimeOffset? ExpiresAt, bool CanAccept, bool CanCounter, bool CanReject, bool CanWithdraw, ProposalContactDto? Contact);

public sealed record ProposalSummaryDto(Guid Id, string Kind, string Status, ProposalTermsDto Terms, PractitionerRefDto Practitioner, ClientRefDto Client, string? CompanyName,
    int Round, bool YourTurn, DateTimeOffset LastActionAt, DateTimeOffset? ExpiresAt);

/// <summary>An empty company name is omitted, not sent as null (CONTRACTS 0).</summary>
public sealed record CreateProposalBody(Guid PractitionerId, string Kind, ProposalTermsDto Terms, string Text,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? CompanyName);

public sealed record CounterProposalBody(int Round, ProposalTermsDto Terms,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Text);

public sealed record ProposalRoundBody(int Round);
