using System.Net;
using System.Text.Json;
using Gabinety.Web.Api;
using Gabinety.Web.Auth;
using Gabinety.Web.Tests.Pages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Web.Tests.Backends;

/// <summary>The writes of the booking flow and the client area at the HTTP boundary: what is sent, what is read back, and what never leaves (a message of the server, a text of the user).</summary>
public class ClientAreaClientTests
{
    sealed class Token(string? value) : IAccessTokenSource { public Task<string?> AccessTokenAsync(CancellationToken ct = default) => Task.FromResult(value); }

    static (BookingClient Client, FakeBackend Backend) Bookings(string? token = "tok")
    {
        var backend = new FakeBackend();
        return (new BookingClient(new HttpClient(backend) { BaseAddress = new Uri("http://booking.test") }, new VisitorAddressAccessor(new HttpContextAccessor()), new Token(token)), backend);
    }

    static readonly Guid Id = Guid.Parse("018f0000-0000-7000-8000-0000000000b1");

    [Fact]
    public async Task Create_posts_the_json_the_service_reads_with_the_key_in_the_header_and_the_instant_with_a_Z()
    {
        var (client, backend) = Bookings();
        backend.Respond = _ => FakeBackend.Json(new CreateBookingResponseDto(Id, "PendingPayment", DateTimeOffset.UtcNow, "/checkout/mock/x"), HttpStatusCode.Created);
        var key = Guid.NewGuid();
        var offering = Guid.NewGuid();
        var result = await client.CreateAsync(key, new CreateBookingRequestDto(offering, new DateTimeOffset(2026, 12, 8, 12, 0, 0, TimeSpan.FromHours(1)), new BriefDto("Opis problemu demo, syntetyczny.", "audyt-kodu", true)));
        Assert.True(result.IsOk);
        Assert.Equal("/checkout/mock/x", result.Value!.CheckoutUrl);
        var sent = Assert.Single(backend.Requests);
        Assert.Equal((HttpMethod.Post, "/api/v1/bookings"), (sent.Method, sent.PathAndQuery));
        using var body = JsonDocument.Parse(sent.Body!);
        Assert.Equal("2026-12-08T11:00:00Z", body.RootElement.GetProperty("startUtc").GetString()); // the offset is normalised to UTC and written with Z
        Assert.Equal(offering.ToString(), body.RootElement.GetProperty("offeringId").GetString());
        Assert.True(body.RootElement.GetProperty("brief").GetProperty("aiAssisted").GetBoolean());
        Assert.Equal(["offeringId", "startUtc", "brief"], body.RootElement.EnumerateObject().Select(p => p.Name)); // camelCase, nothing extra
    }

    [Fact]
    public async Task The_idempotency_key_is_a_header_never_in_the_body_or_the_url()
    {
        var (client, backend) = Bookings();
        string? header = null;
        backend.Respond = r => { header = r.Headers.GetValues("Idempotency-Key").Single(); return FakeBackend.Json(new CreateBookingResponseDto(Id, "PendingPayment", DateTimeOffset.UtcNow, "/x"), HttpStatusCode.Created); };
        var key = Guid.NewGuid();
        await client.CreateAsync(key, new CreateBookingRequestDto(Guid.NewGuid(), DateTimeOffset.UtcNow, new BriefDto("Opis problemu demo, syntetyczny.", null, false)));
        Assert.Equal(key.ToString("D"), header);
        Assert.DoesNotContain(key.ToString(), backend.Requests.Single().PathAndQuery + backend.Requests.Single().Body);
    }

    [Fact]
    public async Task A_failure_carries_the_status_the_code_and_the_names_of_the_refused_fields_never_a_message()
    {
        var (client, backend) = Bookings();
        backend.Respond = _ => FakeBackend.Raw("""{"title":"SERVER-TITLE","detail":"SERVER-DETAIL","code":"validation_failed","status":422,"errors":{"brief.text":["brief.text must be 20 to 2000 characters"],"Reason":["too long"]}}""", HttpStatusCode.UnprocessableEntity);
        var result = await client.CancelAsync(Id, "powód");
        var failure = result.Failure!;
        Assert.Equal((422, "validation_failed"), (failure.Status, failure.Code));
        Assert.Equal(["brief.text", "Reason"], failure.Fields);
        Assert.True(failure.Names("brief")); // brief.text is part of brief
        Assert.True(failure.Names("reason")); // case-insensitive
        Assert.False(failure.Names("text"));
        Assert.DoesNotContain("SERVER", JsonSerializer.Serialize(failure));
        Assert.DoesNotContain("20 to 2000", JsonSerializer.Serialize(failure)); // the message is not kept
    }

    [Theory]
    [InlineData("""{"code":"x","errors":{"a b":["m"],"ok_1":["m"],"":["m"]}}""", new[] { "ok_1" })]
    [InlineData("""{"code":"x","errors":["m"]}""", null)]
    [InlineData("""{"code":"x"}""", null)]
    [InlineData("""not json""", null)]
    public async Task Field_names_are_kept_only_when_they_look_like_field_names(string json, string[]? expected)
    {
        var (client, backend) = Bookings();
        backend.Respond = _ => FakeBackend.Raw(json, HttpStatusCode.BadRequest);
        Assert.Equal(expected, (await client.GetAsync(Id)).Failure!.Fields);
    }

