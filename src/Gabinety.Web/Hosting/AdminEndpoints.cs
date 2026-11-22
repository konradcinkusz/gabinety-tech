using Gabinety.Web.Api;
using Gabinety.Web.Auth;
using Gabinety.Web.Components.Pages.Admin;
using Gabinety.Web.Text;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gabinety.Web.Hosting;

/// <summary>
/// The one non-page route of the administrator area: the ledger download. The Next.js app needed its own route because its generic proxy cuts reads at 5 s and drops <c>Content-Disposition</c>; here it is an endpoint that STREAMS Booking's
/// <c>ledger.csv</c> to the browser with the administrator's own bearer: nothing is buffered, so a year of rows costs a few buffers of memory. Only the HEADERS wait for the 5 s of the read profile; the body is not time-limited here (a year of rows
/// streams for longer) and a browser that leaves cancels it (<see cref="HttpContext.RequestAborted"/>, which also ends the backend call).
/// </summary>
public static class AdminEndpoints
{
    public const string LedgerCsvPath = "/admin/ksiega/csv";
    const int FirstChunk = 8 * 1024, CopyBuffer = 16 * 1024;

    public static WebApplication MapAdminEndpoints(this WebApplication app)
    {
        app.MapGet(LedgerCsvPath, LedgerCsvAsync).RequireAuthorization(AdminAuthorization.Policy);
        return app;
    }

    static async Task<IResult> LedgerCsvAsync([FromQuery] string? from, [FromQuery] string? to, AdminApi api, HttpContext http, ILoggerFactory logs)
    {
        if (LedgerPeriod.Check(from, to) != PeriodProblem.None) return Refusal(StatusCodes.Status400BadRequest, "validation_failed");
        var log = logs.CreateLogger("Gabinety.Web.LedgerCsv");
        UseRequestUser(http);
        var (stream, failure) = await api.LedgerCsv(from!, to!, http.RequestAborted);
        if (stream is null)
        {
            log.LogWarning("ledger.csv upstream refused: status {Status}", failure!.Status); // the status only: no body, no address, no period
            return failure.Status switch
            {
                401 => Refusal(StatusCodes.Status401Unauthorized, "unauthorized"),
                403 => Refusal(StatusCodes.Status403Forbidden, "forbidden"),
                400 or 422 => Refusal(StatusCodes.Status400BadRequest, "validation_failed"),
                404 => Refusal(StatusCodes.Status404NotFound, "not_found"),
                429 => Refusal(StatusCodes.Status429TooManyRequests, "rate_limited"),
                _ => Refusal(StatusCodes.Status503ServiceUnavailable, "dependency_unavailable"),
            };
        }
        // The first buffer is read BEFORE the response is committed: a backend that fails at once is a 503 the browser can show, not a "download" of nothing.
        var first = new byte[FirstChunk];
        int read;
        try { read = await stream.Body.ReadAsync(first, http.RequestAborted); }
        catch (Exception) when (!http.RequestAborted.IsCancellationRequested)
        {
            stream.Dispose();
            log.LogWarning("ledger.csv upstream failed before the first byte");
            return Refusal(StatusCodes.Status503ServiceUnavailable, "dependency_unavailable");
        }
        return new LedgerStreamResult(stream, first.AsMemory(0, read), LedgerPeriod.FileName(from!, to!), log);
    }

    /// <summary>
    /// The client wrappers read the bearer through the scope's <see cref="AuthenticationStateProvider"/>, which a Razor component's request gets from the framework. This route is not a component, so it gives the provider
    /// the same thing the framework does for a page: the user of THIS request (the cookie's principal). Nothing else is trusted.
    /// </summary>
    static void UseRequestUser(HttpContext http)
    {
        if (http.RequestServices.GetRequiredService<AuthenticationStateProvider>() is IHostEnvironmentAuthenticationStateProvider host)
            host.SetAuthenticationState(Task.FromResult(new AuthenticationState(http.User)));
    }

    static IResult Refusal(int status, string code) =>
        Results.Problem(statusCode: status, title: PlAdmin.Ledger.DownloadFailed, extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>
    /// The response: headers, the first buffer, the rest copied as it arrives. If the backend's stream breaks AFTER the response started, the connection is ABORTED, never completed: the browser then reports a failed download
    /// instead of saving a file that looks complete and is not (the status line is already sent, so an error status is no longer possible).
    /// </summary>
    sealed class LedgerStreamResult(ApiStream stream, ReadOnlyMemory<byte> first, string fileName, ILogger log) : IResult
    {
        public async Task ExecuteAsync(HttpContext http)
        {
            using var _ = stream;
            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.ContentType = "text/csv; charset=utf-8";
            http.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            try
            {
                await http.Response.Body.WriteAsync(first, http.RequestAborted);
                await stream.Body.CopyToAsync(http.Response.Body, CopyBuffer, http.RequestAborted);
            }
            catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { } // the browser left: the backend call ends with the disposal
            catch (Exception)
            {
                log.LogError("ledger.csv stream broke after the response started; the connection is aborted"); // never the exception: it can carry the backend address
                http.Abort();
            }
        }
    }
}
