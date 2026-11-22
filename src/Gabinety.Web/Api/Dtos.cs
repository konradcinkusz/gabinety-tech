namespace Gabinety.Web.Api;

// The web host's own copies of the contracts it reads (BLAZOR-WEB-APP section 4 and 5, P3): the contract is HTTP, so this project does not reference Gabinety.Contracts (SPEC 3.2:
// the web references the kernel and nothing else). Only the fields the pages use are copied; an extra field in an answer is ignored. A test in tests/Gabinety.Web.Tests serializes the
// real Contracts types with the services' own JSON options and reads them into these records (and back), so a renamed field is a failing test and not a blank column.
// Strings stand for enums (badges, statuses): a value this copy does not know still renders, as "unknown", instead of failing the whole page.

public sealed record MoneyDto(long AmountMinor, string Currency);

public sealed record CategoryDto(string Slug, string NamePl);

public sealed record TaxonomyDto(string Version, IReadOnlyList<CategoryDto> Problems, IReadOnlyList<CategoryDto> Techs);

public sealed record RatingDto(double Average, int Count);

public sealed record PractitionerCardDto(Guid Id, string Slug, string DisplayName, string Headline, string? GithubLogin, IReadOnlyList<string> Badges,
    RatingDto? Rating, bool IsNew, MoneyDto FromPrice, IReadOnlyList<string> ProblemSlugs, IReadOnlyList<int> Durations);

/// <summary>A cursor page (CONTRACTS 0); <c>NextCursor</c> is absent on the last page.</summary>
public sealed record PageDto<T>(IReadOnlyList<T> Items, string? NextCursor);

public sealed record SitemapCategoryDto(string Path, int PractitionerCount, bool Indexable);

public sealed record SitemapPractitionerDto(string Slug, DateTimeOffset LastModified);

/// <summary><c>Practitioners</c> is optional so a caller that only needs the categories (the list pages) is unaffected by it.</summary>
public sealed record SitemapDto(IReadOnlyList<SitemapCategoryDto> Categories, IReadOnlyList<SitemapPractitionerDto>? Practitioners = null);

public sealed record OfferingDto(Guid Id, string Title, string Description, int DurationMinutes, MoneyDto Price, string ProblemSlug, IReadOnlyList<string> TechSlugs);

/// <summary>The public profile (CONTRACTS 3.2): the card's fields and the extras side by side (flat on the wire).</summary>
public sealed record PractitionerProfileDto(Guid Id, string Slug, string DisplayName, string Headline, string? GithubLogin, IReadOnlyList<string> Badges, RatingDto? Rating, bool IsNew,
    MoneyDto FromPrice, string Bio, string? PortfolioUrl, IReadOnlyList<OfferingDto> Offerings, int CompletedCount, DateTimeOffset MemberSince);

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
    bool CanCancel, bool CanDispute, bool CanReportNoShow, bool CanReview, CancelOutcomeDto? CancelOutcome = null);

/// <summary><c>cancelOutcome</c> (CONTRACTS 4.4, 9.2): what cancelling would do for the viewer right now, computed by the service on its clock. Present only when <c>canCancel</c>. <c>Outcome</c> is <c>full_refund</c>, <c>partial</c> or <c>no_refund</c>; <c>Refund</c> is what the client gets back.</summary>
public sealed record CancelOutcomeDto(string Outcome, MoneyDto Refund);

/// <summary><c>POST /api/v1/assistant/brief</c> (CONTRACTS 6). Only a suggestion: it is shown, never applied by itself.</summary>
public sealed record BriefSuggestionDto(string ProblemSlug, IReadOnlyList<string> TechSlugs, string Summary, IReadOnlyList<string> ClarifyingQuestions, int SuggestedDurationMinutes, string Confidence);

public sealed record CreateReviewResponseDto(Guid ReviewId, string Status);

public sealed record MeDto(string UserId, Guid? PractitionerId, string? PractitionerStatus, bool IsAdmin, bool IsModerator);

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
