using System.Globalization;
using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Bookings;
using Gabinety.Web.Components.Pages.Shared;
using Gabinety.Web.Components.Shared;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Proposals;

/// <summary>How a proposal reads (ADR proposal 0013): the badge of a state, the terms as one line, a round's title, and what to say when a call failed. The wire names are those of the Booking service.</summary>
public static class ProposalFormat
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public const string LongTerm = "LongTerm", ShortTerm = "ShortTerm";
    public const string ClientRole = "Client", PractitionerRole = "Practitioner";
    public static readonly string[] States = [ProposalsClient.OpenState, ProposalsClient.ClosedState];

    public static bool IsOpen(string status) => status is "AwaitingPractitioner" or "AwaitingClient";

    public static Badge StatusBadge(string status) => status switch
    {
        "AwaitingPractitioner" => new(ProposalsText.Status.AwaitingPractitioner, BadgeKind.Warning),
        "AwaitingClient" => new(ProposalsText.Status.AwaitingClient, BadgeKind.Warning),
        "Accepted" => new(ProposalsText.Status.Accepted, BadgeKind.Success),
        "Rejected" => new(ProposalsText.Status.Rejected, BadgeKind.Danger),
        "Withdrawn" or "Expired" => new(status == "Withdrawn" ? ProposalsText.Status.Withdrawn : ProposalsText.Status.Expired, BadgeKind.Neutral),
        _ => new(ProposalsText.Status.Unknown, BadgeKind.Neutral),
    };

    public static string KindLabel(string kind) => kind == LongTerm ? ProposalsText.Terms.Long : ProposalsText.Terms.Short;

    /// <summary>"150,00 zł za godzinę".</summary>
    public static string Rate(ProposalTermsDto t) => string.Format(Inv, ProposalsText.Terms.RatePerHour, Money.Pln(t.Rate.AmountMinor));

    /// <summary>"20 godz. tygodniowo" (long term) or "40 godz. łącznie" (short term).</summary>
    public static string Hours(ProposalTermsDto t) => t.HoursPerWeek is { } w
        ? string.Format(Inv, ProposalsText.Terms.HoursPerWeekValue, w)
        : string.Format(Inv, ProposalsText.Terms.TotalHoursValue, t.TotalHours ?? 0);

    public static string? Duration(ProposalTermsDto t) => t.DurationMonths is { } m ? string.Format(Inv, ProposalsText.Terms.DurationValue, m) : null;

    /// <summary>"1 grudnia 2026".</summary>
    public static string Date(DateOnly d) => $"{d.Day} {Pl.Format.MonthsGenitive[d.Month - 1]} {d.Year}";

    /// <summary>The Warsaw calendar day of an instant, as "1 grudnia 2026".</summary>
    public static string Date(DateTimeOffset instant) => Date(DateOnly.FromDateTime(PlFormat.ToWarsaw(instant)));

    /// <summary>One line for a list row: rate, hours, the period when there is one, and the start.</summary>
    public static string Summary(ProposalTermsDto t) => Duration(t) is { } d
        ? string.Format(Inv, ProposalsText.Terms.SummaryLong, Rate(t), Hours(t), d, Date(t.StartDate))
        : string.Format(Inv, ProposalsText.Terms.Summary, Rate(t), Hours(t), Date(t.StartDate));

    public static string ActionTitle(string action) => action switch
    {
        "Proposed" => ProposalsText.Thread.Proposed, "Countered" => ProposalsText.Thread.Countered, "Accepted" => ProposalsText.Thread.Accepted,
        "Rejected" => ProposalsText.Thread.Rejected, "Withdrawn" => ProposalsText.Thread.Withdrawn, "Expired" => ProposalsText.Thread.Expired, _ => action,
    };

    public static string ByLabel(string by) => by switch
    {
        "Client" => ProposalsText.Thread.ByClient, "Practitioner" => ProposalsText.Thread.ByPractitioner, _ => ProposalsText.Thread.BySystem,
    };

    public static string RoundHeading(ProposalRoundDto r) => string.Format(Inv, ProposalsText.Thread.RoundHeading, r.Number, ActionTitle(r.Action) + " (" + ByLabel(r.By) + ")");

    /// <summary>The other party of a row: the programmer for the client, the client for the programmer.</summary>
    public static string Counterpart(ProposalSummaryDto p, bool viewerIsClient) => viewerIsClient ? p.Practitioner.DisplayName : p.Client.DisplayName;

    /// <summary>What the user reads when a call failed: from the status and the code only, never from the server's title or message (they are English and may echo input).</summary>
    public static string FailureText(ApiFailure f) => f switch
    {
        { Status: 0 } => ProposalsText.Errors.Network,
        { Code: "invalid_transition" } => ProposalsText.Errors.InvalidTransition,
        { Code: "own_profile" } => ProposalsText.Errors.OwnProfile,
        { Code: "practitioner_not_available" } => ProposalsText.Errors.NotAvailable,
        { Code: "proposal_exists" } => ProposalsText.Errors.Exists,
        { Code: "contact_details_not_allowed" } => ProposalsText.Errors.Contact,
        { Code: "rate_limited" } or { Status: 429 } => ProposalsText.Errors.RateLimited,
        { Code: "dependency_unavailable" } or { Status: 503 } => ProposalsText.Errors.Unavailable,
        { Code: "not_found" } or { Status: 404 } => ProposalsText.Errors.NotFound,
        { Code: "validation_failed" or "invalid_input" } or { Status: 400 or 422 } => ProposalsText.Errors.Validation,
        { Code: "forbidden" } or { Status: 403 } => ProposalsText.Errors.Forbidden,
        _ => ProposalsText.Errors.Generic,
    };

    /// <summary>Where a server-named field goes in our form: the service names <c>terms.rate</c>, <c>terms.startDate</c>, <c>text</c>, <c>companyName</c>; the form's ids are the last part.</summary>
    public static IEnumerable<(string Field, string Message)> FieldErrors(ApiFailure f, bool longTerm)
    {
        foreach (var name in f.Fields ?? [])
        {
            var field = name.StartsWith("terms.", StringComparison.Ordinal) ? name["terms.".Length..] : name;
            var message = field switch
            {
                "rate" => ProposalsText.Errors.Rate,
                "hoursPerWeek" => ProposalsText.Errors.HoursPerWeek,
                "totalHours" => ProposalsText.Errors.TotalHours,
                "durationMonths" => ProposalsText.Errors.Duration,
                "startDate" => ProposalsText.Errors.Start,
                "text" => ProposalsText.Errors.TextShort,
                "companyName" => ProposalsText.Errors.CompanyLong,
                _ => null,
            };
            if (message is not null) yield return (field, message);
        }
    }

    /// <summary>"Zamknięte" is a state of the list; anything but <c>open</c> and <c>closed</c> is "every state".</summary>
    public static string? StateFilter(string? state) => state is ProposalsClient.OpenState or ProposalsClient.ClosedState ? state : null;
}
