using System.Net;
using Gabinety.Booking.Clients;
using Gabinety.Contracts.Directory;

namespace Gabinety.Booking.Tests;

public class DirectoryClientTests
{
    static DirectoryClient Make(FakeDirectory h, TimeSpan? timeout = null) =>
        new(new HttpClient(h) { BaseAddress = new Uri(FakeDirectory.BaseUrl) }, FakeDirectory.Key, retryDelay: TimeSpan.Zero, timeout: timeout);

    [Fact]
    public async Task GetOffering_returns_a_typed_result_and_sends_the_internal_key()
    {
        var offering = Guid.NewGuid();
        var practitioner = Guid.NewGuid();
        var h = new FakeDirectory
        {
            Override = (r, _) => r.RequestUri!.AbsolutePath == $"/internal/v1/offerings/{offering}"
                ? FakeDirectory.Json(new
                {
                    offeringId = offering, practitionerId = practitioner, practitionerUserId = "demo-user-7", practitionerStatus = "Approved",
                    title = "demo-offering", durationMinutes = 60, price = new { amountMinor = 15000, currency = "PLN" },
                    meetingUrl = "https://meet.demo.invalid/x", isActive = true,
                })
                : new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        var o = await Make(h).GetOfferingAsync(offering, default);
        Assert.NotNull(o);
        Assert.Equal(practitioner, o.PractitionerId);
        Assert.Equal("demo-user-7", o.PractitionerUserId);
        Assert.Equal(PractitionerStatus.Approved, o.PractitionerStatus);
        Assert.Equal(60, o.DurationMinutes);
        Assert.Equal(15000, o.Price.AmountMinor);
        Assert.True(o.IsActive);
        Assert.Equal(FakeDirectory.Key, h.LastKeyHeader);
    }

    [Fact]
    public async Task A_404_is_null_and_is_not_retried()
    {
        var h = new FakeDirectory();
        Assert.Null(await Make(h).GetOfferingAsync(Guid.NewGuid(), default));
        Assert.Null(await Make(h).GetPractitionerByUserAsync("demo-nobody", default));
        Assert.Equal(2, h.Calls);
    }

    [Fact]
    public async Task A_persistent_5xx_is_retried_exactly_once_then_throws_without_the_key_in_the_message()
    {
        var h = new FakeDirectory { Override = (_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError) };
        var ex = await Assert.ThrowsAsync<DirectoryUnavailableException>(() => Make(h).GetOfferingAsync(Guid.NewGuid(), default));
        Assert.Equal(2, h.Calls);
        Assert.DoesNotContain(FakeDirectory.Key, ex.ToString());
        Assert.DoesNotContain("directory.demo.invalid", ex.Message);
    }

    [Fact]
    public async Task A_hung_call_times_out_per_attempt_and_is_unavailable()
    {
        var h = new HangingHandler();
        var c = new DirectoryClient(new HttpClient(h) { BaseAddress = new Uri(FakeDirectory.BaseUrl) }, FakeDirectory.Key,
            retryDelay: TimeSpan.Zero, timeout: TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => c.GetPractitionerByUserAsync("demo-user-1", default));
        Assert.Equal(2, h.Calls);
    }

    [Fact]
    public async Task The_caller_cancelling_is_not_turned_into_unavailable()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Make(new FakeDirectory()).GetPractitionerByUserAsync("demo-user-1", cts.Token));
    }

    sealed class HangingHandler : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        }
    }
}
