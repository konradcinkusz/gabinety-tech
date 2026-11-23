using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Booking.Features.Proposals;
using Gabinety.Booking.Notifications;
using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Tests;

/// <summary>
/// A Booking host for collaboration proposals (ADR proposal 0013): the create-booking host with a Directory that also answers the contract lookup (<see cref="Accepts"/>) and the practitioner's contact.
/// The clock is the manual clock of <see cref="CreateHost"/>: Monday 2026-11-16 12:00 UTC, Warsaw date 2026-11-16.
/// </summary>
sealed class ProposalHost : IAsyncDisposable
{
    public const string Client = "demo-client-1", OtherClient = "demo-client-2", Stranger = "demo-stranger";
    public const string FirstText = "Szukamy programisty .NET do dłuższej współpracy nad systemem rezerwacji.";

    public CreateHost Host { get; }
    public bool Accepts = true;
    public string? PractitionerEmail = "demo-practitioner@demo.invalid";
    public Guid Practitioner => Host.Practitioner;
    public string PractitionerUser => Host.OwnerUserId;

    public ProposalHost(CreateHost? host = null)
    {
        Host = host ?? new CreateHost();
        var inner = Host.Directory.Override!;
        Host.Directory.Override = (r, n) =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path == $"/internal/v1/practitioners/{Host.Practitioner}/contract")
                return FakeDirectory.Json(new
                {
                    practitionerId = Host.Practitioner, practitionerUserId = Host.OwnerUserId, status = "Approved",
                    displayName = Host.PractitionerName, slug = Host.PractitionerSlug, acceptsProposals = Accepts,
                });
            if (path == $"/internal/v1/practitioners/{Uri.EscapeDataString(Host.OwnerUserId)}/contact")
                return PractitionerEmail is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : FakeDirectory.Json(new { email = PractitionerEmail });
            return inner(r, n);
        };
    }

    public HttpClient AsClient(string user = Client) => Host.ClientFor(user);
    public HttpClient AsPractitioner() => Host.ClientFor(Host.OwnerUserId);
    public static string NewKey() => Guid.NewGuid().ToString();
    public DateTimeOffset Now => Host.Clock.GetUtcNow();

    public static object LongTerms(long rate = 15000, int hours = 20, int months = 6, string start = "2026-12-01") =>
        new { rate = new { amountMinor = rate, currency = "PLN" }, hoursPerWeek = hours, durationMonths = months, startDate = start };

    public static object ShortTerms(long rate = 15000, int total = 40, string start = "2026-12-01") =>
        new { rate = new { amountMinor = rate, currency = "PLN" }, totalHours = total, startDate = start };

    public object Req(object? terms = null, string kind = "LongTerm", string? text = null, string? company = null, Guid? practitioner = null) => new
    {
        practitionerId = practitioner ?? Host.Practitioner, kind, terms = terms ?? (kind == "LongTerm" ? LongTerms() : ShortTerms()), text = text ?? FirstText, companyName = company,
    };

    public static Task<HttpResponseMessage> Post(HttpClient c, object body, string? key = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/proposals") { Content = JsonContent.Create(body) };
        if (key is not null) req.Headers.Add("Idempotency-Key", key);
        return c.SendAsync(req);
    }

    /// <summary>Creates a proposal and returns its id; fails the test when the answer is not 201.</summary>
    public async Task<Guid> Create(HttpClient? client = null, object? body = null)
    {
        var r = await Post(client ?? AsClient(), body ?? Req(), NewKey());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Body()).GetProperty("id").GetGuid();
    }

    public static Task<HttpResponseMessage> Act(HttpClient c, Guid id, string action, object? body = null) =>
        c.PostAsJsonAsync($"/api/v1/proposals/{id}/{action}", body ?? new { });

    public static Task<HttpResponseMessage> Counter(HttpClient c, Guid id, int round, object? terms = null, string? text = null) =>
        Act(c, id, "counter", new { round, terms = terms ?? LongTerms(rate: 13000), text });

    public static Task<HttpResponseMessage> View(HttpClient c, Guid id) => c.GetAsync($"/api/v1/proposals/{id}");

    /// <summary>The proposals to THIS host's practitioner (the in-memory database is shared by name between hosts, the practitioner id is not).</summary>
    public Task<List<Proposal>> Proposals() => Host.Query(db => db.Proposals.AsNoTracking().Where(p => p.PractitionerId == Host.Practitioner).ToListAsync());

    public Task<Proposal> Row(Guid id) => Host.Query(db => db.Proposals.AsNoTracking().Include(p => p.Rounds).SingleAsync(p => p.Id == id));
    public Task<List<Notification>> Notifications(Guid id) => Host.Query(db => db.Notifications.AsNoTracking().Where(n => n.ProposalId == id).OrderBy(n => n.Round).ThenBy(n => n.ToUserId).ToListAsync());

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}
