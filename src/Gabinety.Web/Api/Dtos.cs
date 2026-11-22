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

public sealed record SitemapDto(IReadOnlyList<SitemapCategoryDto> Categories);

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

public sealed record OfferingDto(Guid Id, string Title, string Description, int DurationMinutes, MoneyDto Price, string ProblemSlug);

/// <summary>The part of <c>PractitionerProfile</c> (CONTRACTS 3.2) the booking form needs: whose offering it is.</summary>
public sealed record PractitionerProfileDto(PractitionerCardDto Card, IReadOnlyList<OfferingDto> Offerings);

public sealed record BriefDto(string Text, string? ProblemSlug, bool AiAssisted);

public sealed record CreateBookingRequestDto(Guid OfferingId, DateTimeOffset StartUtc, BriefDto Brief);

/// <summary><c>CheckoutUrl</c> is where the browser goes next: the mock checkout of this site, or the payment provider's page (Stripe Checkout) which returns to the site afterwards.</summary>
public sealed record CreateBookingResponseDto(Guid BookingId, string Status, DateTimeOffset HoldExpiresAt, string CheckoutUrl);

public sealed record PaymentViewDto(string Status, MoneyDto Refunded);

/// <summary>
/// <c>BookingView</c> (CONTRACTS 4.4) as the detail, the checkout and the confirmation read it. <c>CancelOutcome</c> is the outcome the cancellation of THIS booking would have (F-11); the
/// Booking service adds it to the detail read, until then it is absent and the page says the general rule. It is kept as the raw JSON so a shape this copy does not know yet does not fail the page:
/// <see cref="Gabinety.Web.Components.Pages.Bookings.CancelOutcome"/> reads it.
/// </summary>
public sealed record BookingDetailDto(Guid Id, string Status, string? StatusReason, DateTimeOffset StartUtc, DateTimeOffset EndUtc, int DurationMinutes, string OfferingTitle, MoneyDto Price,
    PractitionerRefDto Practitioner, ClientRefDto? Client, BriefDto? Brief, string? MeetingUrl, DateTimeOffset? HoldExpiresAt, PaymentViewDto Payment, MoneyDto? Fee, MoneyDto? Payout,
    bool CanCancel, bool CanDispute, bool CanReportNoShow, bool CanReview, System.Text.Json.JsonElement? CancelOutcome = null);

/// <summary><c>POST /api/v1/assistant/brief</c> (CONTRACTS 6). Only a suggestion: it is shown, never applied by itself.</summary>
public sealed record BriefSuggestionDto(string ProblemSlug, IReadOnlyList<string> TechSlugs, string Summary, IReadOnlyList<string> ClarifyingQuestions, int SuggestedDurationMinutes, string Confidence);

public sealed record CreateReviewResponseDto(Guid ReviewId, string Status);

public sealed record MeDto(string UserId, Guid? PractitionerId, string? PractitionerStatus, bool IsAdmin, bool IsModerator);
