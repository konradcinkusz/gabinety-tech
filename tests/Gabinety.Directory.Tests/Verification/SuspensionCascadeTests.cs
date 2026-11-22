using System.Net;
using System.Net.Http.Json;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Internal;
using Gabinety.Directory.Features.Verification;
using Gabinety.Directory.Tests.Internal;
using Gabinety.Directory.Tests.Profiles;

namespace Gabinety.Directory.Tests.Verification;

/// <summary>
/// ADR-0012 item 4: suspending a practitioner cancels their future bookings (Booking, full refund) and erasing one does the same first.
/// Booking is a double here; its own behaviour is tested in Gabinety.Booking.Tests.
/// </summary>
public class SuspensionCascadeTests(InternalFactory f) : IClassFixture<InternalFactory>
{
    const string Admin = "/api/v1/admin/practitioners", Reason = "Skarga klienta w trakcie wyjaśniania";

    static async Task<string?> Code(HttpResponseMessage r) => (await Vk.Problem(r)).Code;

    [Fact]
    public async Task Suspending_asks_Booking_once_to_cancel_that_practitioners_future_bookings_after_the_status_is_saved()
    {
        var p = await f.Seed(PractitionerStatus.Approved);
        PractitionerStatus? seenByBooking = null;
        f.Booking.AtCall = async id => seenByBooking = id == p.Id ? (await f.Load(p.Id)).Status : null;
        var before = f.Booking.Calls.Count(c => c == p.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await f.As("demo-admin", "Admin").PostAsJsonAsync($"{Admin}/{p.Id}/suspend", new { reason = Reason })).StatusCode);

        Assert.Equal(before + 1, f.Booking.Calls.Count(c => c == p.Id));
        Assert.Equal(PractitionerStatus.Suspended, seenByBooking); // Booking already refuses new bookings when it runs: no gap for a new one
        Assert.Equal(PractitionerStatus.Suspended, (await f.Load(p.Id)).Status);
        f.Booking.AtCall = null;
    }

    [Fact]
    public async Task When_Booking_fails_the_profile_stays_suspended_and_the_answer_says_the_cancellation_must_be_repeated()
    {
        var p = await f.Seed(PractitionerStatus.Approved);
        f.Booking.Down = true;
        try
        {
            var r = await f.As("demo-admin", "Admin").PostAsJsonAsync($"{Admin}/{p.Id}/suspend", new { reason = Reason });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
            Assert.Equal("dependency_unavailable", await Code(r));
        }
        finally { f.Booking.Down = false; }
        Assert.Equal(PractitionerStatus.Suspended, (await f.Load(p.Id)).Status);
        Assert.Equal([VerificationEventKind.Suspended], (await f.Events(p.Id)).Select(e => e.Kind));
    }

    [Theory]
    [InlineData("approve", PractitionerStatus.Submitted)]
    [InlineData("reject", PractitionerStatus.Submitted)]
    [InlineData("reinstate", PractitionerStatus.Suspended)]
    public async Task The_other_admin_actions_do_not_touch_bookings(string action, PractitionerStatus from)
    {
        var p = await f.Seed(from);
        HttpContent? body = action == "reject" ? JsonContent.Create(new { reason = Reason }) : null;
        Assert.Equal(HttpStatusCode.NoContent, (await f.As("demo-admin", "Admin").PostAsync($"{Admin}/{p.Id}/{action}", body)).StatusCode);
        Assert.DoesNotContain(p.Id, f.Booking.Calls);
    }

    [Fact]
    public async Task A_refused_or_unknown_suspension_does_not_call_Booking()
    {
        var draft = await f.Seed(PractitionerStatus.Draft);
        var c = f.As("demo-admin", "Admin");
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"{Admin}/{draft.Id}/suspend", new { reason = Reason })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync($"{Admin}/{draft.Id}/suspend", new { reason = "short" })).StatusCode);
        var unknown = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync($"{Admin}/{unknown}/suspend", new { reason = Reason })).StatusCode);
        Assert.DoesNotContain(draft.Id, f.Booking.Calls);
        Assert.DoesNotContain(unknown, f.Booking.Calls);
    }

    [Fact]
    public async Task Erasure_cancels_the_future_bookings_first_and_only_then_anonymises()
    {
        var p = await f.Seed(PractitionerStatus.Approved, x => x.DisplayName = "Demo Jan Kowalski");
        string? nameAtCall = null;
        f.Booking.AtCall = async id => { if (id == p.Id) nameAtCall = (await f.Load(p.Id)).DisplayName; };

        Assert.Equal(HttpStatusCode.NoContent, (await f.Internal().DeleteAsync($"/internal/v1/subjects/{p.UserId}")).StatusCode);
        f.Booking.AtCall = null;

        Assert.Equal("Demo Jan Kowalski", nameAtCall); // still whole when Booking ran: it needs the practitioner id, which survives, and nothing was lost first
        Assert.Contains(p.Id, f.Booking.Calls);
        Assert.Equal(InternalFeature.ErasedName, (await f.Load(p.Id)).DisplayName);
    }

    [Fact]
    public async Task Erasure_is_refused_with_503_and_leaves_the_row_when_Booking_cannot_cancel_and_a_retry_completes_it()
    {
        var p = await f.Seed(PractitionerStatus.Approved, x => x.DisplayName = "Demo Anna Nowak");
        f.Booking.Down = true;
        try
        {
            var r = await f.Internal().DeleteAsync($"/internal/v1/subjects/{p.UserId}");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        }
        finally { f.Booking.Down = false; }
        var kept = await f.Load(p.Id);
        Assert.Equal("Demo Anna Nowak", kept.DisplayName);
        Assert.Equal(p.UserId, kept.UserId);

        Assert.Equal(HttpStatusCode.NoContent, (await f.Internal().DeleteAsync($"/internal/v1/subjects/{p.UserId}")).StatusCode);
        Assert.Equal(InternalFeature.ErasedName, (await f.Load(p.Id)).DisplayName);
    }

    [Fact]
    public async Task Erasing_an_unknown_subject_or_one_that_was_erased_already_does_not_call_Booking()
    {
        var before = f.Booking.Calls.Count;
        Assert.Equal(HttpStatusCode.NoContent, (await f.Internal().DeleteAsync($"/internal/v1/subjects/demo-{Guid.NewGuid():n}")).StatusCode);
        Assert.Equal(before, f.Booking.Calls.Count);
    }
}
