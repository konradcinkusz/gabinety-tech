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
