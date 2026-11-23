using System.Net;
using Gabinety.Booking.Notifications;
using Gabinety.Contracts.Booking;

namespace Gabinety.Booking.Tests;

public class ProposalActionsApiTests
{
    static async Task Problem(HttpResponseMessage r, HttpStatusCode status, string code)
    {
        Assert.Equal(status, r.StatusCode);
        Assert.Equal(code, (await r.Body()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_practitioner_accepts_and_both_parties_are_told()
    {
        await using var h = new ProposalHost();
        var id = await h.Create();
        var r = await ProposalHost.Act(h.AsPractitioner(), id, "accept", new { round = 1 });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var v = await r.Body();
        Assert.Equal("Accepted", v.GetProperty("status").GetString());
        Assert.Equal(2, v.GetProperty("round").GetInt32());
        Assert.False(v.GetProperty("canAccept").GetBoolean() || v.GetProperty("canCounter").GetBoolean() || v.GetProperty("canReject").GetBoolean() || v.GetProperty("canWithdraw").GetBoolean());
        Assert.True(!v.TryGetProperty("expiresAt", out var expires) || expires.ValueKind == System.Text.Json.JsonValueKind.Null); // closed: no deadline

        var p = await h.Row(id);
        Assert.Equal(ProposalStatus.Accepted, p.Status);
        Assert.Null(p.ExpiresAt);
        Assert.Equal(h.Now, p.ClosedAt);
        Assert.Equal([ProposalAction.Proposed, ProposalAction.Accepted], p.Rounds.OrderBy(x => x.Number).Select(x => x.Action));
        var mails = await h.Notifications(id);
        Assert.Contains(mails, n => n.Kind == NotificationKind.ProposalAccepted && n.ToUserId == ProposalHost.Client && n.Round == 2);
        Assert.Contains(mails, n => n.Kind == NotificationKind.ProposalAccepted && n.ToUserId == h.PractitionerUser && n.Round == 2);
    }

    [Fact]
    public async Task A_counter_hands_the_turn_over_replaces_the_terms_and_tells_the_other_party()
    {
        await using var h = new ProposalHost();
        var id = await h.Create();

        var r = await ProposalHost.Counter(h.AsPractitioner(), id, 1, ProposalHost.LongTerms(rate: 18000, hours: 10, months: 3, start: "2027-01-10"), "Proponuję inne warunki.");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var v = await r.Body();
        Assert.Equal("AwaitingClient", v.GetProperty("status").GetString());
        Assert.Equal(2, v.GetProperty("round").GetInt32());
        Assert.Equal(18000, v.GetProperty("terms").GetProperty("rate").GetProperty("amountMinor").GetInt64());
        Assert.Equal(10, v.GetProperty("terms").GetProperty("hoursPerWeek").GetInt32());
        Assert.False(v.GetProperty("canAccept").GetBoolean()); // the practitioner waits now
        Assert.False(v.GetProperty("canWithdraw").GetBoolean());

        var p = await h.Row(id);
        Assert.Equal(18000, p.RateMinor);
        var last = p.Rounds.OrderBy(x => x.Number).Last();
        Assert.Equal((2, ProposalActor.Practitioner, ProposalAction.Countered, "Proponuję inne warunki."), (last.Number, last.By, last.Action, last.Text));
        Assert.Equal(18000, last.RateMinor);
        var n = Assert.Single(await h.Notifications(id), x => x.Round == 2);
        Assert.Equal((NotificationKind.ProposalCountered, ProposalHost.Client), (n.Kind, n.ToUserId));
        Assert.Equal(h.Now.AddDays(14), p.ExpiresAt); // the clock restarts with each action

        // and the client may answer in turn
        var back = await ProposalHost.Counter(h.AsClient(), id, 2, ProposalHost.LongTerms(rate: 16000));
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        Assert.Equal("AwaitingPractitioner", (await back.Body()).GetProperty("status").GetString());
        Assert.Equal(3, (await h.Row(id)).RoundCount);
    }

    [Fact]
    public async Task Each_round_of_a_negotiation_sends_its_own_mail()
    {
        await using var h = new ProposalHost();
        var id = await h.Create();
        await ProposalHost.Counter(h.AsPractitioner(), id, 1);
        await ProposalHost.Counter(h.AsClient(), id, 2);
        await ProposalHost.Counter(h.AsPractitioner(), id, 3);
        var counters = (await h.Notifications(id)).Where(n => n.Kind == NotificationKind.ProposalCountered).ToList();
        Assert.Equal([2, 3, 4], counters.Select(n => n.Round));
        Assert.Equal([ProposalHost.Client, h.PractitionerUser, ProposalHost.Client], counters.Select(n => n.ToUserId));
    }

    [Fact]
    public async Task The_party_whose_turn_it_is_not_gets_409()
    {
        await using var h = new ProposalHost();
        var id = await h.Create();
        foreach (var action in new[] { "accept", "reject" })
            await Problem(await ProposalHost.Act(h.AsClient(), id, action, new { round = 1 }), HttpStatusCode.Conflict, "invalid_transition");
        await Problem(await ProposalHost.Counter(h.AsClient(), id, 1), HttpStatusCode.Conflict, "invalid_transition");

        await ProposalHost.Counter(h.AsPractitioner(), id, 1); // now the practitioner waits
        foreach (var action in new[] { "accept", "reject" })
            await Problem(await ProposalHost.Act(h.AsPractitioner(), id, action, new { round = 2 }), HttpStatusCode.Conflict, "invalid_transition");
        await Problem(await ProposalHost.Counter(h.AsPractitioner(), id, 2), HttpStatusCode.Conflict, "invalid_transition");
        Assert.Equal(2, (await h.Row(id)).RoundCount);
    }

    [Fact]
    public async Task An_answer_to_terms_that_have_since_changed_is_409()
    {
        await using var h = new ProposalHost();
        var id = await h.Create();
        await ProposalHost.Counter(h.AsPractitioner(), id, 1);
        await ProposalHost.Counter(h.AsClient(), id, 2);
        await Problem(await ProposalHost.Act(h.AsPractitioner(), id, "accept", new { round = 1 }), HttpStatusCode.Conflict, "invalid_transition");
        await Problem(await ProposalHost.Act(h.AsPractitioner(), id, "accept", new { round = 9 }), HttpStatusCode.Conflict, "invalid_transition");
        Assert.Equal(HttpStatusCode.OK, (await ProposalHost.Act(h.AsPractitioner(), id, "accept", new { round = 3 })).StatusCode);
    }

    [Fact]
    public async Task The_body_with_the_round_is_required_for_accept_counter_and_reject()
    {
        await using var h = new ProposalHost();
        var id = await h.Create();
        var empty = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/proposals/{id}/accept") { Content = new StringContent("", System.Text.Encoding.UTF8, "application/json") };
        await Problem(await h.AsPractitioner().SendAsync(empty), HttpStatusCode.BadRequest, "validation_failed");
        var noRound = await ProposalHost.Act(h.AsPractitioner(), id, "accept"); // {} means round 0
        await Problem(noRound, HttpStatusCode.Conflict, "invalid_transition");
    }

    [Fact]
    public async Task A_rejection_closes_the_proposal_and_tells_the_client()
    {
        await using var h = new ProposalHost();
        var id = await h.Create();
        var r = await ProposalHost.Act(h.AsPractitioner(), id, "reject", new { round = 1 });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Rejected", (await r.Body()).GetProperty("status").GetString());
        Assert.Contains(await h.Notifications(id), n => n.Kind == NotificationKind.ProposalRejected && n.ToUserId == ProposalHost.Client);
        foreach (var action in new[] { "accept", "reject", "withdraw" })
            await Problem(await ProposalHost.Act(h.AsPractitioner(), id, action, new { round = 2 }), HttpStatusCode.Conflict, "invalid_transition");
        await Problem(await ProposalHost.Act(h.AsClient(), id, "withdraw"), HttpStatusCode.Conflict, "invalid_transition");
    }

    [Fact]
    public async Task The_client_withdraws_in_either_turn_and_the_practitioner_cannot()
    {
        await using var h = new ProposalHost();
        var first = await h.Create();
        await Problem(await ProposalHost.Act(h.AsPractitioner(), first, "withdraw"), HttpStatusCode.Conflict, "invalid_transition");
        var r = await ProposalHost.Act(h.AsClient(), first, "withdraw");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Withdrawn", (await r.Body()).GetProperty("status").GetString());
        Assert.DoesNotContain(await h.Notifications(first), n => n.Round == 2); // a withdrawal sends nothing

        var second = await h.Create();
        await ProposalHost.Counter(h.AsPractitioner(), second, 1);
        Assert.Equal(HttpStatusCode.OK, (await ProposalHost.Act(h.AsClient(), second, "withdraw")).StatusCode); // out of turn is fine
    }

    [Fact]
    public async Task A_counter_is_validated_like_a_proposal()
    {
        await using var h = new ProposalHost();
        var id = await h.Create();
        var bad = await ProposalHost.Counter(h.AsPractitioner(), id, 1, ProposalHost.LongTerms(rate: 10));
        await Problem(bad, HttpStatusCode.UnprocessableEntity, "validation_failed");
        Assert.True((await bad.Body()).GetProperty("errors").TryGetProperty("terms.rate", out _));
        await Problem(await ProposalHost.Counter(h.AsPractitioner(), id, 1, text: "zadzwoń 600 123 456 w sprawie współpracy"), HttpStatusCode.UnprocessableEntity, "contact_details_not_allowed");
        await Problem(await ProposalHost.Counter(h.AsPractitioner(), id, 1, text: new string('x', 1001)), HttpStatusCode.UnprocessableEntity, "validation_failed");
        Assert.Equal(1, (await h.Row(id)).RoundCount); // nothing changed
    }

    [Fact]
    public async Task A_counter_without_a_note_is_fine_and_a_short_term_counter_uses_total_hours()
    {
        await using var h = new ProposalHost();
        var id = await h.Create(body: h.Req(ProposalHost.ShortTerms(total: 30), kind: "ShortTerm"));
        var r = await ProposalHost.Counter(h.AsPractitioner(), id, 1, ProposalHost.ShortTerms(rate: 17000, total: 50));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var p = await h.Row(id);
        Assert.Equal((17000L, (int?)50, (int?)null), (p.RateMinor, p.TotalHours, p.HoursPerWeek));
        Assert.Null(p.Rounds.OrderBy(x => x.Number).Last().Text);
        await Problem(await ProposalHost.Counter(h.AsClient(), id, 2, ProposalHost.LongTerms()), HttpStatusCode.UnprocessableEntity, "validation_failed"); // a short-term thread stays short
    }

    [Fact]
    public async Task At_most_six_counters_then_only_accept_or_reject()
    {
        await using var h = new ProposalHost();
        var id = await h.Create();
        var round = 1;
        for (var i = 1; i <= ProposalStateMachineCap; i++)
        {
            var who = i % 2 == 1 ? h.AsPractitioner() : h.AsClient();
            Assert.Equal(HttpStatusCode.OK, (await ProposalHost.Counter(who, id, round++, ProposalHost.LongTerms(rate: 10000 + i * 100))).StatusCode);
        }
        var v = await (await ProposalHost.View(h.AsPractitioner(), id)).Body(); // after an even number of counters it is the practitioner's turn again
        Assert.Equal(ProposalStateMachineCap + 1, v.GetProperty("round").GetInt32());
        Assert.False(v.GetProperty("canCounter").GetBoolean());
        Assert.True(v.GetProperty("canAccept").GetBoolean());
        Assert.True(v.GetProperty("canReject").GetBoolean());
        await Problem(await ProposalHost.Counter(h.AsPractitioner(), id, round), HttpStatusCode.Conflict, "invalid_transition");
        Assert.Equal(HttpStatusCode.OK, (await ProposalHost.Act(h.AsPractitioner(), id, "accept", new { round })).StatusCode);
    }

    const int ProposalStateMachineCap = Gabinety.Booking.Features.Proposals.ProposalStateMachine.MaxCounters;

    [Fact]
    public async Task A_stranger_gets_404_for_every_action_and_nothing_changes()
    {
        await using var h = new ProposalHost();
        var id = await h.Create();
        foreach (var action in new[] { "accept", "counter", "reject", "withdraw" })
            await Problem(await ProposalHost.Act(h.AsClient(ProposalHost.Stranger), id, action, new { round = 1, terms = ProposalHost.LongTerms() }), HttpStatusCode.NotFound, "not_found");
        await Problem(await ProposalHost.Act(h.AsClient(), Guid.NewGuid(), "withdraw"), HttpStatusCode.NotFound, "not_found");
        Assert.Equal(1, (await h.Row(id)).RoundCount);
    }

    [Fact]
    public async Task Another_client_cannot_act_on_a_proposal_that_is_not_theirs()
    {
        await using var h = new ProposalHost();
        var id = await h.Create();
        await Problem(await ProposalHost.Act(h.AsClient(ProposalHost.OtherClient), id, "withdraw"), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task The_actions_need_a_signed_in_user()
    {
        await using var h = new ProposalHost();
        var id = await h.Create();
        Assert.Equal(HttpStatusCode.Unauthorized, (await ProposalHost.Act(h.Host.ClientFor(null), id, "withdraw")).StatusCode);
    }

    [Fact]
    public async Task Two_answers_to_the_same_round_cannot_both_win()
    {
        await using var h = new ProposalHost();
        var id = await h.Create();
        var practitioner = h.AsPractitioner();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            ProposalHost.Act(practitioner, id, i % 2 == 0 ? "accept" : "reject", new { round = 1 })));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        var p = await h.Row(id);
        Assert.Equal(2, p.RoundCount);
        Assert.Equal(2, p.Rounds.Count);
    }

    [Fact]
    public async Task The_answers_never_log_the_note()
    {
        const string marker = "MARKER-COUNTER-91ab";
        await using var h = new ProposalHost();
        var id = await h.Create();
        await ProposalHost.Counter(h.AsPractitioner(), id, 1, text: marker + " proponuję inne warunki współpracy");
        await ProposalHost.Counter(h.AsClient(), id, 2, text: marker + " zadzwoń 600 123 456 w sprawie współpracy"); // refused
        Assert.DoesNotContain(h.Host.Logs.Lines, l => l.Contains(marker));
    }
}
