using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Persistence;

namespace Gabinety.Reputation.Tests.Moderation;

public class ModerationTests
{
    const string Admin = "Admin";

    static async Task<string> Code(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()!;

    // ---- list

    [Fact]
    public async Task The_list_defaults_to_Held_and_status_selects_the_others_oldest_first()
    {
        using var h = new ModerationHost();
        var p = Guid.NewGuid(); var t = h.Clock.GetUtcNow();
        var held1 = ModerationHost.NewReview(p, ReviewStatus.Held, at: t.AddMinutes(-20));
        var held2 = ModerationHost.NewReview(p, ReviewStatus.Held, at: t.AddMinutes(-10));
        var pub = ModerationHost.NewReview(p, ReviewStatus.Published, at: t.AddMinutes(-5));
        var rej = ModerationHost.NewReview(p, ReviewStatus.Rejected, at: t.AddMinutes(-4));
        await h.Seed(p, held2, pub, held1, rej);
        var admin = h.As("demo-admin", Admin);

        var def = await ModerationHost.ListAll(admin, p);
        Assert.Equal([held1.Id, held2.Id], def.Select(i => i.GetProperty("id").GetGuid()));
        Assert.Equal([pub.Id], (await ModerationHost.ListAll(admin, p, "Published")).Select(i => i.GetProperty("id").GetGuid()));
        Assert.Equal([rej.Id], (await ModerationHost.ListAll(admin, p, "Rejected")).Select(i => i.GetProperty("id").GetGuid()));
        var first = def[0];
        Assert.Equal("Held", first.GetProperty("status").GetString());
        Assert.Equal(ModerationHost.ReviewText, first.GetProperty("text").GetString());
        Assert.Equal("spam", first.GetProperty("holdReasons")[0].GetString());
        Assert.False(first.TryGetProperty("clientUserId", out _)); // the reviewer's identity is not needed to moderate
    }

    [Fact]
    public async Task The_list_pages_with_a_cursor_and_rejects_a_bad_status_or_cursor()
    {
        using var h = new ModerationHost();
        var p = Guid.NewGuid(); var t = h.Clock.GetUtcNow();
        await h.Seed(p, Enumerable.Range(0, 5).Select(i => ModerationHost.NewReview(p, ReviewStatus.Held, at: t.AddMinutes(-i))).ToArray());
        var admin = h.As("demo-admin", Admin);

        Assert.Equal(5, (await ModerationHost.ListAll(admin, p)).Count);
        var bad = await admin.GetAsync("/api/v1/admin/reviews?status=Nope");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/admin/reviews?cursor=%21%21")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.GetAsync("/api/v1/admin/reviews?status=1")).StatusCode);
    }

    // ---- authorisation

    [Fact]
    public async Task Without_the_Admin_role_every_moderation_route_is_403_and_without_a_token_401_and_nothing_changes()
    {
        using var h = new ModerationHost();
        var p = Guid.NewGuid();
        var held = ModerationHost.NewReview(p, ReviewStatus.Held); var pub = ModerationHost.NewReview(p, ReviewStatus.Published, 4);
        await h.Seed(p, held, pub);

        foreach (var who in new[] { h.As("demo-client"), h.As("demo-practitioner", "Practitioner"), h.As("demo-moderator", "Moderator") })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await who.GetAsync("/api/v1/admin/reviews")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ModerationHost.Publish(who, held.Id)).StatusCode);
            var reject = await ModerationHost.Reject(who, pub.Id); // the reviewed practitioner cannot remove a review about themselves
            Assert.Equal(HttpStatusCode.Forbidden, reject.StatusCode);
        }
        var anon = h.As(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/admin/reviews")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ModerationHost.Reject(anon, pub.Id)).StatusCode);

        Assert.Equal(ReviewStatus.Held, (await h.ReviewOf(held.Id)).Status);
        Assert.Equal(ReviewStatus.Published, (await h.ReviewOf(pub.Id)).Status);
        Assert.Equal((1, 4), ((await h.SummaryOf(p))!.Count, (await h.SummaryOf(p))!.Sum));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("SuperAdmin")]
    public async Task Both_admin_roles_may_moderate(string role)
    {
        using var h = new ModerationHost();
        var p = Guid.NewGuid(); var held = ModerationHost.NewReview(p, ReviewStatus.Held);
        await h.Seed(p, held);
        Assert.Equal(HttpStatusCode.NoContent, (await ModerationHost.Publish(h.As("demo-admin", role), held.Id)).StatusCode);
    }

    // ---- state machine

    [Fact]
    public async Task Publish_works_from_Held_only()
    {
        using var h = new ModerationHost();
        var p = Guid.NewGuid();
        var held = ModerationHost.NewReview(p, ReviewStatus.Held, 4); var pub = ModerationHost.NewReview(p, ReviewStatus.Published, 5); var rej = ModerationHost.NewReview(p, ReviewStatus.Rejected, 1);
        await h.Seed(p, held, pub, rej);
        var admin = h.As("demo-admin", Admin);
        var before = await h.SummaryOf(p);

        foreach (var id in new[] { pub.Id, rej.Id }) // Published again, or Rejected (final)
        {
            var r = await ModerationHost.Publish(admin, id);
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("invalid_transition", await Code(r));
        }
        var s = await h.SummaryOf(p);
        Assert.Equal((before!.Count, before.Sum, before.Version), (s!.Count, s.Sum, s.Version)); // refused: nothing moved

        h.Clock.Advance(TimeSpan.FromMinutes(7));
        Assert.Equal(HttpStatusCode.NoContent, (await ModerationHost.Publish(admin, held.Id)).StatusCode);
        var now = await h.ReviewOf(held.Id);
        Assert.Equal(ReviewStatus.Published, now.Status);
        Assert.Equal(h.Clock.GetUtcNow(), now.PublishedAt);
        Assert.Equal("demo-admin", now.ModeratedBy);
        s = await h.SummaryOf(p);
        Assert.Equal((2, 9, before.Version + 1), (s!.Count, s.Sum, s.Version));
    }

    [Fact]
    public async Task Reject_works_from_Held_or_Published_and_Rejected_is_final()
    {
        using var h = new ModerationHost();
        var p = Guid.NewGuid();
        var held = ModerationHost.NewReview(p, ReviewStatus.Held, 2); var pub = ModerationHost.NewReview(p, ReviewStatus.Published, 4); var keep = ModerationHost.NewReview(p, ReviewStatus.Published, 5);
        await h.Seed(p, held, pub, keep);
        var admin = h.As("demo-admin", Admin);
        var v0 = (await h.SummaryOf(p))!.Version;

        Assert.Equal(HttpStatusCode.NoContent, (await ModerationHost.Reject(admin, held.Id, "Spam reklamowy")).StatusCode);
        var s = (await h.SummaryOf(p))!;
        Assert.Equal((2, 9, v0), (s.Count, s.Sum, s.Version)); // a Held review was never counted: nothing to remove, no change, no version bump

        Assert.Equal(HttpStatusCode.NoContent, (await ModerationHost.Reject(admin, pub.Id, "Opinia zawiera dane osobowe")).StatusCode);
        s = (await h.SummaryOf(p))!;
        Assert.Equal((1, 5, v0 + 1), (s.Count, s.Sum, s.Version)); // removed from count and sum, version incremented

        var rejected = await h.ReviewOf(pub.Id);
        Assert.Equal(ReviewStatus.Rejected, rejected.Status);
        Assert.Equal("Opinia zawiera dane osobowe", rejected.ModerationNote);
        Assert.Equal("demo-admin", rejected.ModeratedBy);

        foreach (var attempt in new[] { await ModerationHost.Reject(admin, held.Id), await ModerationHost.Publish(admin, held.Id) }) // Rejected is final
        {
            Assert.Equal(HttpStatusCode.Conflict, attempt.StatusCode);
            Assert.Equal("invalid_transition", await Code(attempt));
        }
        s = (await h.SummaryOf(p))!;
        Assert.Equal((1, 5, v0 + 1), (s.Count, s.Sum, s.Version));
    }

    [Fact]
    public async Task Reject_needs_a_reason_and_an_unknown_review_is_404()
    {
        using var h = new ModerationHost();
        var p = Guid.NewGuid(); var held = ModerationHost.NewReview(p, ReviewStatus.Held);
        await h.Seed(p, held);
        var admin = h.As("demo-admin", Admin);

        foreach (var reason in new[] { null, "", "   ", "Za krótki", "   Za krótki   ", new string('x', 501) })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ModerationHost.Reject(admin, held.Id, reason)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsync($"/api/v1/admin/reviews/{held.Id}/reject", null)).StatusCode);
        Assert.Equal(ReviewStatus.Held, (await h.ReviewOf(held.Id)).Status);

        // the boundaries match practitioner verification (CONTRACTS 3.4): 10 and 500 characters after trimming are accepted
        Assert.Equal(HttpStatusCode.NoContent, (await ModerationHost.Reject(admin, held.Id, "  " + new string('x', 10) + "  ")).StatusCode);
        var other = ModerationHost.NewReview(p, ReviewStatus.Held);
        await h.Seed(p, other);
        Assert.Equal(HttpStatusCode.NoContent, (await ModerationHost.Reject(admin, other.Id, new string('x', 500))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await ModerationHost.Publish(admin, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ModerationHost.Reject(admin, Guid.NewGuid())).StatusCode);
    }

    // ---- idempotency and races

    [Fact]
    public async Task A_repeated_decision_is_409_and_changes_nothing()
    {
        using var h = new ModerationHost();
        var p = Guid.NewGuid(); var held = ModerationHost.NewReview(p, ReviewStatus.Held, 3);
        await h.Seed(p, held);
        var admin = h.As("demo-admin", Admin);

        Assert.Equal(HttpStatusCode.NoContent, (await ModerationHost.Publish(admin, held.Id)).StatusCode);
        var s1 = (await h.SummaryOf(p))!; var ev1 = (await h.EventsOf(held.Id)).Count;
        Assert.Equal(HttpStatusCode.Conflict, (await ModerationHost.Publish(admin, held.Id)).StatusCode);
        var s2 = (await h.SummaryOf(p))!;
        Assert.Equal((s1.Count, s1.Sum, s1.Version), (s2.Count, s2.Sum, s2.Version));
        Assert.Equal(ev1, (await h.EventsOf(held.Id)).Count);
    }

    [Fact]
    public async Task Two_admins_deciding_at_once_one_wins_and_the_summary_moves_once()
    {
        using var h = new ModerationHost();
        var p = Guid.NewGuid(); var held = ModerationHost.NewReview(p, ReviewStatus.Held, 5);
        await h.Seed(p, held);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => ModerationHost.Publish(h.As($"demo-admin-{i}", Admin), held.Id)));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var s = (await h.SummaryOf(p))!;
        Assert.Equal((1, 5, 1L), (s.Count, s.Sum, s.Version));
        Assert.Single(await h.EventsOf(held.Id), e => e.Kind == ModerationEventKind.Published);
    }

    // ---- the summary after any sequence (property-style, seeded so a failure reproduces)

    [Theory]
    [InlineData(20260101)]
    [InlineData(20260102)]
    [InlineData(20260103)]
    [InlineData(20260104)]
    [InlineData(20260105)]
    [InlineData(20260106)]
    public async Task The_summary_equals_a_recount_after_every_step_of_a_random_sequence(int seed)
    {
        var rnd = new Random(seed);
        for (var round = 0; round < 5; round++)
        {
            using var h = new ModerationHost(); // the shared limiter (60 per minute, partitioned before authentication) is per host: one host per round
            var p = Guid.NewGuid();
            var reviews = Enumerable.Range(0, 2 + rnd.Next(5))
                .Select(_ => ModerationHost.NewReview(p, rnd.Next(2) == 0 ? ReviewStatus.Held : ReviewStatus.Published, 1 + rnd.Next(5))).ToArray();
            await h.Seed(p, reviews);
            var model = reviews.ToDictionary(r => r.Id, r => r.Status);
            var version = (await h.SummaryOf(p))?.Version ?? 0;

            for (var step = 0; step < 14; step++)
            {
                var admin = h.As("demo-admin", Admin);
                var target = reviews[rnd.Next(reviews.Length)];
                var publish = rnd.Next(2) == 0;
                var from = model[target.Id];
                var legal = publish ? from == ReviewStatus.Held : from is ReviewStatus.Held or ReviewStatus.Published;
                var response = publish ? await ModerationHost.Publish(admin, target.Id) : await ModerationHost.Reject(admin, target.Id);

                Assert.Equal(legal ? HttpStatusCode.NoContent : HttpStatusCode.Conflict, response.StatusCode);
                var countedBefore = from == ReviewStatus.Published;
                if (legal) model[target.Id] = publish ? ReviewStatus.Published : ReviewStatus.Rejected;
                var countedAfter = model[target.Id] == ReviewStatus.Published;
                if (countedBefore != countedAfter) version++; // the version moves exactly when the published set did

                var recount = await h.Recount(p);
                var s = await h.SummaryOf(p);
                Assert.Equal(recount, (s?.Count ?? 0, s?.Sum ?? 0));
                Assert.Equal(version, s?.Version ?? 0);
                Assert.Equal(model.Values.Count(x => x == ReviewStatus.Published), recount.Count);
            }
        }
    }

    // ---- the public sees the decision

    [Fact]
    public async Task A_moderation_decision_changes_what_the_public_list_shows()
    {
        using var h = new ModerationHost();
        var p = Guid.NewGuid(); var held = ModerationHost.NewReview(p, ReviewStatus.Held); var pub = ModerationHost.NewReview(p, ReviewStatus.Published);
        await h.Seed(p, held, pub);
        var admin = h.As("demo-admin", Admin); var anon = h.As(null);

        async Task<List<Guid>> Public() => (await anon.GetFromJsonAsync<Page<ReviewDto>>($"/api/v1/practitioners/{p}/reviews", new JsonSerializerOptions(JsonSerializerDefaults.Web)))!.Items.Select(i => i.Id).ToList();
        Assert.Equal([pub.Id], await Public());
        await ModerationHost.Publish(admin, held.Id);
        Assert.Equal(2, (await Public()).Count);
        await ModerationHost.Reject(admin, pub.Id);
        Assert.Equal([held.Id], await Public());
    }

    // ---- the event log

    [Fact]
    public async Task Each_decision_appends_one_event_and_earlier_events_never_change()
    {
        using var h = new ModerationHost();
        var p = Guid.NewGuid(); var held = ModerationHost.NewReview(p, ReviewStatus.Held);
        await h.Seed(p, held);
        var admin = h.As("demo-admin-7", Admin);

        var log = await h.EventsOf(held.Id);
        Assert.Equal([ModerationEventKind.Created], log.Select(e => e.Kind));

        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await ModerationHost.Publish(admin, held.Id);
        var afterPublish = await h.EventsOf(held.Id);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await ModerationHost.Reject(admin, held.Id, "Opinia nieprawdziwa");
        await ModerationHost.Reject(admin, held.Id, "Powtórzenie"); // refused: no event
        await ModerationHost.Publish(admin, held.Id);               // refused: no event
        var afterReject = await h.EventsOf(held.Id);

        Assert.Equal([ModerationEventKind.Created, ModerationEventKind.Published], afterPublish.Select(e => e.Kind));
        Assert.Equal([ModerationEventKind.Created, ModerationEventKind.Published, ModerationEventKind.Rejected], afterReject.Select(e => e.Kind));
        for (var i = 0; i < log.Count; i++) Assert.Equivalent(log[i], afterReject[i]); // append-only: the old rows are untouched
        for (var i = 0; i < afterPublish.Count; i++) Assert.Equivalent(afterPublish[i], afterReject[i]);
        var rejected = afterReject[^1];
        Assert.Equal("demo-admin-7", rejected.Actor);
        Assert.Equal("Opinia nieprawdziwa", rejected.Note);
        Assert.Equal(h.Clock.GetUtcNow(), rejected.At);
        Assert.Equal("demo-admin-7", afterPublish[^1].Actor);
    }

    [Fact]
    public void The_moderation_code_has_no_path_that_updates_or_deletes_an_event()
    {
        var src = Directory.GetFiles(FindDir("src/Gabinety.Reputation/Features/Moderation"), "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(src);
        foreach (var file in src)
        {
            var code = File.ReadAllText(file);
            Assert.DoesNotContain("ModerationEvents.Remove", code);
            Assert.DoesNotContain("ModerationEvents.Update", code);
            Assert.DoesNotContain("ModerationEvents.RemoveRange", code);
            Assert.DoesNotContain("ModerationEvents.ExecuteDelete", code);
            Assert.DoesNotContain("ModerationEvents.ExecuteUpdate", code);
            Assert.DoesNotContain("moderation_events\" SET", code);
            Assert.DoesNotContain("DELETE FROM moderation_events", code);
        }
    }

    static string FindDir(string relative)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, relative))) return Path.Combine(d.FullName, relative);
        return Path.Combine(AppContext.BaseDirectory, relative); // missing: the NotEmpty assertion fails with a clear signal
    }

    // ---- free text never leaves the database

    [Fact]
    public async Task Review_text_and_moderator_notes_reach_no_log_and_no_response_but_the_list()
    {
        using var h = new ModerationHost();
        var p = Guid.NewGuid(); var held = ModerationHost.NewReview(p, ReviewStatus.Held); var pub = ModerationHost.NewReview(p, ReviewStatus.Published);
        await h.Seed(p, held, pub);
        var admin = h.As("demo-admin", Admin);
        const string note = "NOTATKA-MODERATORA-NIE-DO-LOGU";

        var publish = await ModerationHost.Publish(admin, held.Id);
        var reject = await ModerationHost.Reject(admin, pub.Id, note);
        var again = await ModerationHost.Reject(admin, pub.Id, note); // a 409 body must not echo it either
        var bad = await ModerationHost.Reject(admin, Guid.NewGuid(), note);
        foreach (var r in new[] { publish, reject, again, bad })
        {
            var body = await r.Content.ReadAsStringAsync();
            Assert.DoesNotContain("MODERACJA-TEKST", body); Assert.DoesNotContain(note, body);
        }
        Assert.Equal(HttpStatusCode.NoContent, reject.StatusCode);
        Assert.NotEmpty(h.Logs.Lines);
        Assert.DoesNotContain(h.Logs.Lines, l => l.Contains("MODERACJA-TEKST") || l.Contains(note));
    }
}
