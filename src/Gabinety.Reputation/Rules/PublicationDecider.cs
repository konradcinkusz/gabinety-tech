using Gabinety.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Gabinety.Reputation.Rules;

public abstract record PublicationDecision
{
    private PublicationDecision() { }
    /// <summary>All deterministic rules passed; the review goes on to triage (not in this task).</summary>
    public sealed record Pass : PublicationDecision;
    /// <summary>One or more rules fired; reasons are in rule-registration order.</summary>
    public sealed record Held(IReadOnlyList<string> Reasons) : PublicationDecision;
    /// <summary>Validation failed (20 to 2000 characters after trimming); no rule ran.</summary>
    public sealed record Invalid(IReadOnlyList<string> Errors) : PublicationDecision;
}

/// <summary>CONTRACTS 5.3 step 1: validate, then run every rule and accumulate the reasons. Pure.</summary>
public sealed class PublicationDecider(IEnumerable<IReviewRule> rules)
{
    readonly IReviewRule[] _rules = [.. rules];
    readonly ReviewValidator _validator = new();

    public PublicationDecision Decide(string? text)
    {
        var errors = _validator.Validate(text);
        if (errors.Count > 0) return new PublicationDecision.Invalid(errors);
        var input = ReviewText.From(text);
        var reasons = _rules.Select(r => r.Check(input)).OfType<string>().ToList();
        return reasons.Count == 0 ? new PublicationDecision.Pass() : new PublicationDecision.Held(reasons);
    }
}

public sealed class ReviewsOptions
{
    public string[]? CodeHosts { get; set; }
    public string[]? BlockedTerms { get; set; }
    public int WindowDays { get; set; } = 14; // not used by the rules
}

/// <summary>Registers the options (section Reviews), the rules in their documented order, and the decider.</summary>
public sealed class RulesFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.Configure<ReviewsOptions>(builder.Configuration.GetSection("Reviews"));
        // Order matters: it is the order of the reasons.
        builder.Services.AddSingleton<IReviewRule>(sp => new ContainsLinkRule(sp.GetRequiredService<IOptions<ReviewsOptions>>().Value.CodeHosts));
        builder.Services.AddSingleton<IReviewRule, ContactDetailsRule>();
        builder.Services.AddSingleton<IReviewRule>(sp => new BlockedTermRule(sp.GetRequiredService<IOptions<ReviewsOptions>>().Value.BlockedTerms));
        builder.Services.AddSingleton<IReviewRule, RepetitionRule>();
        builder.Services.AddSingleton<IReviewRule, TooShortAfterTrimRule>();
        builder.Services.AddSingleton<PublicationDecider>();
    }

    public void Map(IEndpointRouteBuilder endpoints) { }
}
