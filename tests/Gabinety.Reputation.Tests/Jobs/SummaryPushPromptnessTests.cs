using Gabinety.Reputation.Jobs.SummaryPush;
using Gabinety.Reputation.Tests.Reviews;

namespace Gabinety.Reputation.Tests;

/// <summary>
/// The J3 flake: a review committed just after a tick of the 60 s timer reached the Directory a whole interval later, so a reader that waits 60 s missed it.
/// The push loop must run when a review is published, without any tick (these hosts never reach a real tick: the clock is manual and the interval is 60 s).
/// </summary>
public class SummaryPushPromptnessTests
{
    static async Task<List<ReputationPush>> WaitForPushes(ReviewsHost h, Guid practitioner, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var pushes = h.Directory.For(practitioner);
            if (pushes.Count >= count) return pushes;
            await Task.Delay(25);
        }
        return h.Directory.For(practitioner);
    }

    [Fact]
    public async Task A_published_review_is_pushed_to_Directory_without_waiting_for_the_next_tick()
    {
        using var h = new ReviewsHost();
        var user = "demo-" + Guid.NewGuid().ToString("N")[..12];
        var pract = Guid.NewGuid();
        var booking = h.Booking.Add(user, pract);

        var created = await ReviewsHost.Post(h.ClientFor(user), booking, rating: 4);
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);

        var push = Assert.Single(await WaitForPushes(h, pract, 1));
        Assert.Equal(new ReputationPush(1, 4m, 1), push);
        Assert.Equal(1, (await h.SummaryOf(pract))!.PushedVersion);
    }

    [Fact]
    public async Task A_held_review_changes_no_summary_and_pushes_nothing()
    {
        using var h = new ReviewsHost();
        var user = "demo-" + Guid.NewGuid().ToString("N")[..12];
        var pract = Guid.NewGuid();
        var booking = h.Booking.Add(user, pract);

        await ReviewsHost.Post(h.ClientFor(user), booking, rating: 4, text: "Kontakt: demo.osoba@example.invalid, napiszcie do mnie w sprawie spotkania.");
        await Task.Delay(300);

        Assert.Empty(h.Directory.For(pract));
    }
}
