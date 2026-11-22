using System.Net;
using System.Text.Json;
using Gabinety.Web.Api;
using Gabinety.Web.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Web.Tests.Backends;

/// <summary>
/// The admin calls of the three typed clients against canned answers in the exact JSON the services write (camelCase, instants with Z, the property names of their admin records: AdminPractitionerRow, AdminDisputeItem,
/// AdminReviewRow). The services declare these records beside their endpoints (not in Gabinety.Contracts yet), so this is where the wire spelling of the copies is pinned.
/// </summary>
public class AdminClientTests : IClassFixture<WebFactory>
{
    readonly WebFactory _web;
    public AdminClientTests(WebFactory web) => _web = web;
    static readonly Guid Id = Guid.Parse("018f0000-0000-7000-8000-000000000001");

    (T Client, IServiceScope Scope) Get<T>() where T : notnull { var scope = _web.Services.CreateScope(); return (scope.ServiceProvider.GetRequiredService<T>(), scope); }

    [Fact]
    public async Task The_verification_queue_is_read_with_a_status_a_limit_a_cursor_and_the_bearer_and_parsed_from_the_services_json()
    {
        var (client, scope) = Get<DirectoryClient>(); using var _ = scope;
        _web.Directory.Respond = _ => FakeBackend.Raw($$"""{"items":[{"id":"{{Id}}","slug":"demo-anna","displayName":"Anna Demowska","headline":"H","status":"Submitted","statusReason":null,"githubLinked":true,"githubLogin":"demo-dev","nipValid":false,"createdAt":"2026-11-20T08:00:00Z","submittedAt":"2026-11-24T12:00:09Z"}],"nextCursor":"CUR"}""");
        var before = _web.Directory.Calls;
        var page = await client.AdminPractitionersAsync("tok-1", "Submitted", "C+/=", 12);
        Assert.True(page.IsOk);
        var row = Assert.Single(page.Value!.Items);
        Assert.Equal(("Anna Demowska", "Submitted", true, false, "CUR"), (row.DisplayName, row.Status, row.GithubLinked, row.NipValid, page.Value.NextCursor));
        Assert.Equal(new DateTimeOffset(2026, 11, 24, 12, 0, 9, TimeSpan.Zero), row.SubmittedAt);
        var sent = _web.Directory.Requests[before];
        Assert.Equal("/api/v1/admin/practitioners?status=Submitted&limit=12&cursor=C%2B%2F%3D", sent.PathAndQuery);
        Assert.Equal("Bearer tok-1", sent.Authorization);
    }

    [Fact]
    public async Task The_four_practitioner_actions_post_to_their_own_path_and_only_reject_and_suspend_carry_the_trimmed_reason()
    {
        var (client, scope) = Get<DirectoryClient>(); using var _ = scope;
        _web.Directory.Respond = _ => new HttpResponseMessage(HttpStatusCode.NoContent);
        var before = _web.Directory.Calls;
        foreach (var (action, reason) in new[] { ("approve", (string?)null), ("reject", "  powód odrzucenia  "), ("suspend", "powód zawieszenia"), ("reinstate", null) })
            Assert.True((await client.AdminPractitionerActionAsync("tok", Id, action, reason)).IsOk);
        var sent = _web.Directory.Requests.Skip(before).ToList();
        Assert.All(sent, r => { Assert.Equal(HttpMethod.Post, r.Method); Assert.Equal("Bearer tok", r.Authorization); });
        Assert.Equal(["approve", "reject", "suspend", "reinstate"], sent.Select(r => r.PathAndQuery.Split('/')[^1]));
        Assert.Equal([null, "{\"reason\":\"powód odrzucenia\"}", "{\"reason\":\"powód zawieszenia\"}", null], sent.Select(r => r.Body is null ? null : Normalize(r.Body)));
    }

