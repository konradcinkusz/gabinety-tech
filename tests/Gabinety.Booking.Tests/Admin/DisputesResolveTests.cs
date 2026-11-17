using System.Net;
using Gabinety.Contracts.Booking;
using System.Text.Json;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Payments.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>POST /api/v1/admin/bookings/{id}/resolve (CONTRACTS 4.3, 8.1, 9.2). The price of the test host is 10000 grosz.</summary>
public class DisputesResolveTests
{
    static async Task<string> Code(HttpResponseMessage r) => (await r.Body()).GetProperty("code").GetString()!;

    public static TheoryData<BookingStatus> NotDisputed
    {
        get
        {
            var d = new TheoryData<BookingStatus>();
            foreach (var s in Enum.GetValues<BookingStatus>().Where(s => s != BookingStatus.Disputed)) d.Add(s);
            return d;
        }
    }

    [Theory]
    [MemberData(nameof(NotDisputed))]
    public async Task Every_status_but_Disputed_is_409_and_nothing_moves(BookingStatus status)
    {
        var provider = new RecordingProvider();
        await using var h = Adm.Host(provider);
        var (id, _) = await Adm.Seed(h, status);
        var r = await Adm.Resolve(h, id, Adm.Body("refund_full"));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("invalid_transition", await Code(r));
        Assert.Equal(status, (await Life.Row(h, id)).Status);
        Assert.Empty(provider.Refunds);
        Assert.Empty(await Life.RefundRows(h, id));
    }

    [Theory]
    [InlineData("refund_full", null)]
    [InlineData("refund_partial", 3000L)]
    public async Task A_Disputed_booking_whose_payment_is_already_refunded_is_409_without_calling_the_provider(string resolution, long? refundMinor)
    {
        var provider = new RecordingProvider();
        await using var h = Adm.Host(provider);
        var (id, _) = await Adm.Seed(h);
        await h.Seed(db =>
        {
            var p = db.Payments.Single(x => x.BookingId == id);
            p.Status = PaymentStatus.Refunded;
            p.RefundedMinor = p.AmountMinor;
        });
        var r = await Adm.Resolve(h, id, Adm.Body(resolution, refundMinor));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("invalid_transition", await Code(r));
        Assert.Empty(provider.Refunds);
        Assert.Equal(BookingStatus.Disputed, (await Life.Row(h, id)).Status);
    }

    [Fact]
    public async Task Unknown_id_is_404()
    {
        await using var h = Adm.Host(new RecordingProvider());
        var r = await Adm.Resolve(h, Guid.NewGuid(), Adm.Body("release"));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("not_found", await Code(r));
    }

