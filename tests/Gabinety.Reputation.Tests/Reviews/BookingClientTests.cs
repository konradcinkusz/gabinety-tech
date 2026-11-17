using System.Net;
using Gabinety.Reputation.Clients;

namespace Gabinety.Reputation.Tests.Reviews;

public class BookingClientTests
{
    static readonly Guid Booking = Guid.NewGuid();

    static (BookingClient Client, FakeBooking Handler) Make()
    {
        var handler = new FakeBooking();
        return (new BookingClient(new HttpClient(handler) { BaseAddress = new Uri(FakeBooking.BaseUrl + "/") }, FakeBooking.Key, TimeSpan.Zero), handler);
    }

    [Fact]
    public async Task It_asks_the_reviewability_path_with_the_internal_key_and_reads_the_contract_record()
    {
        var (client, h) = Make();
        var pract = Guid.NewGuid();
        h.Bookings[Booking] = (true, "demo-user-1", pract);

        var dto = await client.GetReviewabilityAsync(Booking, default);

        Assert.Equal($"/internal/v1/bookings/{Booking}/reviewability", h.LastPath);
        Assert.Equal(FakeBooking.Key, h.LastKeyHeader);
        Assert.Equal((true, "demo-user-1", pract), (dto!.Reviewable, dto.ClientUserId, dto.PractitionerId));
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero), dto.CompletedAt);
    }

    [Fact]
    public async Task A_404_is_null_and_is_not_retried()
    {
        var (client, h) = Make();
        Assert.Null(await client.GetReviewabilityAsync(Booking, default));
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task A_5xx_is_retried_once_and_the_second_answer_counts()
    {
        var (client, h) = Make();
        h.Override = (_, n) => n == 1 ? new HttpResponseMessage(HttpStatusCode.BadGateway) : FakeBooking.Json(new { reviewable = true, clientUserId = "demo-u", practitionerId = Guid.NewGuid(), completedAt = (string?)null });
        Assert.True((await client.GetReviewabilityAsync(Booking, default))!.Reviewable);
        Assert.Equal(2, h.Calls);
    }

    [Fact]
    public async Task Two_failures_are_BookingUnavailable_after_exactly_two_calls_and_the_message_has_no_url_or_key()
    {
        var (client, h) = Make();
        h.Down = true;

        var ex = await Assert.ThrowsAsync<BookingUnavailableException>(() => client.GetReviewabilityAsync(Booking, default));

        Assert.Equal(2, h.Calls);
        Assert.DoesNotContain(FakeBooking.Key, ex.Message);
        Assert.DoesNotContain("demo.invalid", ex.Message);
    }

    [Fact]
    public async Task A_401_is_a_configuration_fault_and_is_not_retried()
    {
        var (client, h) = Make();
        h.Override = (_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        await Assert.ThrowsAsync<BookingUnavailableException>(() => client.GetReviewabilityAsync(Booking, default));
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task A_slow_answer_is_cut_at_the_timeout_and_counts_as_unavailable()
    {
        var slow = new SlowHandler();
        var c = new BookingClient(new HttpClient(slow) { BaseAddress = new Uri(FakeBooking.BaseUrl + "/") }, FakeBooking.Key, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<BookingUnavailableException>(() => c.GetReviewabilityAsync(Booking, default));

        Assert.Equal(2, slow.Calls);
    }

    sealed class SlowHandler : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
