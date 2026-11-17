extern alias BookingApp;
extern alias DirectoryApp;

using DirectoryApp::Gabinety.Directory.Persistence;
using Gabinety.DemoSeeder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Integration.Tests.Seeder;

/// <summary>T-08.02: <c>--reset</c> calls every step's ResetAsync (Reputation, Booking, Directory, in that order) and leaves a non-demo row alone.</summary>
public class SeederResetTests
{
    [Theory]
    [InlineData(new string[0], SeedMode.Seed)]
    [InlineData(new[] { "--reset" }, SeedMode.ResetThenSeed)]
    [InlineData(new[] { "--reset-only" }, SeedMode.ResetOnly)]
    [InlineData(new[] { "--Backend:Directory=http://localhost:5101", "--reset", "--i-know-this-is-demo" }, SeedMode.ResetThenSeed)]
    public void The_reset_flags_pick_the_mode_and_never_reach_the_configuration(string[] args, SeedMode expected)
    {
        var (mode, rest) = SeederApp.ParseMode(args);
        Assert.Equal(expected, mode);
        Assert.DoesNotContain(rest, a => a.StartsWith("--reset", StringComparison.Ordinal));
        Assert.Equal(args.Count(a => !a.StartsWith("--reset", StringComparison.Ordinal)), rest.Length); // everything else is kept
    }

    static async Task<int> DirectoryPractitioners(DirectoryHost directory)
    {
        using var scope = directory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DirectoryDbContext>().Practitioners.CountAsync();
    }

    static Task NonDemoRows(ReputationWorld w) => w.Booking.Booking.Db(async db =>
    {
        db.Bookings.Add(new()
        {
            Id = Guid.NewGuid(), ClientUserId = "real-client", PractitionerId = Guid.NewGuid(), PractitionerUserId = "real-practitioner", OfferingId = Guid.NewGuid(),
            OfferingTitle = "real", DurationMinutes = 60, PriceMinor = 10000, StartUtc = DateTimeOffset.UtcNow.AddDays(4), EndUtc = DateTimeOffset.UtcNow.AddDays(4).AddHours(1),
            Status = Gabinety.Billing.BookingStatus.Confirmed, BriefText = "real", IdempotencyKey = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return 0;
    });

    [Fact]
    public async Task Reset_only_removes_the_seeded_data_of_all_three_systems_and_a_non_demo_row_survives_then_reset_and_seed_brings_it_back()
    {
        await using var w = new ReputationWorld();
        _ = w.Booking.Booking.Services;
        await using var job = new CompletionJobStandIn(w.Booking.Booking);
        await w.SeedDirectoryAndBookingAsync();
        await ReputationWorld.Step().RunAsync(w.Context, CancellationToken.None);
        await NonDemoRows(w);
        var realReview = Guid.NewGuid();
        await w.Reputation.Db(async db =>
        {
            db.Reviews.Add(new() { Id = realReview, BookingId = Guid.NewGuid(), PractitionerId = Guid.NewGuid(), ClientUserId = "real-client", Rating = 5, Text = "real", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            return 0;
        });
        Assert.Equal(3, (await w.Reputation.Reviews()).Count(r => r.ClientUserId.StartsWith("demo-")));
        Assert.Equal(12, (await w.Booking.Booking.Bookings()).Count(b => b.ClientUserId.StartsWith("demo-")));
        Assert.Equal(10, await DirectoryPractitioners(w.Booking.Directory));

        var code = await SeederApp.RunAsync(w.WithReputation, w.Http, w.Log, TimeProvider.System, CancellationToken.None, mode: SeedMode.ResetOnly);

        Assert.Equal(0, code);
        Assert.Equal([realReview], (await w.Reputation.Reviews()).Select(r => r.Id));
        Assert.Equal(["real-client"], (await w.Booking.Booking.Bookings()).Select(b => b.ClientUserId));
        Assert.Equal(0, await DirectoryPractitioners(w.Booking.Directory));
        var log = w.Log.Text;
        Assert.True(log.IndexOf("reset reputation", StringComparison.Ordinal) is var r && r >= 0
            && log.IndexOf("reset booking", StringComparison.Ordinal) is var b && b > r
            && log.IndexOf("reset directory", StringComparison.Ordinal) > b, "reset runs Reputation, Booking, Directory in that order:\n" + log);
        Assert.DoesNotContain("step directory: start", log[log.IndexOf("reset reputation", StringComparison.Ordinal)..]); // reset only: nothing is seeded again

        // --reset: reset, then seed from scratch
        code = await SeederApp.RunAsync(w.WithReputation, w.Http, w.Log, TimeProvider.System, CancellationToken.None, mode: SeedMode.ResetThenSeed);

        Assert.True(code == 0, w.Log.Text);
        Assert.Equal(10, await DirectoryPractitioners(w.Booking.Directory));
        Assert.Equal(12, (await w.Booking.Booking.Bookings()).Count(x => x.ClientUserId.StartsWith("demo-")));
        Assert.Equal(1, (await w.Booking.Booking.Bookings()).Count(x => x.ClientUserId == "real-client"));
        Assert.Equal(3, (await w.Reputation.Reviews()).Count(x => x.ClientUserId.StartsWith("demo-")));
        Assert.Contains(realReview, (await w.Reputation.Reviews()).Select(x => x.Id));
    }

    [Fact]
    public async Task A_step_without_its_endpoint_is_skipped_by_the_reset_and_says_so()
    {
        await using var w = new BookingWorld();
        await w.SeedDirectoryAsync();
        var directoryOnly = new SeedOptions("http://localhost", DirectoryHost.Key, DirectoryHost.Authority, SeedOptions.DevIdentityPassword);

        var code = await SeederApp.RunAsync(directoryOnly, w.Http, w.Log, TimeProvider.System, CancellationToken.None, mode: SeedMode.ResetOnly);

        Assert.Equal(0, code);
        Assert.Contains("reset booking: skipped", w.Log.Text);
        Assert.Contains("reset reputation: skipped", w.Log.Text);
        Assert.Contains("reset directory: removed", w.Log.Text);
    }
}
