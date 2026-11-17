using System.Net;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Features.Internal.Erasure;
using Gabinety.Reputation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Reputation.Tests;

public class ErasureApiTests
{
    const string User = "demo-erase-user-7";
    static Task<HttpResponseMessage> Erase(InternalHost h, string user = User, string? key = InternalHost.Key) => h.Send(HttpMethod.Delete, $"/internal/v1/subjects/{user}", key);

    sealed record Seeded(Review Mine, Review Held, Review Other, Guid Practitioner);

    static async Task<Seeded> SeedAll(InternalHost h)
    {
        var p = Guid.NewGuid();
        Review mine = InternalHost.NewReview(p, User, 4, withExtras: true), held = InternalHost.NewReview(p, User, 2, ReviewStatus.Held), other = InternalHost.NewReview(p, "demo-someone-else", 5, withExtras: true);
        await h.Seed(db =>
        {
            db.Reviews.AddRange(mine, held, other);
            db.RatingSummaries.Add(new RatingSummary { PractitionerId = p, Count = 2, Sum = 9, Version = 5, PushedVersion = 5 });
            db.ModerationEvents.AddRange(
                InternalHost.NewEvent(mine.Id, ModerationEventKind.Created), InternalHost.NewEvent(mine.Id, ModerationEventKind.Rejected, InternalHost.Note, "demo-admin"),
                InternalHost.NewEvent(held.Id, ModerationEventKind.Held, "spam"), InternalHost.NewEvent(other.Id, ModerationEventKind.Rejected, InternalHost.Note, "demo-admin"));
        });
        return new(mine, held, other, p);
    }

    [Fact]
    public async Task Erasure_replaces_the_text_the_reply_and_the_notes_removes_the_user_id_and_keeps_the_rating_and_the_summary()
    {
        await using var h = InternalHost.Fake();
        var s = await SeedAll(h);

        Assert.Equal(HttpStatusCode.NoContent, (await Erase(h)).StatusCode);

        foreach (var (id, rating, status) in new[] { (s.Mine.Id, 4, ReviewStatus.Published), (s.Held.Id, 2, ReviewStatus.Held) })
        {
            var r = await h.ReviewOf(id);
            Assert.Equal(ErasureFeature.ErasedText, r.Text);
            Assert.Equal("Opinia usunięta na wniosek autora", r.Text);
            Assert.Equal(rating, r.Rating);
            Assert.Equal(status, r.Status);
            Assert.NotEqual(User, r.ClientUserId);
            Assert.DoesNotContain("erase-user", r.ClientUserId);
            Assert.StartsWith("demo-", r.ClientUserId); // a demo user stays in the demo namespace, so the demo reset still finds it
        }
        var mine = await h.ReviewOf(s.Mine.Id);
        Assert.Equal(ErasureFeature.ErasedText, mine.ReplyText);
        Assert.Equal(ErasureFeature.ErasedText, mine.ModerationNote);
        Assert.NotNull(mine.RepliedAt);
        Assert.Null((await h.ReviewOf(s.Held.Id)).ReplyText); // an absent reply and note stay absent
        Assert.Null((await h.ReviewOf(s.Held.Id)).ModerationNote);
        Assert.Equal(["spam"], (await h.ReviewOf(s.Held.Id)).HoldReasons);
        Assert.All(await h.EventsOf(s.Mine.Id), e => Assert.DoesNotContain("NOTATKA", e.Note ?? ""));
        Assert.Equal("spam", (await h.EventsOf(s.Held.Id)).Single().Note); // a reason code is not the user's text

        var sum = (await h.SummaryOf(s.Practitioner))!;
        Assert.Equal((2, 9, 5L, 5L), (sum.Count, sum.Sum, sum.Version, sum.PushedVersion)); // the rating still counts; nothing to push
    }

    [Fact]
    public async Task Other_reviewers_are_untouched()
    {
        await using var h = InternalHost.Fake();
        var s = await SeedAll(h);
        await Erase(h);
        var o = await h.ReviewOf(s.Other.Id);
        Assert.Equal((InternalHost.Text, InternalHost.Reply, InternalHost.Note, "demo-someone-else"), (o.Text, o.ReplyText, o.ModerationNote, o.ClientUserId));
        Assert.Equal(InternalHost.Note, (await h.EventsOf(s.Other.Id)).Single().Note);
    }

    [Fact]
    public async Task A_second_call_changes_nothing_and_an_unknown_user_is_204()
    {
        await using var h = InternalHost.Fake();
        var s = await SeedAll(h);
        await Erase(h);
        var before = await h.Query(db => db.Reviews.AsNoTracking().Where(r => r.PractitionerId == s.Practitioner).OrderBy(r => r.Id).ToListAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await Erase(h)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Erase(h, "demo-never-existed")).StatusCode);

        var after = await h.Query(db => db.Reviews.AsNoTracking().Where(r => r.PractitionerId == s.Practitioner).OrderBy(r => r.Id).ToListAsync());
        Assert.Equal(before.Select(r => (r.Id, r.ClientUserId, r.Text, r.ReplyText, r.ModerationNote, r.Rating)), after.Select(r => (r.Id, r.ClientUserId, r.Text, r.ReplyText, r.ModerationNote, r.Rating)));
    }

    [Fact]
    public async Task The_erased_review_is_still_listed_publicly_with_the_placeholder_and_its_rating()
    {
        await using var h = InternalHost.Fake();
        var s = await SeedAll(h);
        await Erase(h);
        var body = await (await h.CreateClient().GetAsync($"/api/v1/practitioners/{s.Practitioner}/reviews")).Body();
        var item = body.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == s.Mine.Id);
        Assert.Equal(ErasureFeature.ErasedText, item.GetProperty("text").GetString());
        Assert.Equal(4, item.GetProperty("rating").GetInt32());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Without_the_internal_key_the_endpoint_is_401_and_nothing_changes(string? key)
    {
        await using var h = new InternalHost(); // the hook is not a fake-profile endpoint: it exists in every environment
        var s = await SeedAll(h);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Erase(h, key: key)).StatusCode);
        Assert.Equal(InternalHost.Text, (await h.ReviewOf(s.Mine.Id)).Text);
    }

    [Fact]
    public async Task The_hook_works_outside_the_fake_profile()
    {
        await using var h = new InternalHost { Settings = { ["Environment:Kind"] = "Production" } };
        var s = await SeedAll(h);
        Assert.Equal(HttpStatusCode.NoContent, (await Erase(h)).StatusCode);
        Assert.Equal(ErasureFeature.ErasedText, (await h.ReviewOf(s.Mine.Id)).Text);
    }

    [Fact]
    public async Task No_text_reply_note_key_or_user_id_reaches_a_log()
    {
        await using var h = InternalHost.Fake();
        var s = await SeedAll(h);
        Assert.Equal(HttpStatusCode.NoContent, (await Erase(h)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Erase(h)).StatusCode);
        Assert.Equal(ErasureFeature.ErasedText, (await h.ReviewOf(s.Mine.Id)).Text); // the erasure really ran, so a clean log is not vacuous
        var lines = h.Logs.Lines.ToArray();
        Assert.NotEmpty(lines);
        Assert.DoesNotContain(lines, l => l.Contains("TEKST-NIE-DO-LOGU") || l.Contains("ODPOWIEDZ-NIE-DO-LOGU") || l.Contains("NOTATKA-NIE-DO-LOGU")
            || l.Contains(InternalHost.Key));
        Assert.DoesNotContain(lines, l => l.StartsWith("Gabinety.") && l.Contains(User)); // the framework's own request line carries the path; ours must not
    }
}
