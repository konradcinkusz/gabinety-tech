using System.Globalization;
using System.Text.RegularExpressions;
using Gabinety.Web.Api;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Proposals;

public enum RateProblem { None, Format, Range }

/// <summary>An hourly rate as a person types it ("150", "150,50", a dot works too) and as integer grosz (SPEC 5 rule 3). Read with integer arithmetic only, never through a float.</summary>
public static partial class RateInput
{
    public const long MinGrosz = 2_000, MaxGrosz = 100_000;

    [GeneratedRegex(@"^(\d{1,7})(?:[.,](\d{1,2}))?$")] private static partial Regex Shape();

    public static (RateProblem Problem, long Grosz) Parse(string? input)
    {
        var m = Shape().Match((input ?? "").Trim());
        if (!m.Success) return (RateProblem.Format, 0);
        var grosz = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 100 + (m.Groups[2].Success ? long.Parse(m.Groups[2].Value.PadRight(2, '0'), CultureInfo.InvariantCulture) : 0);
        return grosz is < MinGrosz or > MaxGrosz ? (RateProblem.Range, grosz) : (RateProblem.None, grosz);
    }

    /// <summary>The value for a rate field: <c>150</c> or <c>150,50</c>.</summary>
    public static string ToInput(long grosz) => grosz % 100 == 0 ? (grosz / 100).ToString(CultureInfo.InvariantCulture) : $"{grosz / 100},{grosz % 100:00}";
}

/// <summary>What the form posted for the terms, as typed (strings only), so a refused form can be shown again as it was.</summary>
public sealed record TermsFields(string Rate, string Hours, string Months, string Start)
{
    public static TermsFields Empty { get; } = new("", "", "", "");
}

/// <summary>
/// Our own check of the terms form (ADR proposal 0013), run first so every problem is shown at once. The service stays the authority: these bounds mirror its own (CONTRACTS section 9 to be updated) and a
/// change there is a failing test here, not a silent difference. Field ids are the ones of the form: <c>rate</c>, <c>hoursPerWeek</c> or <c>totalHours</c>, <c>durationMonths</c>, <c>startDate</c>.
/// </summary>
public static class ProposalInputs
{
    public const int MinHoursPerWeek = 1, MaxHoursPerWeek = 60, MinTotalHours = 1, MaxTotalHours = 400, MinMonths = 1, MaxMonths = 36, MaxStartMonthsAhead = 24, TextMin = 20, TextMax = 1000, CompanyMax = 120;

    public static string HoursField(bool longTerm) => longTerm ? "hoursPerWeek" : "totalHours";

    /// <param name="longTerm">Long term: hours a week and months are required. Short term: total hours are required and months are optional.</param>
    public static (ProposalTermsDto? Terms, Dictionary<string, string> Errors) ParseTerms(bool longTerm, TermsFields f, DateOnly today)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var (rateProblem, grosz) = RateInput.Parse(f.Rate);
        if (rateProblem != RateProblem.None) errors["rate"] = ProposalsText.Errors.Rate;

        int? hours = Whole(f.Hours, longTerm ? MinHoursPerWeek : MinTotalHours, longTerm ? MaxHoursPerWeek : MaxTotalHours);
        if (hours is null) errors[HoursField(longTerm)] = longTerm ? ProposalsText.Errors.HoursPerWeek : ProposalsText.Errors.TotalHours;

        int? months = null;
        if (f.Months.Length > 0 || longTerm)
        {
            months = Whole(f.Months, MinMonths, MaxMonths);
            if (months is null) errors["durationMonths"] = ProposalsText.Errors.Duration;
        }

        DateOnly start = default;
        if (!DateOnly.TryParseExact(f.Start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start) || start < today || start > today.AddMonths(MaxStartMonthsAhead))
            errors["startDate"] = ProposalsText.Errors.Start;

        if (errors.Count > 0) return (null, errors);
        return (new ProposalTermsDto(new MoneyDto(grosz, "PLN"), longTerm ? hours : null, longTerm ? null : hours, months, start), errors);
    }

    static int? Whole(string text, int min, int max) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max ? n : null;

    /// <summary>The message: 20 to 1000 characters when <paramref name="required"/>, otherwise at most 1000 (an empty note is fine).</summary>
    public static string? TextProblem(string? text, bool required)
    {
        var n = (text ?? "").Trim().Length;
        if (required && n < TextMin) return ProposalsText.Errors.TextShort;
        return n > TextMax ? ProposalsText.Errors.TextLong : null;
    }

    public static string? CompanyProblem(string? company) => (company ?? "").Trim().Length > CompanyMax ? ProposalsText.Errors.CompanyLong : null;

    /// <summary>Which of the free-text fields a "contact details" refusal belongs to: the message and the company name are both candidates; the service does not say.</summary>
    public static IReadOnlyList<string> FreeTextFields(bool hasCompany) => hasCompany ? ["text", "companyName"] : ["text"];
}
