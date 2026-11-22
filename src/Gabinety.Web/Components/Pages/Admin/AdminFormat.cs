using Gabinety.Web.Api;
using Gabinety.Web.Components.Shared;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Admin;

/// <summary>The kinds of badge the admin pages use (the status families of the design contract 5.7).</summary>
public enum BadgeKind { Neutral, Info, Success, Warning, Danger }

/// <summary>How the admin pages show a date, a reference, a status. Plain C#, unit tested without a renderer (BLAZOR-WEB-APP section 10).</summary>
public static class AdminFormat
{
    public static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    /// <summary><c>24 lis 2026, 13:00</c> in Europe/Warsaw: no seconds, the month short (P-ADM-3).</summary>
    public static string When(DateTimeOffset at)
    {
        var local = TimeZoneInfo.ConvertTime(at, Warsaw);
        return $"{local.Day} {PlAdmin.Common.MonthsShort[local.Month - 1]} {local.Year}, {local:HH\\:mm}";
    }

    public static string? When(DateTimeOffset? at) => at is { } value ? When(value) : null;

    /// <summary>The first eight characters of an id, for a person to recognise (never the primary text of a row); the whole id goes into a <c>title</c>.</summary>
    public static string ShortRef(string id) => id.Length <= 8 ? id : id[..8];

    public static string ShortRef(Guid id) => id.ToString("D")[..8];

    /// <summary>
    /// The last eight characters of an id: the random part of a UUIDv7. The first eight are its timestamp, so every id made in the same moment reads the same (the ledger's column of
    /// identical <c>01a2039c</c> rows). The whole id goes into a <c>title</c>.
    /// </summary>
    public static string IdTail(Guid id) => id.ToString("D")[^8..];

    /// <summary>A grosz amount: <c>180,00 zł</c>; a negative amount (a reversal) keeps its sign.</summary>
    public static string Pln(long grosz) => grosz < 0 ? "−" + Money.Pln(-grosz) : Money.Pln(grosz);

    static string Label(IReadOnlyDictionary<string, string> labels, string? key, string fallback) => key is not null && labels.TryGetValue(key, out var text) ? text : fallback;

    public static string PractitionerStatus(string? s) => Label(PlAdmin.Verification.Statuses, s, PlAdmin.Common.Unknown);
    public static BadgeKind PractitionerStatusKind(string? s) => s switch
    {
        "Approved" => BadgeKind.Success, "Submitted" => BadgeKind.Warning, "Rejected" or "Suspended" => BadgeKind.Danger, _ => BadgeKind.Neutral,
    };

    public static string VerificationEvent(string? kind) => Label(PlAdmin.Verification.Events, kind, PlAdmin.Verification.EventUnknown);

    public static string DisputeKind(string? kind) => Label(PlAdmin.Disputes.Kinds, kind, PlAdmin.Disputes.KindUnknown);
    public static string Resolution(string? r) => Label(PlAdmin.Disputes.Resolutions, r, PlAdmin.Common.Unknown);
    public static string Payment(string? s) => Label(PlAdmin.Disputes.PaymentStates, s, PlAdmin.Common.Unknown);
    public static BadgeKind PaymentKind(string? s) => s switch
    {
        "Succeeded" => BadgeKind.Success, "Refunded" or "PartiallyRefunded" => BadgeKind.Info, "Created" => BadgeKind.Warning, "Failed" => BadgeKind.Danger, _ => BadgeKind.Neutral,
    };

    public static string ReviewStatus(string? s) => Label(PlAdmin.Reviews.Statuses, s, PlAdmin.Common.Unknown);
    public static BadgeKind ReviewStatusKind(string? s) => s switch { "Published" => BadgeKind.Success, "Held" => BadgeKind.Warning, "Rejected" => BadgeKind.Danger, _ => BadgeKind.Neutral };

    /// <summary>The text of a triage flag; an unknown flag is "no information", never the raw wire value.</summary>
    public static string Triage(string? flag) => flag switch
    {
        "none" => PlAdmin.Reviews.TriageNone, "hold" => PlAdmin.Reviews.TriageHold, "unavailable" => PlAdmin.Reviews.TriageUnavailable, _ => PlAdmin.Reviews.TriageUnknown,
    };

    public static string HoldReason(string? r) => r switch
    {
        "spam" => PlAdmin.Reviews.ReasonSpam, "abuse" => PlAdmin.Reviews.ReasonAbuse, "personal_data" => PlAdmin.Reviews.ReasonPersonalData,
        "off_topic" => PlAdmin.Reviews.ReasonOffTopic, "threat" => PlAdmin.Reviews.ReasonThreat, _ => PlAdmin.Reviews.ReasonOther,
    };

    public static string LedgerKind(string? kind) => kind switch
    {
        "ClientCharge" => PlAdmin.Ledger.KindClientCharge, "Refund" => PlAdmin.Ledger.KindRefund, "PlatformFee" => PlAdmin.Ledger.KindPlatformFee,
        "PractitionerPayout" => PlAdmin.Ledger.KindPractitionerPayout, "PayoutReversal" => PlAdmin.Ledger.KindPayoutReversal, _ => PlAdmin.Ledger.KindUnknown,
    };

    /// <summary>Only an http(s) address is ever a link (a portfolio address is user text).</summary>
    public static bool IsHttp(string? url) => url is not null && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https";

    /// <summary>A GitHub login is linked only when it is a plausible login: letters, digits and hyphens (GitHub's own rule), at most 39 characters.</summary>
    public static bool IsGithubLogin(string? login) => login is { Length: > 0 and <= 39 } && login.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
}

/// <summary>The one rule of every reason and note an administrator writes: 10 to 500 characters after trimming (decided in #125 and ADR-0012 decision 2; CONTRACTS 3.4, 4.3, 5.2).</summary>
public static class TextRule
{
    public const int Min = 10, Max = 500;

    public static bool IsValid(string? text) => text?.Trim().Length is >= Min and <= Max;
}

/// <summary>The messages of a failed admin call. Never the server's title or detail: a code and a status only (the same rule as <see cref="ApiFailure"/>).</summary>
public static class AdminFailure
{
    /// <summary>The generic cases of every admin action. The page adds what is specific to it before calling this.</summary>
    public static string Text(ApiFailure f) => f switch
    {
        { Status: 0 } => PlAdmin.Common.Network,
        { Status: 401 } => PlAdmin.Common.SignedOut,
        { Status: 403 } => PlAdmin.Common.Forbidden,
        { Status: 404 } => PlAdmin.Common.NotFound,
        { Status: 429 } => PlAdmin.Common.RateLimited,
        { Status: 503 } => PlAdmin.Common.Generic,
        _ => PlAdmin.Common.Generic,
    };

    /// <summary>A refused state change (409, <c>invalid_transition</c>): somebody else decided first. A conflict is a message and a fresh list, never a crash.</summary>
    public static bool IsConflict(ApiFailure f) => f.Status == 409;

    /// <summary>A failed read, for the <c>unavailable</c> callout of a list or a detail.</summary>
    public static bool IsForbidden(ApiFailure f) => f.Status == 403;
}
