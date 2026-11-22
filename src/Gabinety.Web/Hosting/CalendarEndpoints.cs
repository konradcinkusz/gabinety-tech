using System.Globalization;
using System.Text;
using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Bookings;
using Gabinety.Web.Text;
using Microsoft.AspNetCore.Components.Authorization;

namespace Gabinety.Web.Hosting;

/// <summary>
/// <c>GET /moje/rezerwacje/{BookingId}/kalendarz.ics</c>: one booking as an iCalendar file, for the "Dodaj do kalendarza" link of the confirmation. It is authorised like the booking pages: the
/// signed-in user's bearer reads the booking from Booking (<see cref="BookingClient"/>), so another user's booking is the same 404 as an unknown id. Only a confirmed or completed booking has a file.
/// Nothing from the booking is logged. The response is an attachment that is never cached.
/// </summary>
public static class CalendarEndpoints
{
    public const string CalendarPath = "/moje/rezerwacje/{BookingId:guid}/kalendarz.ics";
    public const string FileName = "rezerwacja.ics", ContentType = "text/calendar; charset=utf-8";

    public static WebApplication MapCalendarEndpoints(this WebApplication app)
    {
        app.MapGet(CalendarPath, CalendarAsync).RequireAuthorization();
        return app;
    }

    static async Task<IResult> CalendarAsync(Guid BookingId, HttpContext http, BookingClient bookings, TimeProvider time)
    {
        UseRequestUser(http);
        var result = await bookings.GetAsync(BookingId, http.RequestAborted);
        if (!result.IsOk)
        {
            if (result.Failure!.IsUnauthenticated) return Results.Challenge();
            return result.Failure.Status is 403 or 404 ? Results.NotFound() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        var b = result.Value!;
        if (b.Status is not ("Confirmed" or "Completed")) return Results.NotFound();

        var lines = new List<string>();
        if (BookingFormat.Name(b.Practitioner.DisplayName) is { } practitioner) lines.Add(string.Format(CultureInfo.InvariantCulture, BookFlowText.Confirmation.CalendarPractitioner, practitioner));
        if (SafeUrl.Http(b.MeetingUrl) is { } meeting) lines.Add(string.Format(CultureInfo.InvariantCulture, BookFlowText.Confirmation.CalendarMeeting, meeting));
        var ics = IcsBuilder.Event(new IcsEvent(b.Id, time.GetUtcNow(), b.StartUtc, b.EndUtc, b.OfferingTitle, string.Join("\n", lines)));

        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.ContentDisposition = $"attachment; filename=\"{FileName}\"";
        return Results.Bytes(Encoding.UTF8.GetBytes(ics), ContentType);
    }

    /// <summary>
    /// The client wrappers read the bearer through the scope's <see cref="AuthenticationStateProvider"/>; this route is not a component, so it gives the provider the user of THIS request (the same
    /// thing <c>AdminEndpoints</c> does for the ledger download). Nothing else is trusted.
    /// </summary>
    static void UseRequestUser(HttpContext http)
    {
        if (http.RequestServices.GetRequiredService<AuthenticationStateProvider>() is IHostEnvironmentAuthenticationStateProvider host)
            host.SetAuthenticationState(Task.FromResult(new AuthenticationState(http.User)));
    }
}