    [Fact]
    public async Task The_code_of_a_problem_answer_is_kept_only_when_it_is_a_plain_code()
    {
        var (client, backend) = Bookings();
        backend.Respond = _ => FakeBackend.Raw("""{"code":"SERVER-TITLE: <b>x</b>"}""", HttpStatusCode.Conflict);
        Assert.Equal("http_409", (await client.GetAsync(Id)).Failure!.Code);
    }

    [Fact]
    public async Task Cancel_sends_the_trimmed_reason_or_an_empty_object()
    {
        var (client, backend) = Bookings();
        backend.Respond = _ => FakeBackend.Json(ClientAreaFixture.Detail());
        await client.CancelAsync(Id, "  Zmiana planów  ");
        await client.CancelAsync(Id, null);
        await client.CancelAsync(Id, "   ");
        Assert.Equal("Zmiana planów", JsonDocument.Parse(backend.Requests[0].Body!).RootElement.GetProperty("reason").GetString()); // the wire may escape non-ASCII; the value is what counts
        Assert.Equal(["{}", "{}"], backend.Requests.Skip(1).Select(r => r.Body!));
        Assert.All(backend.Requests, r => Assert.Equal($"/api/v1/bookings/{Id}/cancel", r.PathAndQuery));
    }

    [Fact]
    public async Task Dispute_sends_the_wire_kind_and_the_trimmed_text_and_no_show_sends_no_body()
    {
        var (client, backend) = Bookings();
        backend.Respond = _ => FakeBackend.Json(ClientAreaFixture.Detail());
        await client.DisputeAsync(Id, "not_as_described", "  Opis sporu.  ");
        await client.ReportNoShowAsync(Id);
        Assert.Equal("""{"kind":"not_as_described","text":"Opis sporu."}""", backend.Requests[0].Body);
        Assert.Equal(($"/api/v1/bookings/{Id}/report-client-no-show", null), (backend.Requests[1].PathAndQuery, backend.Requests[1].Body));
    }

    [Fact]
    public async Task Simulate_needs_no_body_to_read_back_and_a_403_is_a_failure()
    {
        var (client, backend) = Bookings();
        backend.Respond = _ => new HttpResponseMessage(HttpStatusCode.NoContent);
        Assert.True((await client.SimulatePaymentAsync(Id, "succeeded")).IsOk);
        Assert.Equal($$"""{"bookingId":"{{Id}}","outcome":"succeeded"}""", backend.Requests.Single().Body);
        backend.Respond = _ => FakeBackend.Problem(HttpStatusCode.Forbidden, "forbidden");
        Assert.Equal(403, (await client.SimulatePaymentAsync(Id, "failed")).Failure!.Status);
    }

    [Fact]
    public async Task Without_a_session_no_bearer_is_sent_and_the_service_answers_401()
    {
        var (client, backend) = Bookings(token: null);
        backend.Respond = _ => FakeBackend.Problem(HttpStatusCode.Unauthorized, "unauthenticated");
        var result = await client.GetAsync(Id);
        Assert.Null(backend.Requests.Single().Authorization);
        Assert.True(result.Failure!.IsUnauthenticated);
    }

    [Fact]
    public async Task A_backend_that_is_down_is_a_failure_with_status_0_not_an_exception()
    {
        var (client, backend) = Bookings();
        backend.Respond = FakeBackend.Throw(new HttpRequestException("down"));
        var result = await client.CreateAsync(Guid.NewGuid(), new CreateBookingRequestDto(Guid.NewGuid(), DateTimeOffset.UtcNow, new BriefDto("x", null, false)));
        Assert.Equal((0, "unavailable"), (result.Failure!.Status, result.Failure.Code));
    }

    [Fact]
    public async Task Reviews_replies_and_me_use_the_routes_of_the_services()
    {
        var backend = new FakeBackend { Respond = r => r.RequestUri!.AbsolutePath.EndsWith("/reply") ? new HttpResponseMessage(HttpStatusCode.Created) : FakeBackend.Json(new CreateReviewResponseDto(Id, "Published"), HttpStatusCode.Created) };
        var reviews = new ReviewsClient(new HttpClient(backend) { BaseAddress = new Uri("http://r.test") }, new VisitorAddressAccessor(new HttpContextAccessor()), new Token("tok"));
        Assert.Equal("Published", (await reviews.CreateAsync(Id, 5, "  Treść opinii.  ")).Value!.Status);
        Assert.True((await reviews.ReplyAsync(Id, " Dziękuję. ")).IsOk);
        var review = JsonDocument.Parse(backend.Requests[0].Body!).RootElement;
        Assert.Equal((Id.ToString(), 5, "Treść opinii."), (review.GetProperty("bookingId").GetString()!, review.GetProperty("rating").GetInt32(), review.GetProperty("text").GetString()!));
        Assert.Equal("Dziękuję.", JsonDocument.Parse(backend.Requests[1].Body!).RootElement.GetProperty("text").GetString());
        Assert.Equal(["/api/v1/reviews", $"/api/v1/reviews/{Id}/reply"], backend.Requests.Select(r => r.PathAndQuery));
    }

