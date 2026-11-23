using System.Globalization;
using Gabinety.Web.Api;
using Gabinety.Web.Components.Shared;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Shared;

/// <summary>
/// How contract availability reads on a profile and on a card (ADR proposal 0013): "od zaraz", "za 3 dni", "za 2 tygodnie", "za 3 miesiące", and beyond six months "od stycznia 2027". Pure: it takes today's Warsaw
/// date, so the same page reads the same on every machine and a test needs no clock. A date that has passed (or none) is "now".
/// </summary>
public static class ContractAvailabilityFormat
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The phrase after "Dostępny do współpracy": <c>od zaraz</c>, <c>za 3 dni</c>, <c>za 2 tygodnie</c>, <c>za 3 miesiące</c> or <c>od stycznia 2027</c>.</summary>
    public static string Relative(DateOnly? from, DateOnly today)
    {
        if (from is not { } date || date <= today) return ProposalsText.Availability.PhraseNow;
        var days = date.DayNumber - today.DayNumber;
        if (days < 14) return days == 1 ? ProposalsText.Availability.Day1 : string.Format(Inv, ProposalsText.Availability.Days, days);
        if (days < 60)
        {
            var weeks = (int)Math.Round(days / 7.0, MidpointRounding.AwayFromZero); // 55 days is closer to eight weeks than to seven
            return string.Format(Inv, Few(weeks) ? ProposalsText.Availability.WeeksFew : ProposalsText.Availability.WeeksMany, weeks);
        }
        var months = (date.Year - today.Year) * 12 + date.Month - today.Month - (date.Day < today.Day ? 1 : 0);
        months = Math.Max(2, months);
        if (months <= 6) return string.Format(Inv, Few(months) ? ProposalsText.Availability.MonthsFew : ProposalsText.Availability.MonthsMany, months);
        return string.Format(Inv, ProposalsText.Availability.FromMonth, Pl.Format.MonthsGenitive[date.Month - 1], date.Year);
    }

    /// <summary>2 to 4 take the "few" form (and 22 to 24); the teens and 5 to 21 the "many" form.</summary>
    static bool Few(int n) => n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14;

    /// <summary>"Dostępny do współpracy od zaraz" or "... za 3 miesiące".</summary>
    public static string ProfileLine(ContractAvailabilityDto contract, DateOnly today) => Is(contract, today)
        ? ProposalsText.Availability.Now
        : string.Format(Inv, ProposalsText.Availability.From, Relative(contract.AvailableFrom, today));

    /// <summary>The short line of a card: "Dostępny do kontraktu" or "Kontrakt: za 3 miesiące".</summary>
    public static string CardLine(ContractAvailabilityDto contract, DateOnly today) => Is(contract, today)
        ? ProposalsText.Availability.CardNow
        : string.Format(Inv, ProposalsText.Availability.CardFrom, Relative(contract.AvailableFrom, today));

    public static string HoursLine(ContractAvailabilityDto contract) => string.Format(Inv, ProposalsText.Availability.Hours, contract.HoursPerWeek);

    /// <summary>"Orientacyjna stawka: 150,00 zł za godzinę", or null when none was given.</summary>
    public static string? RateLine(ContractAvailabilityDto contract) =>
        contract.Rate is { } rate ? string.Format(Inv, ProposalsText.Availability.Rate, Money.Pln(rate.AmountMinor)) : null;

    static bool Is(ContractAvailabilityDto contract, DateOnly today) => contract.AvailableFrom is not { } d || d <= today;
}
