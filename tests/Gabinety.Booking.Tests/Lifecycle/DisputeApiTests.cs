using System.Net;
using System.Net.Http.Json;
using Gabinety.Billing;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>POST /api/v1/bookings/{id}/dispute (CONTRACTS 4.2, 8.1). Seeded bookings last 1 h, so end = start + 1 h; the clock is fixed.</summary>
public class DisputeApiTests
{
    const string Text = "demo-dispute-text: the session was not what the offer said";
    static readonly TimeSpan H = TimeSpan.FromHours(1), S = TimeSpan.FromSeconds(1);

    internal static Task<HttpResponseMessage> Dispute(CreateHost h, Guid id, string? as_, object? body) =>
        h.ClientFor(as_).PostAsJsonAsync($"/api/v1/bookings/{id}/dispute", body);

    static object Body(string kind = "not_as_described", string text = Text) => new { kind, text };
    static async Task<string> Code(HttpResponseMessage r) => (await r.Body()).GetProperty("code").GetString()!;

    public static TheoryData<string, int, bool> Windows => new()
    {
        // kind, seconds from now to START, allowed. The session lasts 3600 s, so END = START + 3600.
        { "other", -3601, true },                       // ended 1 s ago
        { "other", -3600, true },                       // ends exactly now
        { "other", -3599, false },                      // ends in 1 s
        { "other", -(49 * 3600) + 1, true },            // end + 48 h is 1 s ahead
        { "other", -(49 * 3600), false },               // end + 48 h is exactly now (auto-completion is due)
        { "other", -(49 * 3600) - 1, false },           // 1 s past
        { "not_as_described", -3599, false },
        { "not_as_described", -(49 * 3600) + 1, true },
        { "practitioner_no_show", 1, false },           // not started
        { "practitioner_no_show", 0, true },            // starts exactly now
        { "practitioner_no_show", -1, true },           // started 1 s ago, still running
        { "practitioner_no_show", -(72 * 3600) + 1, true },
        { "practitioner_no_show", -(72 * 3600), false },
        { "practitioner_no_show", -(72 * 3600) - 1, false },
        { "practitioner_no_show", -(60 * 3600), true }, // past end + 48 h but inside start + 72 h
        { "other", -(60 * 3600), false },               // the same time is too late for the other kinds
    };

