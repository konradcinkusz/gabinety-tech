using System.Globalization;
using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Proposals;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.PractitionerArea;

/// <summary>The contract availability form as typed (strings only), so a refused form is shown again as it was.</summary>
public sealed record ContractFields(string Status, string From, string Hours, string Rate)
{
    public static ContractFields Default { get; } = new(ContractInput.NotAvailable, "", "", "");
}

/// <summary>
/// The programmer's own contract availability form (ADR proposal 0013): our check first so every problem is shown at once; the service stays the authority and its bounds are mirrored here (a change there
/// is a failing test here). Field ids: <c>contractStatus</c>, <c>contractFrom</c>, <c>contractHours</c>, <c>contractRate</c>. "Not available" ignores and clears the other fields.
/// </summary>
public static class ContractInput
{
    public const string NotAvailable = "not_available", AvailableNow = "available_now", AvailableFrom = "available_from";
    public static readonly string[] Statuses = [NotAvailable, AvailableNow, AvailableFrom];
    public const int MinHours = 1, MaxHours = 60, MaxMonthsAhead = 24;
    public static readonly string[] FieldOrder = ["contractStatus", "contractFrom", "contractHours", "contractRate"];

    public static string StatusLabel(string status) => status switch
    {
        AvailableNow => ProposalsText.Contract.AvailableNow, AvailableFrom => ProposalsText.Contract.AvailableFrom, _ => ProposalsText.Contract.NotAvailable,
    };

    public static ContractFields From(ContractAvailabilitySettingsDto d) => new(
        Statuses.Contains(d.Status) ? d.Status : NotAvailable, d.AvailableFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
        d.HoursPerWeek?.ToString(CultureInfo.InvariantCulture) ?? "", d.RateMinor is { } r ? RateInput.ToInput(r) : "");

    public static (ContractAvailabilitySettingsDto? Body, Dictionary<string, string> Errors) Parse(ContractFields f, DateOnly today)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Statuses.Contains(f.Status)) { errors["contractStatus"] = ProposalsText.Contract.ErrStatus; return (null, errors); }
        if (f.Status == NotAvailable) return (new ContractAvailabilitySettingsDto(NotAvailable, null, null, null), errors);

        int? hours = int.TryParse(f.Hours, NumberStyles.None, CultureInfo.InvariantCulture, out var h) && h is >= MinHours and <= MaxHours ? h : null;
        if (hours is null) errors["contractHours"] = ProposalsText.Contract.ErrHours;

        long? rate = null;
        if (f.Rate.Length > 0)
        {
            var (problem, grosz) = RateInput.Parse(f.Rate);
            if (problem == RateProblem.None) rate = grosz; else errors["contractRate"] = ProposalsText.Contract.ErrRate;
        }

        DateOnly? from = null;
        if (f.Status == AvailableFrom)
        {
            if (DateOnly.TryParseExact(f.From, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d > today && d <= today.AddMonths(MaxMonthsAhead)) from = d;
            else errors["contractFrom"] = ProposalsText.Contract.ErrFrom;
        }
        return errors.Count > 0 ? (null, errors) : (new ContractAvailabilitySettingsDto(f.Status, from, hours, rate), errors);
    }

    /// <summary>The service names <c>status</c>, <c>availableFrom</c>, <c>hoursPerWeek</c> or <c>rateMinor</c>; the form's ids are ours.</summary>
    public static IEnumerable<(string Field, string Message)> FieldErrors(ApiFailure f)
    {
        foreach (var name in f.Fields ?? [])
        {
            var mapped = name switch
            {
                "status" => ("contractStatus", ProposalsText.Contract.ErrStatus), "availableFrom" => ("contractFrom", ProposalsText.Contract.ErrFrom),
                "hoursPerWeek" => ("contractHours", ProposalsText.Contract.ErrHours), "rateMinor" => ("contractRate", ProposalsText.Contract.ErrRate), _ => default,
            };
            if (mapped.Item1 is not null) yield return mapped;
        }
    }
}
