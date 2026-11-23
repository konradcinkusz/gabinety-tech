using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Booking.Notifications;
using Gabinety.Contracts.Booking;

namespace Gabinety.Booking.Tests;

public class CreateProposalApiTests
{
    static async Task Problem(HttpResponseMessage r, HttpStatusCode status, string code)
    {
        Assert.Equal(status, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, (await r.Body()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_answers_201_with_the_view_and_stores_the_proposal_its_first_round_and_the_notification()
    {
        await using var h = new ProposalHost();
        var key = ProposalHost.NewKey();
        var r = await ProposalHost.Post(h.AsClient(), h.Req(company: "Demo Sp. z o.o."), key);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var v = await r.Body();
        var id = v.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/proposals/{id}", r.Headers.Location?.ToString());
        Assert.Equal("AwaitingPractitioner", v.GetProperty("status").GetString());
        Assert.Equal("LongTerm", v.GetProperty("kind").GetString());
        Assert.Equal(1, v.GetProperty("round").GetInt32());
        Assert.Equal("Client", v.GetProperty("viewerRole").GetString());
        Assert.Equal("Demo Sp. z o.o.", v.GetProperty("companyName").GetString());
        Assert.Equal("Demo Practitioner", v.GetProperty("practitioner").GetProperty("displayName").GetString());
        Assert.Equal("2026-11-30T12:00:00Z", v.GetProperty("expiresAt").GetString()); // 14 days after the clock
        Assert.False(v.GetProperty("canAccept").GetBoolean());
        Assert.True(v.GetProperty("canWithdraw").GetBoolean());
        Assert.False(v.TryGetProperty("contact", out var c) && c.ValueKind != JsonValueKind.Null);
        var terms = v.GetProperty("terms");
        Assert.Equal(15000, terms.GetProperty("rate").GetProperty("amountMinor").GetInt64());
        Assert.Equal(20, terms.GetProperty("hoursPerWeek").GetInt32());
        Assert.Equal(6, terms.GetProperty("durationMonths").GetInt32());
        Assert.Equal("2026-12-01", terms.GetProperty("startDate").GetString());

        var p = await h.Row(id);
        Assert.Equal((ProposalHost.Client, h.PractitionerUser, h.Practitioner), (p.ClientUserId, p.PractitionerUserId, p.PractitionerId));
        Assert.Equal((ProposalStatus.AwaitingPractitioner, 1, Guid.Parse(key)), (p.Status, p.RoundCount, p.IdempotencyKey));
        Assert.Equal("demo-client-1@demo.invalid", p.ClientEmail);
        Assert.Equal(h.Now.AddDays(14), p.ExpiresAt);
        var round = Assert.Single(p.Rounds);
        Assert.Equal((1, ProposalActor.Client, ProposalAction.Proposed, ProposalHost.FirstText), (round.Number, round.By, round.Action, round.Text));

        var n = Assert.Single(await h.Notifications(id));
        Assert.Equal((NotificationKind.ProposalReceived, h.PractitionerUser, 1, (Guid?)null), (n.Kind, n.ToUserId, n.Round, n.BookingId));
    }

    [Fact]
    public async Task A_short_term_proposal_stores_total_hours_and_no_hours_a_week()
    {
        await using var h = new ProposalHost();
        var id = await h.Create(body: h.Req(ProposalHost.ShortTerms(total: 30), kind: "ShortTerm"));
        var p = await h.Row(id);
        Assert.Equal((ProposalKind.ShortTerm, (int?)30, (int?)null), (p.Kind, p.TotalHours, p.HoursPerWeek));
    }

    [Fact]
    public async Task The_client_is_shown_to_the_practitioner_by_an_initial_only()
    {
        await using var h = new ProposalHost(new CreateHost { NameClaim = "jan.kowalski@firma.pl" });
        var id = await h.Create();
        Assert.Equal("Klient J.", (await h.Row(id)).ClientDisplayName);
        var v = await (await ProposalHost.View(h.AsPractitioner(), id)).Body();
        Assert.Equal("Klient J.", v.GetProperty("client").GetProperty("displayName").GetString());
        Assert.DoesNotContain("kowalski", v.GetRawText());
    }

    [Fact]
    public async Task It_needs_a_signed_in_user()
    {
        await using var h = new ProposalHost();
        Assert.Equal(HttpStatusCode.Unauthorized, (await ProposalHost.Post(h.Host.ClientFor(null), h.Req(), ProposalHost.NewKey())).StatusCode);
    }

    [Fact]
    public async Task The_idempotency_key_is_required_and_must_be_a_uuid()
    {
        await using var h = new ProposalHost();
        await Problem(await ProposalHost.Post(h.AsClient(), h.Req()), HttpStatusCode.BadRequest, "validation_failed");
        await Problem(await ProposalHost.Post(h.AsClient(), h.Req(), "not-a-uuid"), HttpStatusCode.BadRequest, "validation_failed");
        Assert.Empty(await h.Proposals());
    }

    [Fact]
    public async Task A_malformed_or_empty_body_is_400()
    {
        await using var h = new ProposalHost();
        var bad = new HttpRequestMessage(HttpMethod.Post, "/api/v1/proposals") { Content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json") };
        bad.Headers.Add("Idempotency-Key", ProposalHost.NewKey());
        await Problem(await h.AsClient().SendAsync(bad), HttpStatusCode.BadRequest, "validation_failed");
        var empty = new HttpRequestMessage(HttpMethod.Post, "/api/v1/proposals") { Content = new StringContent("", System.Text.Encoding.UTF8, "application/json") };
        empty.Headers.Add("Idempotency-Key", ProposalHost.NewKey());
        await Problem(await h.AsClient().SendAsync(empty), HttpStatusCode.BadRequest, "validation_failed");
    }

    public static TheoryData<string, object, string> Invalid => new()
    {
        { "rate too low", ProposalHost.LongTerms(rate: 1000), "terms.rate" },
        { "rate too high", ProposalHost.LongTerms(rate: 500000), "terms.rate" },
        { "no hours a week", new { rate = new { amountMinor = 15000, currency = "PLN" }, durationMonths = 6, startDate = "2026-12-01" }, "terms.hoursPerWeek" },
        { "too many hours", ProposalHost.LongTerms(hours: 61), "terms.hoursPerWeek" },
        { "no months", new { rate = new { amountMinor = 15000, currency = "PLN" }, hoursPerWeek = 20, startDate = "2026-12-01" }, "terms.durationMonths" },
        { "start in the past", ProposalHost.LongTerms(start: "2026-11-15"), "terms.startDate" },
        { "start too far ahead", ProposalHost.LongTerms(start: "2029-01-01"), "terms.startDate" },
    };

    [Theory, MemberData(nameof(Invalid))]
    public async Task Invalid_terms_are_422_with_the_field_named(string why, object terms, string field)
    {
        await using var h = new ProposalHost();
        var r = await ProposalHost.Post(h.AsClient(), h.Req(terms), ProposalHost.NewKey());
        await Problem(r, HttpStatusCode.UnprocessableEntity, "validation_failed");
        Assert.True((await r.Body()).GetProperty("errors").TryGetProperty(field, out _), why);
    }

    [Fact]
    public async Task A_short_term_proposal_without_total_hours_is_422()
    {
        await using var h = new ProposalHost();
        var r = await ProposalHost.Post(h.AsClient(), h.Req(ProposalHost.LongTerms(), kind: "ShortTerm"), ProposalHost.NewKey());
        await Problem(r, HttpStatusCode.UnprocessableEntity, "validation_failed");
        Assert.True((await r.Body()).GetProperty("errors").TryGetProperty("terms.totalHours", out _));
    }

    [Fact]
    public async Task The_message_is_20_to_1000_characters_and_the_company_name_at_most_120()
    {
        await using var h = new ProposalHost();
        foreach (var (text, company, field) in new[] { ("za krótko", null, "text"), (new string('a', 1001), null, "text"), (ProposalHost.FirstText, new string('f', 121), "companyName") })
        {
            var r = await ProposalHost.Post(h.AsClient(), h.Req(text: text, company: company), ProposalHost.NewKey());
            await Problem(r, HttpStatusCode.UnprocessableEntity, "validation_failed");
            Assert.True((await r.Body()).GetProperty("errors").TryGetProperty(field, out _), field);
        }
        Assert.Equal(HttpStatusCode.Created, (await ProposalHost.Post(h.AsClient(), h.Req(text: new string('a', 1000), company: new string('f', 120)), ProposalHost.NewKey())).StatusCode);
    }

    [Theory]
    [InlineData("Pisz na jan@firma.pl w sprawie współpracy.", null)]
    [InlineData("Zadzwoń pod 600 123 456 w sprawie współpracy.", null)]
    [InlineData("Znajdziesz mnie na @jankowalski w sprawie współpracy.", null)]
    [InlineData("Szczegóły na https://firma.example.test/oferta w sprawie współpracy.", null)]
    [InlineData(ProposalHost.FirstText, "jan@firma.pl")]
    [InlineData(ProposalHost.FirstText, "tel 600123456")]
    public async Task Contact_details_in_the_message_or_the_company_name_are_422(string text, string? company)
    {
        await using var h = new ProposalHost();
        await Problem(await ProposalHost.Post(h.AsClient(), h.Req(text: text, company: company), ProposalHost.NewKey()), HttpStatusCode.UnprocessableEntity, "contact_details_not_allowed");
        Assert.Empty(await h.Proposals());
    }

    [Fact]
    public async Task A_date_and_a_code_host_link_in_the_message_are_fine()
    {
        await using var h = new ProposalHost();
        var r = await ProposalHost.Post(h.AsClient(), h.Req(text: "Start 2027-01-15, repozytorium https://github.com/demo/projekt, trzy miesiące."), ProposalHost.NewKey());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    }

    [Fact]
    public async Task A_proposal_to_oneself_is_422_own_profile()
    {
        await using var h = new ProposalHost();
        await Problem(await ProposalHost.Post(h.AsPractitioner(), h.Req(), ProposalHost.NewKey()), HttpStatusCode.UnprocessableEntity, "own_profile");
    }

    [Fact]
    public async Task A_practitioner_who_does_not_accept_proposals_is_422()
    {
        await using var h = new ProposalHost { Accepts = false };
        await Problem(await ProposalHost.Post(h.AsClient(), h.Req(), ProposalHost.NewKey()), HttpStatusCode.UnprocessableEntity, "practitioner_not_available");
        Assert.Empty(await h.Proposals());
    }

    [Fact]
    public async Task An_unknown_practitioner_is_404()
    {
        await using var h = new ProposalHost();
        await Problem(await ProposalHost.Post(h.AsClient(), h.Req(practitioner: Guid.NewGuid()), ProposalHost.NewKey()), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task When_Directory_does_not_answer_the_answer_is_503_and_nothing_is_stored()
    {
        await using var h = new ProposalHost();
        h.Host.Directory.Down = true;
        await Problem(await ProposalHost.Post(h.AsClient(), h.Req(), ProposalHost.NewKey()), HttpStatusCode.ServiceUnavailable, "dependency_unavailable");
        Assert.Empty(await h.Proposals());
    }

    [Fact]
    public async Task Only_one_proposal_per_pair_can_be_open_and_another_client_is_not_blocked()
    {
        await using var h = new ProposalHost();
        await h.Create();
        await Problem(await ProposalHost.Post(h.AsClient(), h.Req(), ProposalHost.NewKey()), HttpStatusCode.Conflict, "proposal_exists");
        Assert.Equal(HttpStatusCode.Created, (await ProposalHost.Post(h.AsClient(ProposalHost.OtherClient), h.Req(), ProposalHost.NewKey())).StatusCode);
    }

    [Fact]
    public async Task After_a_rejection_the_pair_may_start_again()
    {
        await using var h = new ProposalHost();
        var id = await h.Create();
        Assert.Equal(HttpStatusCode.OK, (await ProposalHost.Act(h.AsPractitioner(), id, "reject", new { round = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ProposalHost.Post(h.AsClient(), h.Req(), ProposalHost.NewKey())).StatusCode);
    }

    [Fact]
    public async Task The_same_key_is_a_retry_200_with_the_same_proposal_and_no_second_row_or_mail()
    {
        await using var h = new ProposalHost();
        var key = ProposalHost.NewKey();
        var first = await ProposalHost.Post(h.AsClient(), h.Req(), key);
        var second = await ProposalHost.Post(h.AsClient(), h.Req(), key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var id = (await first.Body()).GetProperty("id").GetGuid();
        Assert.Equal(id, (await second.Body()).GetProperty("id").GetGuid());
        Assert.Single(await h.Proposals());
        Assert.Single(await h.Notifications(id));
    }

    [Fact]
    public async Task The_same_key_for_another_practitioner_is_a_conflict()
    {
        await using var h = new ProposalHost();
        var key = ProposalHost.NewKey();
        Assert.Equal(HttpStatusCode.Created, (await ProposalHost.Post(h.AsClient(), h.Req(), key)).StatusCode);
        await Problem(await ProposalHost.Post(h.AsClient(), h.Req(practitioner: Guid.NewGuid()), key), HttpStatusCode.Conflict, "idempotency_conflict");
    }

    [Fact]
    public async Task The_same_key_of_another_user_is_not_a_replay()
    {
        await using var h = new ProposalHost();
        var key = ProposalHost.NewKey();
        Assert.Equal(HttpStatusCode.Created, (await ProposalHost.Post(h.AsClient(), h.Req(), key)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ProposalHost.Post(h.AsClient(ProposalHost.OtherClient), h.Req(), key)).StatusCode);
    }

    [Fact]
    public async Task The_sixth_attempt_inside_an_hour_is_429_with_Retry_After_and_the_hour_slides()
    {
        await using var h = new ProposalHost();
        await h.Create();
        for (var i = 0; i < 4; i++) // the open pair answers 409, and each attempt still counts against the limit
            Assert.Equal(HttpStatusCode.Conflict, (await ProposalHost.Post(h.AsClient(), h.Req(), ProposalHost.NewKey())).StatusCode);
        var limited = await ProposalHost.Post(h.AsClient(), h.Req(), ProposalHost.NewKey());
        await Problem(limited, HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.True(limited.Headers.TryGetValues("Retry-After", out var after) && int.Parse(after.Single()) > 0);

        h.Host.Clock.Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(HttpStatusCode.Conflict, (await ProposalHost.Post(h.AsClient(), h.Req(), ProposalHost.NewKey())).StatusCode);
    }

    [Fact]
    public async Task A_retry_does_not_use_up_the_limit()
    {
        await using var h = new ProposalHost();
        var key = ProposalHost.NewKey();
        await ProposalHost.Post(h.AsClient(), h.Req(), key);
        for (var i = 0; i < 10; i++) Assert.Equal(HttpStatusCode.OK, (await ProposalHost.Post(h.AsClient(), h.Req(), key)).StatusCode);
    }

    [Fact]
    public async Task The_message_and_the_company_name_never_reach_a_log_a_notification_or_a_problem()
    {
        const string marker = "MARKER-PROPOSAL-4d2c";
        await using var h = new ProposalHost();
        var text = $"{marker} szukamy programisty do współpracy nad systemem.";
        var r = await ProposalHost.Post(h.AsClient(), h.Req(text: text, company: marker + "-firma"), ProposalHost.NewKey());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        // refused attempts must not echo the text either
        var refused = await ProposalHost.Post(h.AsClient(ProposalHost.OtherClient), h.Req(text: marker + " pisz na jan@firma.pl w sprawie współpracy"), ProposalHost.NewKey());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.DoesNotContain(marker, await refused.Content.ReadAsStringAsync());
        Assert.DoesNotContain(h.Host.Logs.Lines, l => l.Contains(marker));
        var id = (await r.Body()).GetProperty("id").GetGuid();
        Assert.All(await h.Notifications(id), n => Assert.DoesNotContain(marker, n.ToString() ?? ""));
    }
}
