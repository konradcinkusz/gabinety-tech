using System.ComponentModel.DataAnnotations;

namespace Gabinety.Contracts.Common;

/// <summary>Money on the wire (CONTRACTS 0): integer minor units plus currency, never a decimal.</summary>
public sealed record MoneyDto(long AmountMinor, string Currency = "PLN");

/// <summary>Cursor page (CONTRACTS 0): NextCursor is omitted on the last page.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor = null);

/// <summary>RFC 9457 problem+json body (CONTRACTS 0). Detail never carries user free text or a secret.</summary>
public sealed record ApiProblem(string Type, string Title, int Status, string? Detail, string Code,
    IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public static ApiProblem Of(string code, string title, int status, string? detail = null,
        IReadOnlyDictionary<string, string[]>? errors = null) =>
        new($"urn:gabinety:error:{code}", title, status, detail, code, errors);
}

/// <summary>Error codes of CONTRACTS 10.1.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed", Unauthenticated = "unauthenticated", Forbidden = "forbidden",
        NotFound = "not_found", InvalidTransition = "invalid_transition", SlotTaken = "slot_taken",
        IdempotencyConflict = "idempotency_conflict", PractitionerExists = "practitioner_exists",
        AlreadyReviewed = "already_reviewed", AlreadyReplied = "already_replied", SlotUnavailable = "slot_unavailable",
        OfferingInactive = "offering_inactive", OwnOffering = "own_offering", PractitionerNotBookable = "practitioner_not_bookable",
        SubmitIncomplete = "submit_incomplete", TooManyOfferings = "too_many_offerings",
        ContactDetailsNotAllowed = "contact_details_not_allowed", GithubNotLinked = "github_not_linked",
        NotReviewable = "not_reviewable", InvalidInput = "invalid_input", RateLimited = "rate_limited",
        AssistantUnavailable = "assistant_unavailable", DependencyUnavailable = "dependency_unavailable",
        CrossSiteRequest = "cross_site_request",
        /// <summary>The practitioner has no connected payment account that can receive money yet (T-09.05, ADR proposal 0011); CONTRACTS 10.1 does not list it yet.</summary>
        PractitionerPaymentsNotReady = "practitioner_payments_not_ready";
}
