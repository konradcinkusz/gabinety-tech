using System.Net;
using Gabinety.Booking.Features.Proposals;
using Gabinety.Booking.Jobs.ProposalExpiry;
using Gabinety.Booking.Notifications;
using Gabinety.Contracts.Booking;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Booking.Tests;

/// <summary>The expiry job of ADR proposal 0013. Each test has its own in-memory database, so the job sees only its own proposals.</summary>
public class ProposalExpiryTests
{
    static ProposalExpiryJob Job(NotificationHost t) => t.Host.Services.GetRequiredService<ProposalExpiryJob>();

    [Fact]
    public async Task An_unanswered_proposal_expires_after_14_days_and_both_parties_are_told()
    {
        using var t = new NotificationHost();
        await using var h = new ProposalHost(t.Host);
        var id = await h.Create();

        t.Host.Clock.Advance(TimeSpan.FromDays(14) - TimeSpan.FromMinutes(1));
        Assert.Equal(0, await Job(t).ExpireDueAsync(t.Now, default)); // not yet
        Assert.Equal(ProposalStatus.AwaitingPractitioner, (await h.Row(id)).Status);

        t.Host.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await Job(t).ExpireDueAsync(t.Now, default));
        var p = await h.Row(id);
        Assert.Equal(ProposalStatus.Expired, p.Status);
        Assert.Null(p.ExpiresAt);
        Assert.Equal(t.Now, p.ClosedAt);
        Assert.Equal(2, p.RoundCount);
        var last = p.Rounds.OrderBy(r => r.Number).Last();
        Assert.Equal((2, ProposalActor.System, ProposalAction.Expired, (string?)null), (last.Number, last.By, last.Action, last.Text));
        var mails = (await h.Notifications(id)).Where(n => n.Kind == NotificationKind.ProposalExpired).ToList();
        Assert.Equal(new[] { ProposalHost.Client, h.PractitionerUser }.Order(), mails.Select(n => n.ToUserId).Order());
        Assert.All(mails, n => Assert.Equal(2, n.Round));

        Assert.Equal(0, await Job(t).ExpireDueAsync(t.Now, default)); // idempotent
        Assert.Equal(2, (await h.Row(id)).RoundCount);
    }

    [Fact]
    public async Task After_expiry_nobody_can_answer_and_the_pair_may_start_again()
    {
        using var t = new NotificationHost();
        await using var h = new ProposalHost(t.Host);
        var id = await h.Create();
        t.Host.Clock.Advance(TimeSpan.FromDays(15));
        await Job(t).ExpireDueAsync(t.Now, default);
        Assert.Equal(HttpStatusCode.Conflict, (await ProposalHost.Act(h.AsPractitioner(), id, "accept", new { round = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ProposalHost.Act(h.AsClient(), id, "withdraw")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ProposalHost.Post(h.AsClient(), h.Req(), ProposalHost.NewKey())).StatusCode);
    }

    [Fact]
    public async Task Every_action_moves_the_deadline_and_closed_proposals_are_left_alone()
    {
        using var t = new NotificationHost();
        await using var h = new ProposalHost(t.Host);
        var countered = await h.Create();
        var rejected = await h.Create(h.AsClient(ProposalHost.OtherClient));
        await ProposalHost.Act(h.AsPractitioner(), rejected, "reject", new { round = 1 });

        t.Host.Clock.Advance(TimeSpan.FromDays(10));
        await ProposalHost.Counter(h.AsPractitioner(), countered, 1); // the clock restarts: due on day 24
        t.Host.Clock.Advance(TimeSpan.FromDays(10)); // day 20: the original deadline has passed
        Assert.Equal(0, await Job(t).ExpireDueAsync(t.Now, default));
        Assert.Equal(ProposalStatus.AwaitingClient, (await h.Row(countered)).Status);
        Assert.Equal(ProposalStatus.Rejected, (await h.Row(rejected)).Status);

        t.Host.Clock.Advance(TimeSpan.FromDays(5)); // day 25
        Assert.Equal(1, await Job(t).ExpireDueAsync(t.Now, default));
        Assert.Equal(ProposalStatus.Expired, (await h.Row(countered)).Status);
        Assert.Equal(ProposalStatus.Rejected, (await h.Row(rejected)).Status);
    }

    [Fact]
    public async Task The_number_of_days_is_configurable()
    {
        using var t = new NotificationHost(setup: host => host.Settings["Proposals:ExpiryDays"] = "3");
        await using var h = new ProposalHost(t.Host);
        var id = await h.Create();
        Assert.Equal(t.Now.AddDays(3), (await h.Row(id)).ExpiresAt);
        t.Host.Clock.Advance(TimeSpan.FromDays(3));
        Assert.Equal(1, await Job(t).ExpireDueAsync(t.Now, default));
    }

    [Fact]
    public async Task The_job_works_in_batches_and_stops_at_the_cap()
    {
        using var t = new NotificationHost(setup: host => { host.Settings["Jobs:ProposalExpiry:BatchSize"] = "2"; host.Settings["Jobs:ProposalExpiry:MaxPerRun"] = "5"; });
        await using var h = new ProposalHost(t.Host);
        var ids = new List<Guid>();
        for (var i = 0; i < 7; i++) ids.Add(await h.Create(h.AsClient($"demo-batch-{i}")));
        t.Host.Clock.Advance(TimeSpan.FromDays(15));
        Assert.Equal(5, await Job(t).ExpireDueAsync(t.Now, default)); // the cap
        Assert.Equal(2, await Job(t).ExpireDueAsync(t.Now, default)); // the rest next cycle
        Assert.All(await h.Proposals(), p => Assert.Equal(ProposalStatus.Expired, p.Status));
    }

    [Fact]
    public async Task A_proposal_answered_after_it_was_selected_is_not_expired()
    {
        using var t = new NotificationHost();
        await using var h = new ProposalHost(t.Host);
        var id = await h.Create();
        t.Host.Clock.Advance(TimeSpan.FromDays(15));
        // the practitioner answers just before the job runs: the deadline is not due any more
        Assert.Equal(HttpStatusCode.OK, (await ProposalHost.Counter(h.AsPractitioner(), id, 1)).StatusCode);
        Assert.Equal(0, await Job(t).ExpireDueAsync(t.Now, default));
        Assert.Equal(ProposalStatus.AwaitingClient, (await h.Row(id)).Status);
    }

    [Fact]
    public async Task The_expiry_mails_are_sent_by_the_dispatcher()
    {
        using var t = new NotificationHost();
        await using var h = new ProposalHost(t.Host);
        var id = await h.Create();
        await t.Run();
        t.Host.Clock.Advance(TimeSpan.FromDays(15));
        await Job(t).ExpireDueAsync(t.Now, default);
        Assert.Equal(2, await t.Run());
        Assert.Equal(2, t.Sender.Sent.Count(m => m.Kind == NotificationKind.ProposalExpired));
        Assert.Contains(t.Sender.Sent, m => m.Kind == NotificationKind.ProposalExpired && m.TextBody.Contains($"/moje/propozycje/{id}"));
    }

    [Fact]
    public void The_job_is_registered_as_a_hosted_service_with_its_name()
    {
        using var t = new NotificationHost();
        Assert.Equal("proposal_expiry", ProposalExpiryJob.JobName);
        Assert.Contains(t.Host.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>(), s => s is ProposalExpiryJob);
        Assert.Equal(ProposalExpiryJob.JobName, Job(t).Name);
    }
}
