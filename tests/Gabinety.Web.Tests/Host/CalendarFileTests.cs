using System.Net;
using Gabinety.Web.Api;
using Gabinety.Web.Tests.Pages;
using static Gabinety.Web.Tests.Pages.ClientAreaFixture;

namespace Gabinety.Web.Tests.Pipeline;

/// <summary>GET /moje/rezerwacje/{id}/kalendarz.ics against the real host with a fake Booking: the owner's file, authentication, the statuses that have a file, and what the file says.</summary>
public class CalendarFileTests
{
    static readonly string Path = $"/moje/rezerwacje/{BookingId}/kalendarz.ics";

    static async Task<ClientAreaFixture> SignedIn(BookingDetailDto booking)
    {
        var f = await ClientAreaFixture.SignedIn();
        f.Web.Booking.Respond = _ => FakeBackend.Json(booking);
        return f;
    }

    [Fact]
    public async Task A_visitor_is_sent_to_sign_in_and_no_booking_is_read()
    {
        await using var f = await ClientAreaFixture.Visitor();
        var page = await f.Browser.GetAsync(Path);
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.StartsWith("/logowanie", page.Location);
        Assert.Empty(f.Web.Booking.Requests);
        Assert.DoesNotContain("BEGIN:VCALENDAR", page.Html);
    }

    [Fact]
    public async Task A_confirmed_booking_is_an_attachment_in_the_calendar_format_and_is_read_with_the_users_own_bearer()
    {
        await using var f = await SignedIn(Detail("Confirmed", meeting: "https://meet.example/demo-sala"));
        var page = await f.Browser.GetAsync(Path);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("text/calendar; charset=utf-8", page.Header("Content-Type"));
        Assert.Equal("attachment; filename=\"rezerwacja.ics\"", page.Header("Content-Disposition"));
        Assert.Equal("no-store", page.Header("Cache-Control"));
        Assert.StartsWith("BEGIN:VCALENDAR\r\n", page.Html);
        Assert.EndsWith("END:VCALENDAR\r\n", page.Html);
        Assert.DoesNotMatch(@"(?<!\r)\n", page.Html); // CRLF throughout

        // Long lines are folded (CRLF and a space): the content is checked unfolded, as a calendar application reads it.
        var content = page.Html.Replace("\r\n ", "");
        Assert.Contains($"UID:{BookingId:D}@gabinety.tech\r\n", page.Html);
        Assert.Matches(@"DTSTAMP:\d{8}T\d{6}Z\r\n", page.Html);
        Assert.Contains("DTSTART:20261208T110000Z\r\n", page.Html); // the fixture's start is 11:00 UTC
        Assert.Contains("DTEND:20261208T120000Z\r\n", page.Html);
        Assert.Contains("SUMMARY:Audyt kodu demo\r\n", page.Html);
        Assert.Contains("DESCRIPTION:Programista: Anna Demowska\\nLink do spotkania: https://meet.example/demo-sala\r\n", content);

        // the booking is read as the signed-in user, never with a token of its own
        Assert.NotEmpty(f.Web.Booking.Requests);
        Assert.All(f.Web.Booking.Requests, r => Assert.StartsWith("Bearer eyJ", r.Authorization));
    }

    [Theory]
    [InlineData("javascript:alert(1)")] [InlineData("/wzgledny")] [InlineData("ftp://x.example/a")]
    public async Task A_meeting_link_that_is_not_http_or_https_is_left_out_of_the_file(string url)
    {
        await using var f = await SignedIn(Detail("Confirmed", meeting: url));
        var page = await f.Browser.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("DESCRIPTION:Programista: Anna Demowska\r\n", page.Html);
        Assert.DoesNotContain(url, page.Html);
    }

    [Fact]
    public async Task A_completed_booking_has_its_file_too()
    {
        await using var f = await SignedIn(Detail("Completed"));
        Assert.Equal(HttpStatusCode.OK, (await f.Browser.GetAsync(Path)).Status);
    }

    [Theory]
    [InlineData("PendingPayment")] [InlineData("Expired")] [InlineData("Cancelled")]
    public async Task A_booking_that_is_not_confirmed_or_completed_has_no_file(string status)
    {
        await using var f = await SignedIn(Detail(status, payment: "Created", hold: status == "PendingPayment" ? DateTimeOffset.UtcNow.AddMinutes(5) : null));
        var page = await f.Browser.GetAsync(Path);
        Assert.Equal(HttpStatusCode.NotFound, page.Status);
        Assert.DoesNotContain("BEGIN:VCALENDAR", page.Html);
    }

    [Theory]
    [InlineData(404, "not_found")] [InlineData(403, "forbidden")]
    public async Task Another_users_or_an_unknown_booking_is_a_404_not_a_leak(int status, string code)
    {
        await using var f = await ClientAreaFixture.SignedIn();
        f.Web.Booking.Respond = _ => FakeBackend.Problem((HttpStatusCode)status, code);
        var page = await f.Browser.GetAsync(Path);
        Assert.Equal(HttpStatusCode.NotFound, page.Status);
        Assert.DoesNotContain("SERVER-TITLE-MUST-NOT-APPEAR", page.Html);
        Assert.DoesNotContain("BEGIN:VCALENDAR", page.Html);
    }

    [Fact]
    public async Task A_booking_service_that_is_down_is_a_503_and_never_an_empty_file()
    {
        await using var f = await ClientAreaFixture.SignedIn();
        f.Web.Booking.Respond = _ => FakeBackend.Problem(HttpStatusCode.ServiceUnavailable, "dependency_unavailable");
        var page = await f.Browser.GetAsync(Path);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, page.Status);
        Assert.DoesNotContain("BEGIN:VCALENDAR", page.Html);
    }

    [Fact]
    public async Task The_route_takes_a_guid_only()
    {
        await using var f = await ClientAreaFixture.SignedIn();
        var page = await f.Browser.GetAsync("/moje/rezerwacje/nie-guid/kalendarz.ics");
        Assert.Equal(HttpStatusCode.NotFound, page.Status);
        Assert.Empty(f.Web.Booking.Requests);
    }
}
