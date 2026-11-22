using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Gabinety.Contracts.Common;

namespace Gabinety.Contracts.Directory;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PractitionerStatus { Draft, Submitted, Approved, Rejected, Suspended }

[JsonConverter(typeof(Gabinety.Contracts.Common.SnakeCaseLowerEnumConverter))]
public enum Badge { Email, Github, Nip, Approved }

public sealed record CategoryDto(string Slug, string NamePl);
public sealed record TaxonomyDto(string Version, IReadOnlyList<CategoryDto> Problems, IReadOnlyList<CategoryDto> Techs);

public sealed record RatingDto(double Average, int Count);

public sealed record PractitionerCard(Guid Id, string Slug, string DisplayName, string Headline, string? GithubLogin,
    IReadOnlyList<Badge> Badges, RatingDto? Rating, bool IsNew, MoneyDto FromPrice,
    IReadOnlyList<string> ProblemSlugs, IReadOnlyList<int> Durations);

public sealed record Offering(Guid Id, string Title, string Description, int DurationMinutes, MoneyDto Price,
    string ProblemSlug, IReadOnlyList<string> TechSlugs);

public sealed record OfferingPrivate(Guid Id, string Title, string Description, int DurationMinutes, MoneyDto Price,
    string ProblemSlug, IReadOnlyList<string> TechSlugs, bool IsActive, string? MeetingUrl);

public sealed record PractitionerProfile(PractitionerCard Card, string Bio, string? PortfolioUrl,
    IReadOnlyList<Offering> Offerings, int CompletedCount, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantConverter))] DateTimeOffset MemberSince);

public sealed record SitemapPractitioner(string Slug, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantConverter))] DateTimeOffset LastModified);
public sealed record SitemapCategory(string Path, int PractitionerCount, bool Indexable);
public sealed record SitemapDto(IReadOnlyList<SitemapPractitioner> Practitioners, IReadOnlyList<SitemapCategory> Categories);

public sealed record RankingWeights(double Rating, double Reliability, double Availability, double Completeness);
public sealed record RankingConstants(int M, double Mu, int MinReviewsToShowRating, int SlotsForFullAvailability);
public sealed record RankingRulesDto(string Version, [property: System.Text.Json.Serialization.JsonConverter(typeof(Gabinety.Contracts.Common.UtcInstantConverter))] DateTimeOffset EffectiveFrom, RankingWeights Weights, RankingConstants Constants);

public sealed record MeDto(string UserId, Guid? PractitionerId, PractitionerStatus? PractitionerStatus, bool IsAdmin, bool IsModerator);

public sealed record CreatePractitionerRequest([property: Required, StringLength(80, MinimumLength = 2)] string DisplayName,
    [property: StringLength(60, MinimumLength = 3)] string? Slug = null);

public sealed record UpdatePractitionerRequest([property: Required, StringLength(80, MinimumLength = 2)] string DisplayName,
    [property: StringLength(120)] string Headline, [property: StringLength(2000)] string Bio,
    string? PortfolioUrl = null, string? MeetingUrlDefault = null, string? Nip = null);

public sealed record PractitionerPrivate(Guid Id, string Slug, PractitionerStatus Status, string? StatusReason, string DisplayName,
    string Headline, string Bio, string? PortfolioUrl, string? MeetingUrlDefault, string? Nip,
    bool EmailVerified, bool GithubLinked, bool NipValid, string? GithubLogin);

public sealed record OfferingRequest([property: Required, StringLength(120)] string Title, [property: StringLength(1000)] string Description,
    int DurationMinutes, long PriceMinor, [property: Required] string ProblemSlug, IReadOnlyList<string> TechSlugs, string? MeetingUrl = null);

public sealed record SubmitIncomplete(IReadOnlyList<string> UnmetItems);
