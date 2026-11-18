extern alias BookingApp;

using System.Text.Json;
using System.Text.RegularExpressions;
using Gabinety.DemoSeeder;

namespace Gabinety.Integration.Tests.Seeder;

/// <summary>
/// T-08.01c: the Reputation step against in-process Identity, Directory, Booking and Reputation (InMemory). Expected values are written here, not read from the seeder's plan.
/// The first run is shared by the read-only tests; the tests that run again or break something use a world of their own.
/// </summary>
public sealed class ReputationSeedFixture : IAsyncLifetime
{
    public ReputationWorld World { get; } = new();
    CompletionJobStandIn _job = null!;

    public async Task InitializeAsync()
    {
        _ = World.Booking.Booking.Services;
        _job = new CompletionJobStandIn(World.Booking.Booking);
        await World.SeedDirectoryAndBookingAsync();
        await ReputationWorld.Step().RunAsync(World.Context, CancellationToken.None);
    }

    public async Task DisposeAsync() { await _job.DisposeAsync(); await World.DisposeAsync(); }
}

public class ReputationSeedStepTests(ReputationSeedFixture fx) : IClassFixture<ReputationSeedFixture>
{
    ReputationWorld _w => fx.World;

    // booking key -> (practitioner key, rating): the three Completed demo bookings and what each reviewer gave
    static readonly (string Booking, string Practitioner, int Rating)[] Expected =
    [
        ("demo-booking-completed-1", "demo-practitioner-1", 5),
        ("demo-booking-completed-2", "demo-practitioner-2", 4),
        ("demo-booking-completed-3", "demo-practitioner-5", 3),
    ];

    async Task<Dictionary<string, Booking>> CompletedBookingsByKey()
    {
        var rows = await _w.Booking.Booking.Bookings();
        return rows.Where(b => b.Status == Gabinety.Billing.BookingStatus.Completed)
            .ToDictionary(b => Regex.Match(b.BriefText, @"^\[(demo-[a-z0-9-]+)\]").Groups[1].Value, b => new Booking(b.Id, b.ClientUserId, b.PractitionerId));
    }

    sealed record Booking(Guid Id, string ClientUserId, Guid PractitionerId);

    [Fact]
    public async Task Exactly_one_review_exists_per_Completed_demo_booking_by_that_bookings_client_for_that_bookings_practitioner()
    {
        var completed = await CompletedBookingsByKey();
        Assert.Equal(3, completed.Count); // the premise: Booking made three Completed bookings (otherwise the failure is Booking's, and the log says why)
        var reviews = await _w.Reputation.Reviews();

        Assert.Equal(3, reviews.Count);
        foreach (var (key, practitionerKey, rating) in Expected)
        {
            var booking = completed[key];
            var review = Assert.Single(reviews, r => r.BookingId == booking.Id);
            Assert.Equal(booking.ClientUserId, review.ClientUserId);
            Assert.Equal(booking.PractitionerId, review.PractitionerId);
            Assert.Equal(_w.Context.Practitioners[practitionerKey].Id, review.PractitionerId);
            Assert.Equal(rating, review.Rating);
            Assert.Equal("Published", review.Status);
        }
    }

    [Fact]
    public async Task Reviews_are_readable_through_the_public_API_with_rating_and_a_Polish_text_and_no_reviewer_identity()
    {
        foreach (var (_, practitionerKey, rating) in Expected)
        {
            var items = await _w.PublishedFor(_w.Context.Practitioners[practitionerKey].Id);
            var item = Assert.Single(items);
            Assert.Equal(rating, item.GetProperty("rating").GetInt32());
            var text = item.GetProperty("text").GetString()!;
            Assert.InRange(text.Length, 20, 2000);
            Assert.DoesNotContain("demo", text, StringComparison.OrdinalIgnoreCase); // a visitor-facing text, no machine names in it
            Assert.False(item.TryGetProperty("clientUserId", out _));
        }
    }

    [Fact]
    public async Task The_rating_summaries_match_the_reviews()
    {
        var summaries = await _w.Reputation.Summaries();
        Assert.Equal(3, summaries.Count);
        foreach (var (_, practitionerKey, rating) in Expected)
        {
            var s = Assert.Single(summaries, x => x.Practitioner == _w.Context.Practitioners[practitionerKey].Id);
            Assert.Equal((1, rating, 1L), (s.Count, s.Sum, s.Version));
        }
    }

    [Fact]
    public async Task No_other_booking_is_reviewed_and_the_three_ratings_differ_so_the_averages_differ()
    {
        var completed = await CompletedBookingsByKey();
        var reviewed = (await _w.Reputation.Reviews()).Select(r => r.BookingId).ToHashSet();
        Assert.Subset(completed.Values.Select(c => c.Id).ToHashSet(), reviewed);
        Assert.Equal([3, 4, 5], Expected.Select(e => e.Rating).Order());
    }

    [Fact]
    public void The_log_carries_demo_keys_and_counts_but_no_review_text_no_key_and_no_token()
    {
        var log = _w.Log.Text;
        Assert.Contains("reputation: 3 demo reviews changes=3", log);
        Assert.DoesNotContain(ReputationHost.Key, log);
        Assert.DoesNotContain("Bearer", log, StringComparison.OrdinalIgnoreCase);
        foreach (var t in ReputationPlan.All.Select(r => r.Text)) Assert.DoesNotContain(t, log);
    }