    [Fact]
    public async Task The_profile_is_read_by_slug_escaped()
    {
        var backend = new FakeBackend { Respond = _ => FakeBackend.Json(ClientAreaFixture.Profile()) };
        var directory = new DirectoryClient(new HttpClient(backend) { BaseAddress = new Uri("http://d.test") }, new VisitorAddressAccessor(new HttpContextAccessor()));
        Assert.Single((await directory.ProfileAsync("demo anna/../x")).Value!.Offerings);
        Assert.Equal("/api/v1/practitioners/demo%20anna%2F..%2Fx", backend.Requests.Single().PathAndQuery);
    }

    // ---- SPEC 5.1 budgets: the booking write (the payment provider, 10 s) and the assistant (35 s) get more than a read's 5 s

    [Fact]
    public void The_slow_calls_ask_for_their_budget_on_the_request_and_nothing_else_does()
    {
        Assert.Equal(TimeSpan.FromSeconds(12), BookingClient.CreateBudget);
        Assert.Equal(TimeSpan.FromSeconds(35), BriefAssistantClient.Budget);
        Assert.True(BriefAssistantClient.Budget <= ApiResilience.MaxBudget);
    }

    [Fact]
    public async Task The_budget_travels_on_the_request_for_create_and_the_assistant_only()
    {
        var seen = new List<(string Path, TimeSpan? Budget)>();
        var backend = new FakeBackend { Respond = r => { seen.Add((r.RequestUri!.AbsolutePath, r.Options.TryGetValue(RequestContext.Timeout, out var t) ? t : null)); return FakeBackend.Json(new { }, HttpStatusCode.OK); } };
        var http = new HttpClient(backend) { BaseAddress = new Uri("http://b.test") };
        var visitor = new VisitorAddressAccessor(new HttpContextAccessor());
        var bookings = new BookingClient(http, visitor, new Token("t"));
        await bookings.CreateAsync(Guid.NewGuid(), new CreateBookingRequestDto(Guid.NewGuid(), DateTimeOffset.UtcNow, new BriefDto("x", null, false)));
        await bookings.CancelAsync(Id, null);
        await bookings.GetAsync(Id);
        await new BriefAssistantClient(http, visitor, new Token("t")).SuggestAsync("opis");
        Assert.Equal([("/api/v1/bookings", (TimeSpan?)TimeSpan.FromSeconds(12)), ($"/api/v1/bookings/{Id}/cancel", null), ($"/api/v1/bookings/{Id}", null), ("/api/v1/assistant/brief", TimeSpan.FromSeconds(35))], seen);
    }

    [Fact]
    public async Task Through_the_real_pipeline_a_write_that_asked_for_a_budget_outlasts_the_5_second_read_limit_and_one_that_did_not_does_not()
    {
        await using var web = new WebFactory();
        var slow = TimeSpan.FromSeconds(5.5);
        web.Booking.Delay = web.Reputation.Delay = slow;
        web.Booking.Respond = _ => FakeBackend.Json(new CreateBookingResponseDto(Id, "PendingPayment", DateTimeOffset.UtcNow, "/x"), HttpStatusCode.Created);
        web.Reputation.Respond = _ => FakeBackend.Json(new CreateReviewResponseDto(Id, "Published"), HttpStatusCode.Created);
        // The clients are built over the handler chains the host really uses (service discovery, the one resilience handler, the fake as the primary handler).
        var handlers = web.Services.GetRequiredService<IHttpMessageHandlerFactory>();
        var visitor = new VisitorAddressAccessor(new HttpContextAccessor());
        var bookings = new BookingClient(new HttpClient(handlers.CreateHandler(Backend.Booking)) { BaseAddress = new Uri(Backend.BaseAddress(Backend.Booking)) }, visitor, new Token("t"));
        var reviews = new ReviewsClient(new HttpClient(handlers.CreateHandler(Backend.Reputation)) { BaseAddress = new Uri(Backend.BaseAddress(Backend.Reputation)) }, visitor, new Token("t"));

        // A write with no budget is cut at the 5 s of a read; the one that asked for its budget is not.
        var withBudget = bookings.CreateAsync(Guid.NewGuid(), new CreateBookingRequestDto(Guid.NewGuid(), DateTimeOffset.UtcNow, new BriefDto("Opis problemu demo, syntetyczny.", null, false)));
        var without = reviews.CreateAsync(Id, 5, "Treść opinii, syntetyczna.");
        var (booked, reviewed) = (await withBudget, await without);
        Assert.True(booked.IsOk, $"the booking write was cut: {booked.Failure}");
        Assert.False(reviewed.IsOk);
        Assert.Equal(0, reviewed.Failure!.Status);
        Assert.Equal(1, web.Booking.Calls);
        Assert.Equal(1, web.Reputation.Calls); // and neither was retried
    }
}
