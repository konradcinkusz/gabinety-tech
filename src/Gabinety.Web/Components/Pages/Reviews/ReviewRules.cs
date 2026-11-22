using Gabinety.Web.Api;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Reviews;

/// <summary>The shape rules of CONTRACTS 5.1/5.2 (rating 1 to 5, text 20 to 2000, reply 10 to 1000) and the words for a refusal, by code. Whether a booking can be reviewed is the service's decision, not the page's.</summary>
public static class ReviewRules
{
    public const int RatingMin = 1, RatingMax = 5, TextMin = 20, TextMax = 2000, ReplyMin = 10, ReplyMax = 1000;

    public static string? RatingProblem(int? rating) => rating is >= RatingMin and <= RatingMax ? null : ReviewsText.Form.RatingRequired;

    public static string? TextProblem(string? text)
    {
        var n = (text ?? "").Trim().Length;
        return n < TextMin ? ReviewsText.Form.TextTooShort : n > TextMax ? ReviewsText.Form.TextTooLong : null;
    }

    public static string? ReplyProblem(string? text)
    {
        var n = (text ?? "").Trim().Length;
        return n < ReplyMin ? ReviewsText.Reply.TooShort : n > ReplyMax ? ReviewsText.Reply.TooLong : null;
    }

    /// <summary>Only an explicit Published is announced as published; Held and anything unknown get the held wording (never promise publication).</summary>
    public static bool IsPublished(string? status) => status == "Published";

    public static string FailureText(ApiFailure f) => f switch
    {
        { Status: 0 } => ReviewsText.Errors.Network,
        { Code: "dependency_unavailable" } or { Status: 503 } => ReviewsText.Errors.Unavailable,
        { Code: "not_found" } or { Status: 404 } => ReviewsText.Errors.NotFound,
        { Code: "validation_failed" or "invalid_input" } or { Status: 400 or 422 } => ReviewsText.Errors.Validation,
        { Code: "forbidden" } or { Status: 403 } => ReviewsText.Errors.Forbidden,
        { Code: "rate_limited" } or { Status: 429 } => ReviewsText.Errors.RateLimited,
        _ => ReviewsText.Errors.Generic,
    };
}
