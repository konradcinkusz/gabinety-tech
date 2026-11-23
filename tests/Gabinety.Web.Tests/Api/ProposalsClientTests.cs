using System.Net;
using System.Text.Json;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Web.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using W = Gabinety.Web.Api;

namespace Gabinety.Web.Tests.Backends;

/// <summary>Collaboration proposals at the HTTP boundary (ADR proposal 0013): what is sent, what is read back, and what never leaves (a message of the server, a text of the user in a URL).</summary>
public class ProposalsClientTests
{
    sealed class Token(string? value) : IAccessTokenSource { public Task<string?> AccessTokenAsync(CancellationToken ct = default) => Task.FromResult(value); }

    static (W.ProposalsClient Client, FakeBackend Backend) Proposals(string? token = "tok")
    {
        var backend = new FakeBackend();
        return (new W.ProposalsClient(new HttpClient(backend) { BaseAddress = new Uri("http://booking.test") }, new W.VisitorAddressAccessor(new HttpContextAccessor()), new Token(token)), backend);
    }

    static readonly Guid Id = Guid.Parse("018f0000-0000-7000-8000-0000000000c1");
    static readonly W.ProposalTermsDto Terms = new(new W.MoneyDto(15000, "PLN"), 20, null, 6, new DateOnly(2026, 12, 1));

    static ProposalView View(ProposalStatus status = ProposalStatus.AwaitingPractitioner) => new(Id, ProposalKind.LongTerm, status,
        new ProposalTerms(new MoneyDto(15000), 20, null, 6, new DateOnly(2026, 12, 1)), new PractitionerRef(Guid.NewGuid(), "Anna Demowska", "demo-anna"), new ClientRef("Klient J."), null,
        [], 1, ProposalActor.Client, null, false, false, false, true);

