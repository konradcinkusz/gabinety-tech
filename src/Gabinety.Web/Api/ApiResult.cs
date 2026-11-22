namespace Gabinety.Web.Api;

/// <summary>Why a backend call failed. <see cref="Status"/> 0 means no answer at all (down, timed out, circuit open, no address configured); <see cref="Code"/> is the code of CONTRACTS 10.1 when the body had one.</summary>
public sealed record ApiFailure(int Status, string Code)
{
    /// <summary>The names of the fields a <c>422 validation_failed</c> complained about (never the messages).</summary>
    public IReadOnlyList<string> Fields { get; init; } = [];
    /// <summary>The names of the unmet items of a <c>422 submit_incomplete</c>.</summary>
    public IReadOnlyList<string> Unmet { get; init; } = [];
    public const string Unavailable = "unavailable", InvalidResponse = "invalid_response";
    public static ApiFailure NoAnswer { get; } = new(0, Unavailable);
    public bool IsUnauthenticated => Status == 401;
}

/// <summary>
/// A backend answer as state, never as an exception (BLAZOR-WEB-APP section 5): a page renders the failure in a <c>role="alert"</c> block and the rest of the page still renders.
/// The failure carries a status and a code only: never a body, a title or a detail, which a server could fill with text of its own.
/// </summary>
public sealed class ApiResult<T>
{
    ApiResult(T? value, ApiFailure? failure) { Value = value; Failure = failure; }
    public T? Value { get; }
    public ApiFailure? Failure { get; }
    public bool IsOk => Failure is null;
    public static ApiResult<T> Ok(T value) => new(value, null);
    public static ApiResult<T> Fail(ApiFailure failure) => new(default, failure);
    public static ApiResult<T> Fail(int status, string code) => new(default, new ApiFailure(status, code));
}

/// <summary>The "answer" of a call that answers <c>204 No Content</c>.</summary>
public sealed class NoContent
{
    NoContent() { }
    public static NoContent Value { get; } = new();
}
