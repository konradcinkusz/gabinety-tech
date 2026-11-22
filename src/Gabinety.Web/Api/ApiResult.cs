namespace Gabinety.Web.Api;

/// <summary>Why a backend call failed. <see cref="Status"/> 0 means no answer at all (down, timed out, circuit open, no address configured); <see cref="Code"/> is the code of CONTRACTS 10.1 when the body had one.</summary>
public sealed record ApiFailure(int Status, string Code, IReadOnlyList<string>? Fields = null, IReadOnlyList<string>? Unmet = null)
{
    public const string Unavailable = "unavailable", InvalidResponse = "invalid_response";
    public static ApiFailure NoAnswer { get; } = new(0, Unavailable);
    public bool IsUnauthenticated => Status == 401;
    /// <summary>True when the server named <paramref name="field"/> in <c>errors</c> (case-insensitive, <c>brief.text</c> matches <c>brief</c> too).</summary>
    public bool Names(string field) => Fields?.Any(f => f.Equals(field, StringComparison.OrdinalIgnoreCase) || f.StartsWith(field + ".", StringComparison.OrdinalIgnoreCase)) == true;
}

/// <summary>
/// A backend answer as state, never as an exception (BLAZOR-WEB-APP section 5): a page renders the failure in a <c>role="alert"</c> block and the rest of the page still renders.
/// The failure carries a status, a code and the NAMES of the fields the server refused (<see cref="ApiFailure.Fields"/>): never a body, a title, a detail or a message, which a server could fill with text of its own.
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
    public static ApiResult<T> Fail(int status, string code, IReadOnlyList<string>? fields) => new(default, new ApiFailure(status, code, fields));
}

/// <summary>The answer of a call whose body is of no interest (a simulated payment): success is the status.</summary>
public sealed record Unit
{
    public static Unit Value { get; } = new();
}