    static HttpResponseMessage Wire(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Create_posts_the_json_the_service_reads_with_the_key_in_the_header_and_never_in_the_body_or_the_url()
    {
        var (client, backend) = Proposals();
        string? header = null;
        backend.Respond = r => { header = r.Headers.GetValues("Idempotency-Key").Single(); return Wire(View(), HttpStatusCode.Created); };
        var key = Guid.NewGuid();
        var practitioner = Guid.NewGuid();
        var result = await client.CreateAsync(key, new W.CreateProposalBody(practitioner, "LongTerm", Terms, "Szukamy programisty do współpracy.", "Demo Sp. z o.o."));
        Assert.True(result.IsOk);
        Assert.Equal(Id, result.Value!.Id);
        Assert.Equal(key.ToString(), header);
        var sent = Assert.Single(backend.Requests);
        Assert.Equal((HttpMethod.Post, "/api/v1/proposals"), (sent.Method, sent.PathAndQuery));
        Assert.DoesNotContain(key.ToString(), sent.Body!);
        Assert.DoesNotContain("Szukamy", sent.PathAndQuery);
        using var body = JsonDocument.Parse(sent.Body!);
        Assert.Equal(["practitionerId", "kind", "terms", "text", "companyName"], body.RootElement.EnumerateObject().Select(p => p.Name)); // camelCase, nothing extra
        Assert.Equal("2026-12-01", body.RootElement.GetProperty("terms").GetProperty("startDate").GetString());
        Assert.Equal(15000, body.RootElement.GetProperty("terms").GetProperty("rate").GetProperty("amountMinor").GetInt64());
    }

    [Fact]
    public async Task What_the_client_writes_is_what_the_service_contract_reads()
    {
        var (client, backend) = Proposals();
        backend.Respond = _ => Wire(View(), HttpStatusCode.Created);
        var practitioner = Guid.NewGuid();
        await client.CreateAsync(Guid.NewGuid(), new W.CreateProposalBody(practitioner, "ShortTerm", Terms with { HoursPerWeek = null, TotalHours = 40 }, "Wiadomość do programisty.", null));
        var read = JsonSerializer.Deserialize<CreateProposalRequest>(backend.Requests[0].Body!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal((practitioner, ProposalKind.ShortTerm, "Wiadomość do programisty.", (string?)null), (read.PractitionerId, read.Kind, read.Text, read.CompanyName));
        Assert.Equal((15000L, (int?)null, (int?)40, new DateOnly(2026, 12, 1)), (read.Terms.Rate.AmountMinor, read.Terms.HoursPerWeek, read.Terms.TotalHours, read.Terms.StartDate));
        Assert.DoesNotContain("companyName", backend.Requests[0].Body!); // an empty company is omitted, not sent as null
    }

    [Fact]
    public async Task The_actions_post_the_round_the_user_saw_and_a_counter_the_new_terms()
    {
        var (client, backend) = Proposals();
        backend.Respond = _ => Wire(View());
        await client.AcceptAsync(Id, 3);
        await client.RejectAsync(Id, 4);
        await client.CounterAsync(Id, new W.CounterProposalBody(5, Terms, "Inne warunki."));
        await client.CounterAsync(Id, new W.CounterProposalBody(6, Terms, null));
        await client.WithdrawAsync(Id);
        Assert.Equal([$"/api/v1/proposals/{Id}/accept", $"/api/v1/proposals/{Id}/reject", $"/api/v1/proposals/{Id}/counter", $"/api/v1/proposals/{Id}/counter", $"/api/v1/proposals/{Id}/withdraw"],
            backend.Requests.Select(r => r.PathAndQuery));
        Assert.All(backend.Requests, r => Assert.Equal(HttpMethod.Post, r.Method));
        Assert.Equal(3, JsonSerializer.Deserialize<ProposalActionRequest>(backend.Requests[0].Body!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Round);
        Assert.Equal(4, JsonSerializer.Deserialize<ProposalActionRequest>(backend.Requests[1].Body!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Round);
        var counter = JsonSerializer.Deserialize<CounterProposalRequest>(backend.Requests[2].Body!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal((5, "Inne warunki."), (counter.Round, counter.Text));
        Assert.DoesNotContain("text", backend.Requests[3].Body!); // no note: omitted
        Assert.DoesNotContain("Inne warunki", backend.Requests[2].PathAndQuery);
    }

    [Fact]
    public async Task Get_reads_the_view_with_the_bearer_and_a_stranger_is_a_404_failure()
    {
        var (client, backend) = Proposals();
        backend.Respond = _ => Wire(View(ProposalStatus.Accepted));
        var ok = await client.GetAsync(Id);
        Assert.Equal(("Accepted", "Client"), (ok.Value!.Status, ok.Value.ViewerRole));
        Assert.Equal($"/api/v1/proposals/{Id}", backend.Requests[0].PathAndQuery);
        backend.Respond = _ => FakeBackend.Problem(HttpStatusCode.NotFound, "not_found");
        var missing = await client.GetAsync(Id);
        Assert.False(missing.IsOk);
        Assert.Equal((404, "not_found"), (missing.Failure!.Status, missing.Failure.Code));
    }

    [Theory]
    [InlineData("client", null, null, "/api/v1/me/proposals?role=client")]
    [InlineData("practitioner", "open", null, "/api/v1/me/proposals?role=practitioner&state=open")]
    [InlineData("client", "closed", "C 1+/=", "/api/v1/me/proposals?role=client&state=closed&cursor=C%201%2B%2F%3D")]
    [InlineData("admin", "weird", "", "/api/v1/me/proposals?role=client")] // an unknown role is the client's; an unknown state is every state
    public async Task The_list_asks_for_one_role_one_state_and_escapes_the_cursor(string role, string? state, string? cursor, string expected)
    {
        var (client, backend) = Proposals();
        backend.Respond = _ => Wire(new { items = Array.Empty<object>() });
        Assert.True((await client.ListAsync(role, state, cursor)).IsOk);
        Assert.Equal(expected, backend.Requests[0].PathAndQuery);
    }

    [Fact]
    public async Task A_failure_carries_the_code_and_the_names_of_the_fields_never_a_message()
    {
        var (client, backend) = Proposals();
        backend.Respond = _ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("{\"type\":\"urn:gabinety:error:validation_failed\",\"title\":\"SERVER-TITLE-MUST-NOT-APPEAR\",\"status\":422,\"detail\":\"SERVER-DETAIL-MUST-NOT-APPEAR\",\"code\":\"validation_failed\",\"errors\":{\"terms.rate\":[\"SERVER-MESSAGE\"],\"text\":[\"SERVER-MESSAGE\"]}}",
                System.Text.Encoding.UTF8, "application/problem+json"),
        };
        var result = await client.CreateAsync(Guid.NewGuid(), new W.CreateProposalBody(Guid.NewGuid(), "LongTerm", Terms, "Wiadomość do programisty.", null));
        Assert.Equal((422, "validation_failed"), (result.Failure!.Status, result.Failure.Code));
        Assert.Equal(["terms.rate", "text"], result.Failure.Fields);
        Assert.True(result.Failure.Names("terms") && result.Failure.Names("text"));
        Assert.DoesNotContain("SERVER", result.Failure.ToString());
    }

    [Fact]
    public async Task A_service_that_does_not_answer_is_a_state_not_an_exception()
    {
        var (client, backend) = Proposals();
        backend.Respond = FakeBackend.Throw(new HttpRequestException("down"));
        var result = await client.GetAsync(Id);
        Assert.Equal((0, "unavailable"), (result.Failure!.Status, result.Failure.Code));
    }

    [Fact]
    public async Task Without_a_token_no_bearer_is_sent()
    {
        var (client, backend) = Proposals(token: null);
        backend.Respond = _ => Wire(View());
        await client.GetAsync(Id);
        Assert.Null(backend.Requests[0].Authorization);
    }

    [Fact]
    public async Task Through_the_real_pipeline_the_bearer_is_sent_and_a_write_that_failed_is_not_repeated()
    {
        await using var web = new WebFactory();
        web.Booking.Respond = r => r.Method == HttpMethod.Get ? Wire(View()) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        // the client is built over the handler chain the host really uses: service discovery, the one resilience handler and the handler that adds the bearer
        var handlers = web.Services.GetRequiredService<IHttpMessageHandlerFactory>();
        var client = new W.ProposalsClient(new HttpClient(handlers.CreateHandler(W.Backend.Booking)) { BaseAddress = new Uri(W.Backend.BaseAddress(W.Backend.Booking)) },
            new W.VisitorAddressAccessor(new HttpContextAccessor()), new Token("t"));

        Assert.True((await client.GetAsync(Id)).IsOk);
        Assert.Equal("Bearer t", web.Booking.Requests.Single().Authorization);

        var failed = await client.AcceptAsync(Id, 1);
        Assert.Equal(503, failed.Failure!.Status);
        Assert.Equal(2, web.Booking.Calls); // one read and ONE write: SPEC 5 rule 8, a POST is never retried by the pipeline
    }
}

/// <summary>The web's copies of the proposal and contract-availability DTOs against the real contract types, serialized the way the services do (plain web options: the enum attributes decide).</summary>
public class ProposalContractCopiesTests
{
    static readonly JsonSerializerOptions Service = new(JsonSerializerDefaults.Web);
    static readonly Guid Id = Guid.Parse("018f0000-0000-7000-8000-0000000000c1");
    static readonly DateTimeOffset At = new(2026, 12, 1, 12, 0, 0, TimeSpan.Zero);

    static T Read<T>(object real) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(real, real.GetType(), Service), W.ApiClient.Json)!;

    [Fact]
    public void A_proposal_view_is_read_with_every_field_the_pages_use()
    {
        var view = new ProposalView(Id, ProposalKind.LongTerm, ProposalStatus.AwaitingClient, new ProposalTerms(new MoneyDto(15000), 20, null, 6, new DateOnly(2026, 12, 1)),
            new PractitionerRef(Guid.NewGuid(), "Anna Demowska", "demo-anna"), new ClientRef("Klient J."), "Demo Sp. z o.o.",
            [new ProposalRoundDto(1, ProposalActor.Client, ProposalAction.Proposed, new ProposalTerms(new MoneyDto(15000), 20, null, 6, new DateOnly(2026, 12, 1)), "Wiadomość klienta.", At),
             new ProposalRoundDto(2, ProposalActor.System, ProposalAction.Expired, null, null, At.AddDays(14))],
            2, ProposalActor.Practitioner, At.AddDays(14), true, true, true, false, new ProposalContact("a@example.test"));
        var copy = Read<W.ProposalViewDto>(view);
        Assert.Equal(("LongTerm", "AwaitingClient", "Practitioner", "Demo Sp. z o.o.", 2), (copy.Kind, copy.Status, copy.ViewerRole, copy.CompanyName, copy.Round));
        Assert.Equal((15000L, 20, (int?)null, 6, new DateOnly(2026, 12, 1)), (copy.Terms.Rate.AmountMinor, copy.Terms.HoursPerWeek, copy.Terms.TotalHours, copy.Terms.DurationMonths, copy.Terms.StartDate));
        Assert.Equal(("Anna Demowska", "demo-anna", "Klient J."), (copy.Practitioner.DisplayName, copy.Practitioner.Slug, copy.Client.DisplayName));
        Assert.Equal([true, true, true, false], [copy.CanAccept, copy.CanCounter, copy.CanReject, copy.CanWithdraw]);
        Assert.Equal(At.AddDays(14), copy.ExpiresAt);
        Assert.Equal("a@example.test", copy.Contact!.Email);
        Assert.Equal([("Client", "Proposed", "Wiadomość klienta."), ("System", "Expired", null)], copy.Rounds.Select(r => (r.By, r.Action, r.Text)));
        Assert.Null(copy.Rounds[1].Terms);
        Assert.Equal(1, copy.Rounds[0].Terms!.Rate.AmountMinor / 15000);
    }

    [Fact]
    public void A_proposal_view_without_a_company_a_deadline_or_a_contact_is_read_as_none()
    {
        var view = new ProposalView(Id, ProposalKind.ShortTerm, ProposalStatus.Accepted, new ProposalTerms(new MoneyDto(15050), null, 40, null, new DateOnly(2026, 12, 1)),
            new PractitionerRef(Guid.NewGuid(), "Anna", "a"), new ClientRef("Klient"), null, [], 2, ProposalActor.Client, null, false, false, false, false);
        var copy = Read<W.ProposalViewDto>(view);
        Assert.Null(copy.CompanyName); Assert.Null(copy.ExpiresAt); Assert.Null(copy.Contact);
        Assert.Equal((null, 40, null), (copy.Terms.HoursPerWeek, copy.Terms.TotalHours, copy.Terms.DurationMonths));
    }

    [Fact]
    public void A_summary_page_is_read_with_the_cursor()
    {
        var page = new { items = new[] { new ProposalSummary(Id, ProposalKind.LongTerm, ProposalStatus.AwaitingPractitioner, new ProposalTerms(new MoneyDto(15000), 20, null, 6, new DateOnly(2026, 12, 1)),
            new PractitionerRef(Guid.NewGuid(), "Anna", "a"), new ClientRef("Klient J."), "Firma", 1, true, At, At.AddDays(14)) }, nextCursor = "NEXT" };
        var copy = Read<W.PageDto<W.ProposalSummaryDto>>(page);
        Assert.Equal("NEXT", copy.NextCursor);
        var row = Assert.Single(copy.Items);
        Assert.Equal((Id, "LongTerm", "AwaitingPractitioner", true, At, At.AddDays(14)), (row.Id, row.Kind, row.Status, row.YourTurn, row.LastActionAt, row.ExpiresAt));
    }

    [Fact]
    public void The_contract_availability_of_a_card_and_of_the_own_form_are_read_back()
    {
        var card = new Page<PractitionerCard>([new PractitionerCard(Id, "demo-anna", "Anna Demowska", "Audyty", null, [Badge.Email], null, true, new MoneyDto(25000), ["audyt-kodu"], [30],
            new Gabinety.Contracts.Directory.ContractAvailabilityDto(new DateOnly(2027, 2, 23), 20, new MoneyDto(11000)))]);
        var copy = Read<W.PageDto<W.PractitionerCardDto>>(card);
        Assert.Equal((new DateOnly(2027, 2, 23), 20, 11000L), (copy.Items[0].Contract!.AvailableFrom, copy.Items[0].Contract!.HoursPerWeek, copy.Items[0].Contract!.Rate!.AmountMinor));

        var none = Read<W.PageDto<W.PractitionerCardDto>>(new Page<PractitionerCard>([new PractitionerCard(Id, "s", "N", "H", null, [], null, true, new MoneyDto(5000), [], [30])]));
        Assert.Null(none.Items[0].Contract);

        Assert.Equal(("available_from", new DateOnly(2027, 2, 23), 20, 11000L),
            (Read<W.ContractAvailabilitySettingsDto>(new ContractAvailabilitySettings(ContractStatus.AvailableFrom, new DateOnly(2027, 2, 23), 20, 11000)) is var s ? (s.Status, s.AvailableFrom, s.HoursPerWeek, s.RateMinor) : default));
    }

    [Fact]
    public void The_own_form_is_written_the_way_the_service_reads_it()
    {
        var body = new W.ContractAvailabilitySettingsDto("available_now", null, 20, 11000);
        var read = JsonSerializer.Deserialize<ContractAvailabilitySettings>(JsonSerializer.Serialize(body, W.ApiClient.Json), Service)!;
        Assert.Equal((ContractStatus.AvailableNow, (DateOnly?)null, (int?)20, (long?)11000), (read.Status, read.AvailableFrom, read.HoursPerWeek, read.RateMinor));
        var off = JsonSerializer.Deserialize<ContractAvailabilitySettings>(JsonSerializer.Serialize(new W.ContractAvailabilitySettingsDto("not_available", null, null, null), W.ApiClient.Json), Service)!;
        Assert.Equal(ContractStatus.NotAvailable, off.Status);
    }

    [Fact]
    public void The_flat_profile_carries_the_contract_next_to_the_card_fields()
    {
        const string json = """{"id":"018f0000-0000-7000-8000-0000000000c1","slug":"demo-anna","displayName":"Anna","headline":"H","badges":["email"],"isNew":true,"fromPrice":{"amountMinor":5000,"currency":"PLN"},"problemSlugs":[],"durations":[30],"contract":{"availableFrom":"2027-02-23","hoursPerWeek":20,"rate":{"amountMinor":11000,"currency":"PLN"}},"bio":"B","offerings":[],"completedCount":0,"memberSince":"2026-11-03T09:00:00Z"}""";
        var profile = JsonSerializer.Deserialize<W.PractitionerProfileDto>(json, W.ApiClient.Json)!;
        Assert.Equal((new DateOnly(2027, 2, 23), 20, 11000L), (profile.Contract!.AvailableFrom, profile.Contract.HoursPerWeek, profile.Contract.Rate!.AmountMinor));
        Assert.Null(JsonSerializer.Deserialize<W.PractitionerProfileDto>(json.Replace("\"contract\":{\"availableFrom\":\"2027-02-23\",\"hoursPerWeek\":20,\"rate\":{\"amountMinor\":11000,\"currency\":\"PLN\"}},", ""), W.ApiClient.Json)!.Contract);
    }

    [Fact]
    public async Task The_list_query_carries_the_contract_filter_only_when_it_is_on()
    {
        var backend = new FakeBackend { Respond = _ => FakeBackend.Json(new W.PageDto<W.PractitionerCardDto>([], null)) };
        var client = new W.DirectoryClient(new HttpClient(backend) { BaseAddress = new Uri("http://directory.test") }, new W.VisitorAddressAccessor(new HttpContextAccessor()));
        await client.PractitionersAsync(new W.PractitionerQuery(Problem: "audyt-kodu", Contract: true));
        await client.PractitionersAsync(new W.PractitionerQuery(Problem: "audyt-kodu"));
        Assert.Equal(["/api/v1/practitioners?problem=audyt-kodu&contract=1", "/api/v1/practitioners?problem=audyt-kodu"], backend.Requests.Select(r => r.PathAndQuery));
    }
}