    [Fact]
    public void Every_demo_review_is_in_the_demo_namespace_and_its_text_is_long_enough_and_trips_no_publication_rule()
    {
        Assert.Equal(3, ReputationPlan.All.Length);
        Assert.All(ReputationPlan.All, r =>
        {
            Assert.StartsWith("demo-", r.Key);
            Assert.StartsWith("demo-booking-completed-", r.BookingKey);
            Assert.InRange(r.Text.Length, 20, 2000);
            Assert.DoesNotMatch(@"https?://|www\.|@|\d{5,}", r.Text); // no link, no contact detail, no long digit run
        });
        Assert.Equal(3, ReputationPlan.All.Select(r => r.Text).Distinct().Count());
    }

    [Fact]
    public void The_step_runs_after_Booking_and_is_registered_once()
    {
        var names = SeedSteps.All.Select(s => s.Name).ToList();
        Assert.Equal(["directory", "booking", "reputation", "scale"], names);
        Assert.Equal(300, new ReputationSeedStep().Order);
    }

    [Fact]
    public async Task A_second_run_changes_nothing_in_Reputation_and_says_so()
    {
        await using var w = new ReputationWorld();
        await using var job = await StartedAsync(w);
        await w.SeedDirectoryAndBookingAsync();
        await ReputationWorld.Step().RunAsync(w.Context, CancellationToken.None);
        var reviews = await w.Reputation.Reviews();
        var summaries = await w.Reputation.Summaries();
        var events = await w.Reputation.ModerationEvents();
        Assert.Equal(3, reviews.Count);

        var second = w.NewContext();
        await new DirectorySeedStep().RunAsync(second, CancellationToken.None); // a real second run starts the same way: the context is rebuilt from the systems
        w.Calls.Requests.Clear();
        await ReputationWorld.Step().RunAsync(second, CancellationToken.None);

        Assert.Equal(reviews, await w.Reputation.Reviews()); // same ids, same texts, same timestamps
        Assert.Equal(summaries, await w.Reputation.Summaries());
        Assert.Equal(events, await w.Reputation.ModerationEvents());
        Assert.Contains("reputation: 3 demo reviews unchanged", w.Log.Text);
    }

    [Fact]
    public async Task A_run_that_stopped_after_the_first_review_is_completed_by_the_next_run_without_duplicates()
    {
        await using var w = new ReputationWorld();
        await using var job = await StartedAsync(w);
        await w.SeedDirectoryAndBookingAsync();
        var posts = 0;
        w.Calls.FailWhen = r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/api/v1/reviews" && ++posts == 2;

        await Assert.ThrowsAsync<HttpRequestException>(() => ReputationWorld.Step().RunAsync(w.Context, CancellationToken.None));
        Assert.Single(await w.Reputation.Reviews());

        w.Calls.FailWhen = null;
        await ReputationWorld.Step().RunAsync(w.Context, CancellationToken.None);

        var reviews = await w.Reputation.Reviews();
        Assert.Equal(3, reviews.Count);
        Assert.Equal(3, reviews.Select(r => r.BookingId).Distinct().Count());
        Assert.Equal(3, (await w.Reputation.Summaries()).Sum(s => s.Count));
        Assert.Contains("reputation: 3 demo reviews changes=2", w.Log.Text);
    }

    [Fact]
    public async Task Without_Completed_bookings_the_step_says_so_creates_nothing_and_does_not_fail()
    {
        await using var w = new ReputationWorld();
        await w.SeedDirectoryAndBookingAsync(withCompletionJob: false); // no completion job: the three stay Confirmed

        await ReputationWorld.Step().RunAsync(w.Context, CancellationToken.None);

        Assert.Empty(await w.Reputation.Reviews());
        Assert.Contains("reputation: no Completed demo booking", w.Log.Text);
    }

    [Fact]
    public async Task Without_a_Reputation_endpoint_the_step_says_so_and_does_nothing()
    {
        await using var w = new ReputationWorld();
        var options = new SeedOptions("http://localhost", DirectoryHost.Key, DirectoryHost.Authority, SeedOptions.DevIdentityPassword);
        var context = new SeedContext(options, w.Http, w.Log, TimeProvider.System);

        await ReputationWorld.Step().RunAsync(context, CancellationToken.None);

        Assert.Contains("reputation: skipped", w.Log.Text);
        Assert.Empty(w.Calls.Requests);
    }

    [Fact]
    public async Task A_Reputation_url_without_its_key_is_a_configuration_failure()
    {
        await using var w = new ReputationWorld();
        var options = new SeedOptions("http://localhost", DirectoryHost.Key, DirectoryHost.Authority, SeedOptions.DevIdentityPassword) { ReputationUrl = "http://localhost" };
        var context = new SeedContext(options, w.Http, w.Log, TimeProvider.System);

        var e = await Assert.ThrowsAsync<SeedException>(() => ReputationWorld.Step().RunAsync(context, CancellationToken.None));
        Assert.Contains("Internal:Keys:Reputation", e.Message);
    }

    [Fact]
    public void Options_read_the_Reputation_url_and_key_from_configuration()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Backend:Directory"] = "http://localhost:5101", ["Internal:Keys:Directory"] = "k", ["Identity:Authority"] = "http://localhost:5100",
            ["Backend:Reputation"] = "http://localhost:5103/, http://other", ["Internal:Keys:Reputation"] = " rk ",
        }).Build();

        var o = SeedOptions.From(config);

        Assert.Equal("http://localhost:5103", o.ReputationUrl);
        Assert.Equal("rk", o.ReputationKey);
    }

    static async Task<CompletionJobStandIn> StartedAsync(ReputationWorld w)
    {
        _ = w.Booking.Booking.Services;
        return new CompletionJobStandIn(w.Booking.Booking);
    }
}
