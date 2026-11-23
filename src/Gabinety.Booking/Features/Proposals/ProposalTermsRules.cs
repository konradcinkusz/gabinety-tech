using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;

namespace Gabinety.Booking.Features.Proposals;

/// <summary>
/// Validation and normalisation of the negotiable terms (ADR proposal 0013). The bounds are owner constants (CONTRACTS section 9 to be updated). A short-term proposal is a number of
/// hours; a long-term one is hours a week for a number of months. The fields that do not belong to the kind are dropped, so what is stored never contradicts the kind.
/// </summary>
public static class ProposalTermsRules
{
    public const long MinRateMinor = 2_000, MaxRateMinor = 100_000;
    public const int MinTotalHours = 1, MaxTotalHours = 400, MinHoursPerWeek = 1, MaxHoursPerWeek = 60, MinMonths = 1, MaxMonths = 36, MaxStartMonthsAhead = 24;

    /// <summary>Field name (camelCase, as the request spells it) to messages; empty when the terms are valid.</summary>
    public static Dictionary<string, string[]> Validate(ProposalKind kind, ProposalTerms? t, DateOnly today)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Enum.IsDefined(kind)) { errors["kind"] = ["ShortTerm or LongTerm"]; return errors; }
        if (t is null) { errors["terms"] = ["required"]; return errors; }

        if (t.Rate is null || t.Rate.AmountMinor is < MinRateMinor or > MaxRateMinor || t.Rate.Currency != "PLN")
            errors["terms.rate"] = [$"{MinRateMinor} to {MaxRateMinor} grosz per hour, in PLN"];
        if (t.StartDate < today) errors["terms.startDate"] = ["today or later"];
        else if (t.StartDate > today.AddMonths(MaxStartMonthsAhead)) errors["terms.startDate"] = [$"at most {MaxStartMonthsAhead} months ahead"];

        if (kind == ProposalKind.ShortTerm)
        {
            if (t.TotalHours is not { } h || h is < MinTotalHours or > MaxTotalHours) errors["terms.totalHours"] = [$"{MinTotalHours} to {MaxTotalHours} hours"];
            if (t.DurationMonths is { } m && m is < MinMonths or > MaxMonths) errors["terms.durationMonths"] = [$"{MinMonths} to {MaxMonths} months"];
        }
        else
        {
            if (t.HoursPerWeek is not { } h || h is < MinHoursPerWeek or > MaxHoursPerWeek) errors["terms.hoursPerWeek"] = [$"{MinHoursPerWeek} to {MaxHoursPerWeek} hours a week"];
            if (t.DurationMonths is not { } m || m is < MinMonths or > MaxMonths) errors["terms.durationMonths"] = [$"{MinMonths} to {MaxMonths} months"];
        }
        return errors;
    }

    /// <summary>The terms as stored for the kind: a short-term proposal has no hours a week, a long-term one no total hours.</summary>
    public static ProposalTerms Normalize(ProposalKind kind, ProposalTerms t) => kind == ProposalKind.ShortTerm
        ? new ProposalTerms(t.Rate, null, t.TotalHours, t.DurationMonths, t.StartDate)
        : new ProposalTerms(t.Rate, t.HoursPerWeek, null, t.DurationMonths, t.StartDate);

    public static ProposalTerms Of(Proposal p) => new(new MoneyDto(p.RateMinor), p.HoursPerWeek, p.TotalHours, p.DurationMonths, p.StartDate);

    public static void Apply(Proposal p, ProposalTerms t)
    {
        p.RateMinor = t.Rate.AmountMinor; p.HoursPerWeek = t.HoursPerWeek; p.TotalHours = t.TotalHours; p.DurationMonths = t.DurationMonths; p.StartDate = t.StartDate;
    }
}