    [Fact]
    public async Task Anonymous_is_401_and_a_non_admin_is_403_even_a_party_of_the_booking()
    {
        await using var h = Adm.Host(new RecordingProvider());
        var (id, client) = await Adm.Seed(h);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Adm.Resolve(h, id, Adm.Body("release"), as_: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Adm.Resolve(h, id, Adm.Body("release"), as_: client)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Adm.Resolve(h, id, Adm.Body("release"), as_: h.OwnerUserId)).StatusCode);
        Assert.Equal(BookingStatus.Disputed, (await Life.Row(h, id)).Status);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("SuperAdmin")]
    public async Task Both_admin_roles_may_resolve(string role)
    {
        await using var h = Adm.Host(new RecordingProvider());
        var (id, _) = await Adm.Seed(h);
        Assert.Equal(HttpStatusCode.OK, (await Adm.Resolve(h, id, Adm.Body("release"), role: role)).StatusCode);
    }

    public static TheoryData<string, long?> BadAmounts => new()
    {
        { "refund_partial", 10000 },   // equal to the price: that is refund_full
        { "refund_partial", 0 },
        { "refund_partial", -1 },
        { "refund_partial", 10001 },
        { "refund_partial", null },    // missing
        { "release", 5000 },           // an amount for a resolution that takes none
        { "refund_full", 10000 },
        { "release", 0 },
    };

    [Theory]
    [MemberData(nameof(BadAmounts))]
    public async Task A_wrong_refund_amount_is_400_validation_failed_and_nothing_moves(string resolution, long? refundMinor)
    {
        var provider = new RecordingProvider();
        await using var h = Adm.Host(provider);
        var (id, _) = await Adm.Seed(h);
        var r = await Adm.Resolve(h, id, Adm.Body(resolution, refundMinor));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("validation_failed", await Code(r));
        Assert.Equal(BookingStatus.Disputed, (await Life.Row(h, id)).Status);
        Assert.Empty(provider.Refunds);
    }

    [Theory]
    [InlineData("nonsense", "demo-resolution-note: ok enough")]
    [InlineData("", "demo-resolution-note: ok enough")]
    [InlineData("release", "too short")]            // 9 characters
    [InlineData("release", "          x          ")] // blank after trimming
    public async Task A_bad_resolution_or_note_is_400(string resolution, string note)
    {
        await using var h = Adm.Host(new RecordingProvider());
        var (id, _) = await Adm.Seed(h);
        var r = await Adm.Resolve(h, id, Adm.Body(resolution, note: note));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("validation_failed", await Code(r));
        Assert.Equal(BookingStatus.Disputed, (await Life.Row(h, id)).Status);
    }

    [Fact]
    public async Task The_note_is_10_to_500_characters_after_trimming()
    {
        await using var h = Adm.Host(new RecordingProvider());
        var (a, _) = await Adm.Seed(h);
        var (b, _) = await Adm.Seed(h);
        var (c, _) = await Adm.Seed(h);
        Assert.Equal(HttpStatusCode.BadRequest, (await Adm.Resolve(h, a, Adm.Body("release", note: new string('n', 501)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Adm.Resolve(h, b, Adm.Body("release", note: new string('n', 500)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Adm.Resolve(h, c, Adm.Body("release", note: "  " + new string('n', 10) + "  "))).StatusCode);
        Assert.Equal(new string('n', 10), (await Life.Row(h, c)).StatusReason);
    }

    [Fact]
    public async Task A_missing_body_is_400()
    {
        await using var h = Adm.Host(new RecordingProvider());
        var (id, _) = await Adm.Seed(h);
        var r = await Adm.Admin(h).PostAsync($"/api/v1/admin/bookings/{id}/resolve", null);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9999)]
    public async Task Partial_accepts_the_two_edges_1_and_price_minus_1(long refundMinor)
    {
        await using var h = Adm.Host(new RecordingProvider());
        var (id, _) = await Adm.Seed(h);
        var r = await Adm.Resolve(h, id, Adm.Body("refund_partial", refundMinor));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(refundMinor, (await Life.PaymentOf(h, id)).RefundedMinor);
    }

    [Fact]
    public async Task Release_refunds_nothing_and_the_payment_stays_Succeeded()
    {
        var provider = new RecordingProvider();
        await using var h = Adm.Host(provider);
        var (id, _) = await Adm.Seed(h);
        var r = await Adm.Resolve(h, id, Adm.Body("release", note: "  " + Adm.Note + "  "));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var row = await Life.Row(h, id);
        Assert.Equal(BookingStatus.Resolved, row.Status);
        Assert.Equal("release", row.DisputeResolution);
        Assert.Equal(h.Clock.GetUtcNow(), row.ResolvedAt);
        Assert.Equal(Adm.Note, row.StatusReason);
        var payment = await Life.PaymentOf(h, id);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(0, payment.RefundedMinor);
        Assert.Empty(provider.Refunds);
        Assert.Empty(await Life.RefundRows(h, id));
    }

    [Fact]
    public async Task Refund_full_refunds_the_price_once_and_a_second_resolve_is_409()
    {
        var provider = new RecordingProvider();
        await using var h = Adm.Host(provider);
        var (id, _) = await Adm.Seed(h, kind: "practitioner_no_show");
        var r = await Adm.Resolve(h, id, Adm.Body("refund_full"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Resolved", (await r.Body()).GetProperty("status").GetString());

        var row = await Life.Row(h, id);
        Assert.Equal("refund_full", row.DisputeResolution);
        Assert.Equal("practitioner_no_show", row.DisputeKind); // the stats push counts (Resolved, refund_full, practitioner_no_show)
        var refund = Assert.Single(provider.Refunds);
        Assert.Equal(h.PriceMinor, refund.AmountMinor);
        var payment = await Life.PaymentOf(h, id);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(h.PriceMinor, payment.RefundedMinor);
        var ledger = Assert.Single(await Life.RefundRows(h, id));
        Assert.Equal(h.PriceMinor, ledger.AmountMinor);

        Assert.Equal(HttpStatusCode.Conflict, (await Adm.Resolve(h, id, Adm.Body("refund_full"))).StatusCode);
        Assert.Single(provider.Refunds);
        Assert.Single(await Life.RefundRows(h, id));
    }

    [Fact]
    public async Task Refund_partial_leaves_PartiallyRefunded_with_the_amount_and_one_ledger_row()
    {
        var provider = new RecordingProvider();
        await using var h = Adm.Host(provider);
        var (id, _) = await Adm.Seed(h);
        Assert.Equal(HttpStatusCode.OK, (await Adm.Resolve(h, id, Adm.Body("refund_partial", 3000))).StatusCode);
        var row = await Life.Row(h, id);
        Assert.Equal("refund_partial", row.DisputeResolution);
        Assert.Equal(3000, Assert.Single(provider.Refunds).AmountMinor);
        var payment = await Life.PaymentOf(h, id);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(3000, payment.RefundedMinor);
        Assert.Equal(3000, Assert.Single(await Life.RefundRows(h, id)).AmountMinor);
    }

    [Theory]
    [InlineData("refund_partial", 3000L, 3000L)]
    [InlineData("refund_full", null, 10000L)]
    [InlineData("release", null, 0L)]
    public async Task A_hook_sees_payment_RefundedMinor_already_set_when_it_runs(string resolution, long? refundMinor, long expected)
    {
        var hook = new RecordingHook();
        await using var h = Adm.Host(new RecordingProvider(), hook);
        var (id, _) = await Adm.Seed(h);
        Assert.Equal(HttpStatusCode.OK, (await Adm.Resolve(h, id, Adm.Body(resolution, refundMinor))).StatusCode);
        var seen = Assert.Single(hook.Seen, s => s.BookingId == id);
        Assert.Equal("AdminResolves", seen.Trigger);
        Assert.Equal(expected, seen.RefundedMinor);
    }

    [Fact]
    public async Task With_an_asynchronous_provider_the_refund_is_asked_for_but_recorded_only_at_confirmation()
    {
        var provider = new RecordingProvider("Stripe");
        var hook = new RecordingHook();
        await using var h = Adm.Host(provider, hook);
        var (id, _) = await Adm.Seed(h, provider: "Stripe");
        Assert.Equal(HttpStatusCode.OK, (await Adm.Resolve(h, id, Adm.Body("refund_partial", 3000))).StatusCode);
        Assert.Equal(3000, Assert.Single(provider.Refunds).AmountMinor);
        Assert.Equal(BookingStatus.Resolved, (await Life.Row(h, id)).Status);
        Assert.Equal(PaymentStatus.Succeeded, (await Life.PaymentOf(h, id)).Status); // the RefundSucceeded webhook records it
        Assert.Empty(await Life.RefundRows(h, id));
        Assert.Equal(0, Assert.Single(hook.Seen, s => s.BookingId == id).RefundedMinor);
    }

    [Fact]
    public async Task A_provider_that_is_down_gives_503_and_the_booking_stays_Disputed()
    {
        await using var h = Adm.Host(new RecordingProvider());
        var previous = h.Configure!;
        h.Configure = s => { previous(s); s.RemoveAll<IPaymentProvider>(); s.AddSingleton<IPaymentProvider>(new DownProvider()); };
        var (id, _) = await Adm.Seed(h);
        var r = await Adm.Resolve(h, id, Adm.Body("refund_full"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal(BookingStatus.Disputed, (await Life.Row(h, id)).Status);
        Assert.Empty(await Life.RefundRows(h, id));
    }

    sealed class DownProvider : IPaymentProvider
    {
        public string Name => "Mock";
        public Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest r, CancellationToken ct) => throw new HttpRequestException("down");
        public Task<RefundResult> RefundAsync(RefundRequest r, CancellationToken ct) => throw new HttpRequestException("down");
        public Task<PayoutResult> PayoutAsync(PayoutRequest r, CancellationToken ct) => throw new HttpRequestException("down");
        public Gabinety.Booking.Payments.Abstractions.WebhookEvent? VerifyWebhook(ReadOnlySpan<byte> rawBody, string? sig) => null;
    }

    [Fact]
    public async Task The_response_is_the_booking_view_without_the_dispute_text_or_the_note_and_neither_is_logged()
    {
        await using var h = Adm.Host(new RecordingProvider());
        var (id, client) = await Adm.Seed(h);
        // a dispute that fails its own checks and a refused resolve must not log the text either
        await Adm.Resolve(h, id, Adm.Body("refund_partial", 0));
        var r = await Adm.Resolve(h, id, Adm.Body("refund_partial", 4000));
        var raw = await r.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(id, JsonDocument.Parse(raw).RootElement.GetProperty("id").GetGuid());
        Assert.Equal("PartiallyRefunded", JsonDocument.Parse(raw).RootElement.GetProperty("payment").GetProperty("status").GetString());
        Assert.DoesNotContain("demo-resolution-note", raw);
        Assert.DoesNotContain("demo-dispute-text", raw);
        await Adm.Resolve(h, id, Adm.Body("release")); // 409
        await Adm.List(h, "?status=resolved");

        // the party's own view of the booking never carries the dispute text
        var own = await h.ClientFor(client).GetAsync($"/api/v1/bookings/{id}");
        Assert.DoesNotContain("demo-dispute-text", await own.Content.ReadAsStringAsync());

        foreach (var line in Adm.LogLines(h))
        {
            Assert.DoesNotContain("demo-dispute-text", line);
            Assert.DoesNotContain("demo-resolution-note", line);
        }
        Assert.Contains(Adm.LogLines(h), l => l.Contains("AdminResolves")); // the transition itself is logged, with ids only
    }
}