    static string Normalize(string json) => JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    [Fact]
    public async Task A_dispute_page_and_the_resolve_call_use_the_services_wire_names()
    {
        var (client, scope) = Get<BookingClient>(); using var _ = scope;
        _web.Booking.Respond = r => r.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"anything\":1}") }
            : FakeBackend.Raw($$"""{"items":[{"bookingId":"{{Id}}","clientUserId":"u-1","practitionerUserId":"u-2","kind":"not_as_described","text":"T","openedAt":"2026-11-24T12:00:09.1234567Z","resolvedAt":null,"resolution":null,"note":null}],"nextCursor":null}""");
        var before = _web.Booking.Calls;
        var page = await client.AdminDisputesAsync("tok", "open", null, 20);
        var dispute = Assert.Single(page.Value!.Items);
        Assert.Equal((Id, "not_as_described", "u-1"), (dispute.BookingId, dispute.Kind, dispute.ClientUserId));
        Assert.Equal("/api/v1/admin/disputes?status=open&limit=20", _web.Booking.Requests[before].PathAndQuery);

        Assert.True((await client.AdminResolveAsync("tok", Id, "refund_partial", 10000, "  Notatka testowa.  ")).IsOk); // a 200 with a body is a success too
        var post = _web.Booking.Requests[^1];
        Assert.Equal($"/api/v1/admin/bookings/{Id}/resolve", post.PathAndQuery);
        var json = JsonDocument.Parse(post.Body!).RootElement;
        Assert.Equal(("refund_partial", 10000L, "Notatka testowa."), (json.GetProperty("resolution").GetString(), json.GetProperty("refundMinor").GetInt64(), json.GetProperty("note").GetString()));
    }

    [Fact]
    public async Task Reviews_and_pairs_are_read_and_a_review_is_published_or_rejected_with_the_trimmed_reason()
    {
        var (client, scope) = Get<ReputationClient>(); using var _ = scope;
        _web.Reputation.Respond = r => r.Method == HttpMethod.Post ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : r.RequestUri!.AbsolutePath.EndsWith("suspicious-pairs") ? FakeBackend.Raw($$"""{"items":[{"pairKey":"k","clientUserId":"u-1","practitionerId":"{{Id}}","bookings":4,"reviews":4}],"nextCursor":null}""")
            : FakeBackend.Raw($$"""{"items":[{"id":"{{Id}}","practitionerId":"{{Id}}","rating":4,"text":"T","status":"Held","holdReasons":["spam"],"triageFlag":"hold","triageModelId":"m","createdAt":"2026-11-24T12:00:00Z","publishedAt":null,"moderatedBy":null,"moderationNote":null}],"nextCursor":null}""");
        var before = _web.Reputation.Calls;
        var reviews = await client.AdminReviewsAsync("tok", "Held", null, 20);
        Assert.Equal(("Held", "hold", "spam"), (reviews.Value!.Items[0].Status, reviews.Value.Items[0].TriageFlag, reviews.Value.Items[0].HoldReasons![0]));
        Assert.Equal(4, (await client.AdminSuspiciousPairsAsync("tok", null, 20)).Value!.Items[0].Bookings);
        Assert.True((await client.AdminPublishReviewAsync("tok", Id)).IsOk);
        Assert.True((await client.AdminRejectReviewAsync("tok", Id, "  Powód odrzucenia opinii.  ")).IsOk);
        var sent = _web.Reputation.Requests.Skip(before).ToList();
        Assert.Equal(["/api/v1/admin/reviews?status=Held&limit=20", "/api/v1/admin/suspicious-pairs?limit=20", $"/api/v1/admin/reviews/{Id}/publish", $"/api/v1/admin/reviews/{Id}/reject"], sent.Select(r => r.PathAndQuery));
        Assert.Null(sent[2].Body);
        Assert.Equal("Powód odrzucenia opinii.", JsonDocument.Parse(sent[3].Body!).RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_refused_write_is_state_with_the_status_and_the_code_and_never_the_servers_text()
    {
        var (client, scope) = Get<DirectoryClient>(); using var _ = scope;
        _web.Directory.Respond = _ => FakeBackend.Problem(HttpStatusCode.Conflict, "invalid_transition");
        var result = await client.AdminPractitionerActionAsync("tok", Id, "approve", null);
        Assert.False(result.IsOk);
        Assert.Equal((409, "invalid_transition"), (result.Failure!.Status, result.Failure.Code));
        Assert.DoesNotContain("SERVER", result.Failure.ToString());

        _web.Directory.Respond = FakeBackend.Throw(new HttpRequestException("down secret.internal"));
        Assert.Equal(0, (await client.AdminPractitionerActionAsync("tok", Id, "approve", null)).Failure!.Status); // no answer at all
    }

    [Fact]
    public async Task A_post_is_never_retried_the_resilience_handler_retries_a_GET_only()
    {
        var (client, scope) = Get<DirectoryClient>(); using var _ = scope;
        _web.Directory.Respond = _ => FakeBackend.Problem(HttpStatusCode.ServiceUnavailable, "dependency_unavailable");
        var before = _web.Directory.Calls;
        await client.AdminPractitionerActionAsync("tok", Id, "approve", null);
        Assert.Equal(1, _web.Directory.Calls - before); // a second approve of a decision that may have been taken is a double action (SPEC 5 rule 8)
    }

    [Fact]
    public async Task The_ledger_preview_stops_reading_at_the_limit_and_skips_what_is_not_a_row()
    {
        var (client, scope) = Get<BookingClient>(); using var _ = scope;
        var csv = "booking_id,kind,amount_minor,currency,created_at\n" + string.Join("", Enumerable.Range(0, 5).Select(i => $"0b9e1d2c-0000-0000-0000-{i:D12},Refund,{100 + i},PLN,2026-12-01T10:00:00Z\n")) + "garbage line\n";
        _web.Booking.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(csv) };
        var preview = await client.AdminLedgerPreviewAsync("tok", "2026-12-01", "2026-12-31", 3);
        Assert.Equal((3, true, 100L), (preview.Value!.Rows.Count, preview.Value.HasMore, preview.Value.Rows[0].AmountMinor));
        var all = await client.AdminLedgerPreviewAsync("tok", "2026-12-01", "2026-12-31", 50);
        Assert.Equal((5, false), (all.Value!.Rows.Count, all.Value.HasMore)); // the garbage line is skipped, not counted
        Assert.Equal("/api/v1/admin/ledger.csv?from=2026-12-01&to=2026-12-31", _web.Booking.Requests[^1].PathAndQuery);
    }

    [Fact]
    public async Task Without_a_signed_in_user_the_facade_answers_401_and_calls_no_backend()
    {
        using var scope = _web.Services.CreateScope();
        var before = (_web.Directory.Calls, _web.Booking.Calls, _web.Reputation.Calls);
        var api = scope.ServiceProvider.GetRequiredService<AdminApi>();
        // nobody signed in: the state of this scope is the anonymous user (what a circuit that outlived its cookie ends up with)
        ((Microsoft.AspNetCore.Components.Authorization.IHostEnvironmentAuthenticationStateProvider)scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>())
            .SetAuthenticationState(Task.FromResult(new Microsoft.AspNetCore.Components.Authorization.AuthenticationState(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()))));
        var result = await api.Disputes("open", null, 20).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(401, result.Failure!.Status);
        Assert.Equal(before, (_web.Directory.Calls, _web.Booking.Calls, _web.Reputation.Calls));
    }
}
