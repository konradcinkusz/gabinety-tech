using System.Net;
using Gabinety.Booking.Features.Proposals;
using Gabinety.Booking.Notifications;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Booking.Tests;

/// <summary>ADR proposal 0013: erasure of either party and the demo reset reach the proposal tables.</summary>
public class ProposalErasureTests
{
    const string Secret = "TEKST-PROPOZYCJI-NIE-DO-LOGU", Company = "FIRMA-NIE-DO-LOGU";

    static async Task<(ProposalHost H, string User, Guid Id)> Started(bool counter = true)
    {
        // Its OWN practitioner user: the in-memory database is shared by name, and erasing the default "demo-practitioner-user" would rewrite the rows of every test running beside this one.
        var host = ErasureApiTests.NewHost();
        host.OwnerUserId = ErasureApiTests.NewUser("practitioner");
        var h = new ProposalHost(host);
        var user = ErasureApiTests.NewUser("client");
        var id = await h.Create(h.AsClient(user), h.Req(text: Secret + " szukamy programisty do współpracy.", company: Company));
        if (counter) await ProposalHost.Counter(h.AsPractitioner(), id, 1, text: "Odpowiedź programisty, którą zostawiamy.");
        return (h, user, id);
    }

    [Fact]
    public async Task Erasing_the_client_clears_their_data_closes_the_thread_and_keeps_the_practitioners_words()
    {
        var (h, user, id) = await Started();
        await using var _ = h;
        await h.Host.Seed(db => db.Notifications.AddRange(
            new Notification { Id = Guid.NewGuid(), ProposalId = id, Kind = NotificationKind.ProposalCountered, ToUserId = user, DueAt = h.Now, Round = 90 },
            new Notification { Id = Guid.NewGuid(), ProposalId = id, Kind = NotificationKind.ProposalCountered, ToUserId = user, DueAt = h.Now, SentAt = h.Now, Round = 91 }));

        Assert.Equal(HttpStatusCode.NoContent, (await ErasureApiTests.Erase(h.Host, user)).StatusCode);

        var p = await h.Row(id);
        var token = ErasureApiTests.Token(user);
        Assert.Equal(token, p.ClientUserId);
        Assert.Null(p.ClientEmail); Assert.Null(p.ClientDisplayName); Assert.Null(p.CompanyName);
        Assert.Equal(ProposalStatus.Withdrawn, p.Status);
        Assert.Null(p.ExpiresAt);
        var rounds = p.Rounds.OrderBy(r => r.Number).ToList();
        Assert.Equal(["[usunięto]", "Odpowiedź programisty, którą zostawiamy.", null], rounds.Select(r => r.Text));
        Assert.Equal((3, ProposalActor.System, ProposalAction.Withdrawn), (rounds[^1].Number, rounds[^1].By, rounds[^1].Action));
        Assert.Equal(3, p.RoundCount);

        var mails = await h.Notifications(id);
        Assert.DoesNotContain(mails, n => n.Round == 90); // never sent: deleted
        Assert.Equal(token, mails.Single(n => n.Round == 91).ToUserId); // sent: loses the user id
        Assert.DoesNotContain(mails, n => n.ToUserId == user);

        var view = await (await ProposalHost.View(h.AsPractitioner(), id)).Body();
        Assert.Equal("Usunięty użytkownik", view.GetProperty("client").GetProperty("displayName").GetString());
        Assert.DoesNotContain(Secret, view.GetRawText());
        Assert.DoesNotContain(Company, view.GetRawText());
        Assert.Equal("Withdrawn", view.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await ProposalHost.View(h.AsClient(user), id)).StatusCode); // the old id is a stranger now
    }

    [Fact]
    public async Task Erasing_the_practitioner_rejects_the_open_thread_and_the_client_sees_a_placeholder_name()
    {
        var (h, _, id) = await Started(counter: false);
        await using var _ = h;
        var client = (await h.Row(id)).ClientUserId;
        Assert.Equal(HttpStatusCode.NoContent, (await ErasureApiTests.Erase(h.Host, h.PractitionerUser)).StatusCode);

        var p = await h.Row(id);
        Assert.Equal(ErasureApiTests.Token(h.PractitionerUser), p.PractitionerUserId);
        Assert.Equal(ProposalStatus.Rejected, p.Status);
        Assert.Equal(client, p.ClientUserId); // the client's own data stays
        Assert.NotNull(p.ClientEmail);
        Assert.Contains(Secret, p.Rounds.First().Text); // the client's words are the client's own
        var last = p.Rounds.OrderBy(r => r.Number).Last();
        Assert.Equal((ProposalActor.System, ProposalAction.Rejected), (last.By, last.Action));

        var view = await (await ProposalHost.View(h.AsClient(client), id)).Body();
        Assert.Equal("Usunięty użytkownik", view.GetProperty("practitioner").GetProperty("displayName").GetString());
        Assert.Equal("", view.GetProperty("practitioner").GetProperty("slug").GetString());
    }

    [Fact]
    public async Task An_accepted_thread_stays_accepted_and_the_erased_partys_address_disappears()
    {
        var (h, user, id) = await Started(counter: false);
        await using var _ = h;
        await ProposalHost.Act(h.AsPractitioner(), id, "accept", new { round = 1 });
        await ErasureApiTests.Erase(h.Host, user);

        var p = await h.Row(id);
        Assert.Equal(ProposalStatus.Accepted, p.Status);
        Assert.Equal(2, p.RoundCount); // no closing round for a closed thread
        var contact = (await (await ProposalHost.View(h.AsPractitioner(), id)).Body()).GetProperty("contact");
        Assert.True(!contact.TryGetProperty("email", out var e) || e.ValueKind == System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Erasure_is_idempotent_a_token_is_not_a_subject_and_an_unknown_user_is_204()
    {
        var (h, user, id) = await Started();
        await using var _ = h;
        await ErasureApiTests.Erase(h.Host, user);
        var once = await h.Row(id);
        Assert.Equal(HttpStatusCode.NoContent, (await ErasureApiTests.Erase(h.Host, user)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ErasureApiTests.Erase(h.Host, ErasureApiTests.Token(user))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ErasureApiTests.Erase(h.Host, ErasureApiTests.NewUser("nobody"))).StatusCode);
        var twice = await h.Row(id);
        Assert.Equal((once.ClientUserId, once.RoundCount, once.Status), (twice.ClientUserId, twice.RoundCount, twice.Status));
    }

    [Fact]
    public async Task Erasure_needs_the_internal_key_and_logs_neither_the_text_nor_the_user()
    {
        var (h, user, id) = await Started();
        await using var _ = h;
        Assert.Equal(HttpStatusCode.Unauthorized, (await ErasureApiTests.Erase(h.Host, user, key: null)).StatusCode);
        Assert.Equal(ProposalStatus.AwaitingClient, (await h.Row(id)).Status);
        await ErasureApiTests.Erase(h.Host, user);
        // the framework logs the request path, which carries the id the caller chose; the application's own lines must not
        foreach (var line in h.Host.Logs.Lines)
            Assert.False(line.Contains(Secret) || line.Contains(Company) || (line.StartsWith("Gabinety.", StringComparison.Ordinal) && line.Contains(user)), line);
    }

    [Fact]
    public async Task An_erased_clients_idempotency_key_does_not_block_the_next_user()
    {
        var (h, user, _) = await Started();
        await using var _ = h;
        await ErasureApiTests.Erase(h.Host, user);
        // the proposals' unique key is (client, key): the token is distinct per user, so a key that was erased with its owner can be used again by anyone
        var key = ProposalHost.NewKey();
        Assert.Equal(HttpStatusCode.Created, (await ProposalHost.Post(h.AsClient(ProposalHost.OtherClient), h.Req(), key)).StatusCode);
    }

    // ---- demo reset

    static CreateHost ResetHost()
    {
        var dbName = "proposalreset-" + Guid.NewGuid().ToString("N");
        return ClockApiTests.FakeHost(h =>
        {
            var inner = h.Configure;
            h.Configure = s =>
            {
                inner?.Invoke(s);
                s.RemoveAll<DbContextOptions<BookingDbContext>>();
                s.RemoveAll(typeof(IDbContextOptionsConfiguration<BookingDbContext>));
                s.RemoveAll<BookingDbContext>();
                s.AddDbContext<BookingDbContext>(o => o.UseInMemoryDatabase(dbName));
            };
        });
    }

    [Fact]
    public async Task The_demo_reset_removes_the_proposals_of_demo_users_with_their_rounds_and_mails_and_nothing_else()
    {
        await using var h = new ProposalHost(ResetHost());
        var demo = await h.Create(h.AsClient("demo-reset-client")); // a demo client and the demo practitioner
        await ProposalHost.Counter(h.AsPractitioner(), demo, 1);
        await h.Create(h.AsClient("real-client-1")); // also a demo proposal: its practitioner is "demo-practitioner-user"
        // a proposal between two non-demo users
        var otherPractitioner = Guid.NewGuid();
        var untouched = Guid.NewGuid();
        await h.Host.Seed(db =>
        {
            db.Proposals.Add(new Proposal
            {
                Id = untouched, ClientUserId = "real-client-2", PractitionerId = otherPractitioner, PractitionerUserId = "real-practitioner", Kind = ProposalKind.ShortTerm,
                Status = ProposalStatus.AwaitingPractitioner, RoundCount = 1, RateMinor = 15000, TotalHours = 10, StartDate = new DateOnly(2026, 12, 1),
                IdempotencyKey = Guid.NewGuid(), CreatedAt = h.Now, LastActionAt = h.Now, ExpiresAt = h.Now.AddDays(14),
            });
            db.ProposalRounds.Add(new ProposalRound { Id = Guid.NewGuid(), ProposalId = untouched, Number = 1, By = ProposalActor.Client, Action = ProposalAction.Proposed, CreatedAt = h.Now });
            db.Notifications.Add(new Notification { Id = Guid.NewGuid(), ProposalId = untouched, Kind = NotificationKind.ProposalReceived, ToUserId = "real-practitioner", DueAt = h.Now, Round = 1 });
        });

        var r = await ClockApiTests.PostRaw(h.Host, "/internal/v1/demo/reset", "{}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Body();
        // the demo practitioner (OwnerUserId "demo-practitioner-user") owns both ProposalHost proposals, so both are demo proposals
        Assert.Equal(2, body.GetProperty("proposals").GetInt32());
        Assert.Equal(3, body.GetProperty("proposalRounds").GetInt32()); // proposed + countered, and the other proposed
        Assert.Equal(3, body.GetProperty("proposalNotifications").GetInt32()); // received, countered, received

        var left = await h.Host.Query(async db => (await db.Proposals.ToListAsync(), await db.ProposalRounds.ToListAsync(), await db.Notifications.Where(n => n.ProposalId != null).ToListAsync()));
        Assert.Equal(untouched, Assert.Single(left.Item1).Id);
        Assert.Equal(untouched, Assert.Single(left.Item2).ProposalId);
        Assert.Equal(untouched, Assert.Single(left.Item3).ProposalId);

        // twice is fine
        var again = await (await ClockApiTests.PostRaw(h.Host, "/internal/v1/demo/reset", "{}")).Body();
        Assert.Equal(0, again.GetProperty("proposals").GetInt32());
    }
}