    [Theory]
    [MemberData(nameof(Windows))]
    public async Task The_window_edges_are_exact(string kind, int startInSeconds, bool allowed)
    {
        await using var h = Life.Host(new RecordingProvider());
        var id = await Life.Seed(h, BookingStatus.Confirmed, TimeSpan.FromSeconds(startInSeconds));
        var r = await Dispute(h, id, Life.Client, Body(kind));
        var row = await Life.Row(h, id);
        if (allowed)
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(BookingStatus.Disputed, row.Status);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("invalid_transition", await Code(r));
            Assert.Equal(BookingStatus.Confirmed, row.Status);
            Assert.Null(row.DisputeOpenedAt);
            Assert.Null(row.DisputeKind);
            Assert.Null(row.DisputeText);
        }
    }

    [Fact]
    public async Task It_stores_kind_and_text_freezes_the_payout_and_returns_the_view_without_the_text()
    {
        await using var h = Life.Host(new RecordingProvider());
        var id = await Life.Seed(h, BookingStatus.Confirmed, -2 * H);
        var r = await Dispute(h, id, Life.Client, Body("practitioner_no_show", "  " + Text + "  "));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var raw = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("dispute-text", raw);
        Assert.DoesNotContain("practitioner_no_show", raw);
        var view = System.Text.Json.JsonDocument.Parse(raw).RootElement;
        Assert.Equal(id, view.GetProperty("id").GetGuid());
        Assert.Equal("Disputed", view.GetProperty("status").GetString());

        var row = await Life.Row(h, id);
        Assert.Equal(BookingStatus.Disputed, row.Status);
        Assert.Equal(h.Clock.GetUtcNow(), row.DisputeOpenedAt);
        Assert.Equal("practitioner_no_show", row.DisputeKind);
        Assert.Equal(Text, row.DisputeText); // trimmed
        Assert.Null(row.PayoutDueAt);        // the status itself freezes the payout
        Assert.Null(row.CancelledAt);
        Assert.Empty(await Life.RefundRows(h, id));
    }

    [Fact]
    public async Task A_second_dispute_of_the_same_booking_is_409()
    {
        await using var h = Life.Host(new RecordingProvider());
        var id = await Life.Seed(h, BookingStatus.Confirmed, -2 * H);
        Assert.Equal(HttpStatusCode.OK, (await Dispute(h, id, Life.Client, Body())).StatusCode);
        var again = await Dispute(h, id, Life.Client, Body("other", "demo-second-text-attempt"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var row = await Life.Row(h, id);
        Assert.Equal("not_as_described", row.DisputeKind);
        Assert.Equal(Text, row.DisputeText);
    }

    public static TheoryData<BookingStatus> Illegal => new(Enum.GetValues<BookingStatus>().Where(s => s is not (BookingStatus.Confirmed or BookingStatus.Completed))); // Completed: DisputeFromCompletedTests

    [Theory]
    [MemberData(nameof(Illegal))]
    public async Task From_any_status_but_Confirmed_and_Completed_it_is_409_invalid_transition(BookingStatus status)
    {
        await using var h = Life.Host(new RecordingProvider());
        var id = await Life.Seed(h, status, -2 * H);
        var r = await Dispute(h, id, Life.Client, Body());
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("invalid_transition", await Code(r));
        var row = await Life.Row(h, id);
        Assert.Equal(status, row.Status);
        Assert.Null(row.DisputeText);
        Assert.Null(row.DisputeOpenedAt);
    }

    [Theory]
    [InlineData(Life.Practitioner)]
    [InlineData("demo-stranger")]
    public async Task Only_the_client_may_dispute_anyone_else_gets_404(string caller)
    {
        await using var h = Life.Host(new RecordingProvider());
        var id = await Life.Seed(h, BookingStatus.Confirmed, -2 * H);
        var r = await Dispute(h, id, caller, Body());
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("not_found", await Code(r));
        // the party check comes before body validation: a stranger with a bad body still learns nothing
        Assert.Equal(HttpStatusCode.NotFound, (await Dispute(h, id, caller, Body("nope", "x"))).StatusCode);
        Assert.Equal(BookingStatus.Confirmed, (await Life.Row(h, id)).Status);
    }

    [Fact]
    public async Task An_unknown_booking_is_404_and_no_token_is_401()
    {
        await using var h = Life.Host(new RecordingProvider());
        var id = await Life.Seed(h, BookingStatus.Confirmed, -2 * H);
        Assert.Equal(HttpStatusCode.NotFound, (await Dispute(h, Guid.NewGuid(), Life.Client, Body())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Dispute(h, id, null, Body())).StatusCode);
    }

    public static TheoryData<string?, string?> BadBodies() => new()
    {
        { null, Text },
        { "", Text },
        { "refund_me", Text },
        { "NOT_AS_DESCRIBED", Text },
        { "other", null },
        { "other", "123456789" },                // 9 characters
        { "other", "   123456789   " },          // 9 after trimming
        { "other", "         " },
        { "other", new string('a', 1001) },
        { "other", " " + new string('a', 1001) },
    };

    [Theory]
    [MemberData(nameof(BadBodies))]
    public async Task An_invalid_kind_or_text_is_422_validation_failed(string? kind, string? text)
    {
        await using var h = Life.Host(new RecordingProvider());
        var id = await Life.Seed(h, BookingStatus.Confirmed, -2 * H);
        var r = await Dispute(h, id, Life.Client, new { kind, text });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("validation_failed", await Code(r));
        Assert.Equal(BookingStatus.Confirmed, (await Life.Row(h, id)).Status);
    }

    [Fact]
    public async Task A_missing_body_is_422_and_the_boundary_lengths_pass()
    {
        await using var h = Life.Host(new RecordingProvider());
        var id = await Life.Seed(h, BookingStatus.Confirmed, -2 * H);
        var r = await Dispute(h, id, Life.Client, null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Dispute(h, id, Life.Client, Body("other", " " + new string('b', 10) + " "))).StatusCode);
        var id2 = await Life.Seed(h, BookingStatus.Confirmed, -2 * H);
        Assert.Equal(HttpStatusCode.OK, (await Dispute(h, id2, Life.Client, Body("other", new string('c', 1000)))).StatusCode);
        Assert.Equal(1000, (await Life.Row(h, id2)).DisputeText!.Length);
    }

    [Fact]
    public async Task The_dispute_text_is_in_no_log_line_on_any_path()
    {
        await using var h = Life.Host(new RecordingProvider());
        const string marker = "demo-secret-marker-7f3a";
        var ok = await Life.Seed(h, BookingStatus.Confirmed, -2 * H);
        var late = await Life.Seed(h, BookingStatus.Confirmed, -100 * H);
        var other = await Life.Seed(h, BookingStatus.Completed, -2 * H);
        await Dispute(h, ok, Life.Client, Body("other", marker + " ok"));
        await Dispute(h, ok, Life.Client, Body("other", marker + " again"));       // 409
        await Dispute(h, late, Life.Client, Body("other", marker + " late"));       // 409
        await Dispute(h, other, Life.Client, Body("other", marker + " status"));    // 409
        await Dispute(h, ok, "demo-stranger", Body("other", marker + " stranger")); // 404
        await Dispute(h, ok, Life.Client, Body("bogus", marker + " invalid"));      // 422
        Assert.NotEmpty(h.Logs.Lines);
        Assert.DoesNotContain(h.Logs.Lines, l => l.Contains(marker));
    }
}
