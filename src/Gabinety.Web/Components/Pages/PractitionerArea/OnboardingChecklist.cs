using Gabinety.Web.Api;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.PractitionerArea;

public enum ChecklistKey { Headline, Bio, Offering, Github, Nip, Portfolio, Email }

public sealed record ChecklistItem(ChecklistKey Key, bool Done)
{
    public string Title => Key switch
    {
        ChecklistKey.Headline => PlPractitioner.Checklist.Headline, ChecklistKey.Bio => PlPractitioner.Checklist.Bio, ChecklistKey.Offering => PlPractitioner.Checklist.Offering,
        ChecklistKey.Github => PlPractitioner.Checklist.Github, ChecklistKey.Nip => PlPractitioner.Checklist.Nip, ChecklistKey.Portfolio => PlPractitioner.Checklist.Portfolio,
        _ => PlPractitioner.Checklist.Email,
    };

    public string Help => Key switch
    {
        ChecklistKey.Headline or ChecklistKey.Bio => PlPractitioner.Checklist.HelpProfile, ChecklistKey.Offering => PlPractitioner.Checklist.HelpOffers,
        ChecklistKey.Github => PlPractitioner.Checklist.HelpGithub, ChecklistKey.Nip => PlPractitioner.Checklist.HelpNip, ChecklistKey.Portfolio => PlPractitioner.Checklist.HelpPortfolio,
        _ => PlPractitioner.Checklist.HelpEmail,
    };

    /// <summary>Where the missing step is done; null when it is done on the dashboard itself (GitHub) or outside the product (the e-mail link).</summary>
    public string? Href => Key switch
    {
        ChecklistKey.Headline or ChecklistKey.Bio or ChecklistKey.Nip or ChecklistKey.Portfolio => PractitionerPaths.Profile,
        ChecklistKey.Offering => PractitionerPaths.Offers,
        _ => null,
    };
}

/// <summary>The submit checklist of CONTRACTS 3.5, in its order. It reads only what the API returned (the profile and the offers), never form state, so a step is ticked only when the service agrees.</summary>
public static class OnboardingChecklist
{
    public const int BioMinimum = 200;

    public static IReadOnlyList<ChecklistItem> Build(PractitionerPrivateDto p, IReadOnlyList<OfferingPrivateDto> offers) =>
    [
        new(ChecklistKey.Headline, p.Headline.Trim().Length > 0),
        new(ChecklistKey.Bio, p.Bio.Trim().Length >= BioMinimum),
        new(ChecklistKey.Offering, offers.Any(o => o.IsActive)),
        new(ChecklistKey.Github, p.GithubLinked),
        new(ChecklistKey.Nip, p.NipValid),
        new(ChecklistKey.Portfolio, !string.IsNullOrEmpty(p.PortfolioUrl)),
        new(ChecklistKey.Email, p.EmailVerified),
    ];

    public static int Remaining(IReadOnlyList<ChecklistItem> items) => items.Count(i => !i.Done);

    /// <summary>The items a <c>422 submit_incomplete</c> names (the Directory's own words), in the order of the checklist.</summary>
    public static IReadOnlyList<ChecklistKey> FromServer(IEnumerable<string> unmet)
    {
        static ChecklistKey? Map(string code) => code switch
        {
            "headline" => ChecklistKey.Headline, "bio" => ChecklistKey.Bio, "offering" => ChecklistKey.Offering, "github_linked" => ChecklistKey.Github,
            "nip_valid" => ChecklistKey.Nip, "portfolioUrl" => ChecklistKey.Portfolio, "email_verified" => ChecklistKey.Email, _ => null,
        };
        return [.. unmet.Select(Map).OfType<ChecklistKey>().Distinct().Order()];
    }

    /// <summary>Only a draft or a rejected profile can be sent (CONTRACTS 3.4); everything else has no submit button.</summary>
    public static bool CanBeSubmitted(string status) => status is "Draft" or "Rejected";
}

/// <summary>The routes of the area, in one place.</summary>
public static class PractitionerPaths
{
    public const string Home = "/gabinet-programisty", Profile = "/gabinet-programisty/profil", Offers = "/gabinet-programisty/oferty", Availability = "/gabinet-programisty/dostepnosc", Payments = "/gabinet-programisty/platnosci";
}
