using System.Net;
using System.Net.Http.Json;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Verification;
using Gabinety.Ranking;
using Microsoft.EntityFrameworkCore;
using static Gabinety.Directory.Tests.Verification.Vk;

namespace Gabinety.Directory.Tests.Verification;

public class AdminTransitionTests(VerificationFactory f) : IClassFixture<VerificationFactory>
{
    const string Admin = "/api/v1/admin/practitioners";
    static readonly PractitionerStatus[] All = Enum.GetValues<PractitionerStatus>();
    // action -> (from, to, event kind); anything else is illegal (CONTRACTS 8.2)
    static readonly Dictionary<string, (PractitionerStatus From, PractitionerStatus To, VerificationEventKind Kind)> Legal = new()
    {
        ["approve"] = (PractitionerStatus.Submitted, PractitionerStatus.Approved, VerificationEventKind.Approved),
        ["reject"] = (PractitionerStatus.Submitted, PractitionerStatus.Rejected, VerificationEventKind.Rejected),
        ["suspend"] = (PractitionerStatus.Approved, PractitionerStatus.Suspended, VerificationEventKind.Suspended),
        ["reinstate"] = (PractitionerStatus.Suspended, PractitionerStatus.Approved, VerificationEventKind.Reinstated),
    };
    const string Reason = "Does not meet the bar for review quality";

    public static IEnumerable<object[]> Matrix() =>
        from a in Legal.Keys from s in All select new object[] { a, s };

    static HttpContent? Body(string action) => action is "reject" or "suspend" ? JsonContent.Create(new { reason = Reason }) : null;

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Every_transition_is_legal_only_from_its_state_and_writes_exactly_one_event(string action, PractitionerStatus from)
    {
        var p = await f.Seed(from);
        var adminId = "demo-admin-" + Guid.NewGuid().ToString("n");
        var r = await f.As(adminId, "Admin").PostAsync($"{Admin}/{p.Id}/{action}", Body(action));
        var (legalFrom, to, kind) = Legal[action];
        var events = await f.Events(p.Id);
        var row = await f.Load(p.Id);
        if (from == legalFrom)
        {
            Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
            Assert.Equal(to, row.Status);
            var e = Assert.Single(events);
            Assert.Equal(kind, e.Kind);
            Assert.Equal(adminId, e.ActorUserId);
            Assert.Equal(action is "reject" or "suspend" ? Reason : null, e.Note);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("invalid_transition", (await Problem(r)).Code);
            Assert.Equal(from, row.Status);
            Assert.Empty(events);
        }
    }

    [Theory]
    [InlineData("approve")] [InlineData("reject")] [InlineData("suspend")] [InlineData("reinstate")]
    public async Task Unknown_id_is_404(string action)
    {
        var r = await f.As("demo-admin", "Admin").PostAsync($"{Admin}/{Guid.NewGuid()}/{action}", Body(action));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("not_found", (await Problem(r)).Code);
    }

    [Fact]
    public async Task Approve_sets_approved_at_clears_the_reason_and_computes_the_contract_ranking()
    {
        var p = await f.Seed(PractitionerStatus.Submitted, x => { x.ProfileCompleteness = 1.00m; x.OpenSlots14d = 10; x.StatusReason = "old"; });
        Assert.Equal(HttpStatusCode.NoContent, (await f.As("demo-admin", "SuperAdmin").PostAsync($"{Admin}/{p.Id}/approve", null)).StatusCode);
        var row = await f.Load(p.Id);
        Assert.Equal(82.95m, row.RankingScore);
        Assert.Equal(RankingRules.Version, row.RankingVersion);
        Assert.Equal(f.Clock.GetUtcNow(), row.ApprovedAt);
        Assert.Null(row.StatusReason);
    }

    [Theory]
    [InlineData("reject")] [InlineData("suspend")]
    public async Task Reason_must_be_10_to_500_characters_after_trimming(string action)
    {
        var from = Legal[action].From;
        foreach (var (reason, ok) in new (string?, bool)[]
                 { (new string('x', 9), false), ("   " + new string('x', 9) + "   ", false), (new string('x', 501), false), (null, false), ("", false),
                   (new string('x', 10), true) })
        {
            var p = await f.Seed(from);
            var r = await f.As("demo-admin", "Admin").PostAsJsonAsync($"{Admin}/{p.Id}/{action}", new { reason });
            if (!ok)
            {
                Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
                Assert.Equal("validation_failed", (await Problem(r)).Code);
                Assert.Equal(from, (await f.Load(p.Id)).Status);
                Assert.Empty(await f.Events(p.Id));
            }
            else Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        }
        var p500 = await f.Seed(from);
        Assert.Equal(HttpStatusCode.NoContent, (await f.As("demo-admin", "Admin").PostAsJsonAsync($"{Admin}/{p500.Id}/{action}", new { reason = "  " + new string('y', 500) + " " })).StatusCode);
        Assert.Equal(new string('y', 500), (await f.Load(p500.Id)).StatusReason);
        Assert.Equal(new string('y', 500), Assert.Single(await f.Events(p500.Id)).Note);
    }

    [Fact]
    public async Task Reject_without_a_body_is_422()
    {
        var p = await f.Seed(PractitionerStatus.Submitted);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await f.As("demo-admin", "Admin").PostAsync($"{Admin}/{p.Id}/reject", null)).StatusCode);
    }

    [Fact]
    public async Task Suspend_then_reinstate_returns_to_Approved_with_two_events_and_no_reason()
    {
        var p = await f.Seed(PractitionerStatus.Approved);
        var c = f.As("demo-admin", "Admin");
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync($"{Admin}/{p.Id}/suspend", new { reason = Reason })).StatusCode);
        var suspended = await f.Load(p.Id);
        Assert.Equal(PractitionerStatus.Suspended, suspended.Status);
        Assert.Equal(Reason, suspended.StatusReason);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"{Admin}/{p.Id}/reinstate", null)).StatusCode);
        var back = await f.Load(p.Id);
        Assert.Equal(PractitionerStatus.Approved, back.Status);
        Assert.Null(back.StatusReason);
        Assert.Equal([VerificationEventKind.Suspended, VerificationEventKind.Reinstated], (await f.Events(p.Id)).Select(e => e.Kind));
    }

    // ---- events written by the profile code (Submit, NipChanged)

    const string Me = "/api/v1/me/practitioner";
    static readonly UpdatePractitionerRequest Full = new("Demo Name", "Senior .NET reviewer", new string('b', 220),
        "https://portfolio.example.test/me", "https://meet.google.com/abc-defg-hij", "5260250995");

    async Task<(HttpClient C, Guid Id)> Ready()
    {
        var p = await f.Seed(PractitionerStatus.Draft, x => x.GithubLinked = true);
        var c = f.As(p.UserId);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync(Me, Full)).StatusCode);
        await f.Db(async db =>
        {
            db.Offerings.Add(new Gabinety.Directory.Features.Offerings.Offering { Id = Guid.NewGuid(), PractitionerId = p.Id, Title = "Demo", Description = "Demo", DurationMinutes = 30, PriceMinor = 10000,
                ProblemSlug = "code-review", IsActive = true, CreatedAt = f.Clock.GetUtcNow() });
            await db.SaveChangesAsync();
            return 0;
        });
        return (c, p.Id);
    }

    [Fact]
    public async Task Submit_writes_one_Submitted_event_by_the_practitioner()
    {
        var (c, id) = await Ready();
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync(Me + "/submit", null)).StatusCode);
        var e = Assert.Single(await f.Events(id));
        Assert.Equal(VerificationEventKind.Submitted, e.Kind);
        Assert.Equal((await f.Load(id)).UserId, e.ActorUserId);
    }

    [Fact]
    public async Task Changing_the_NIP_of_an_approved_profile_returns_it_to_Submitted_with_one_NipChanged_event()
    {
        var (c, id) = await Ready();
        await f.Db(async db => { (await db.Practitioners.SingleAsync(p => p.Id == id)).Status = PractitionerStatus.Approved; await db.SaveChangesAsync(); return 0; });
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync(Me, Full)).StatusCode); // same NIP: nothing happens
        Assert.Empty(await f.Events(id));
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync(Me, Full with { Nip = "1234563218" })).StatusCode);
        Assert.Equal(PractitionerStatus.Submitted, (await f.Load(id)).Status);
        var e = Assert.Single(await f.Events(id));
        Assert.Equal(VerificationEventKind.NipChanged, e.Kind);
        // a NIP change on a profile that is not Approved moves nothing and writes nothing
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync(Me, Full)).StatusCode);
        Assert.Single(await f.Events(id));
    }

    [Fact]
    public void The_events_table_has_no_update_or_delete_path()
    {
        // the only writer offers one public method, and it adds
        var methods = typeof(VerificationEvents).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly);
        Assert.Equal(["Record"], methods.Select(m => m.Name));
        // and no endpoint changes or removes an event: a route about events may only be read (GET; T-03.10 added the read endpoint)
        var routes = f.Services.GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>().SelectMany(s => s.Endpoints)
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>().Where(e => e.RoutePattern.RawText?.Contains("event", StringComparison.OrdinalIgnoreCase) == true);
        Assert.All(routes, r => Assert.Equal(["GET"], r.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods));
    }
}

public class AdminAuthorizationTests(VerificationFactory f) : IClassFixture<VerificationFactory>
{
    static readonly (HttpMethod Method, string Path, bool SuperAdminOnly)[] Endpoints =
    [
        (HttpMethod.Get, "/api/v1/admin/practitioners", false),
        (HttpMethod.Get, $"/api/v1/admin/practitioners/{Guid.NewGuid()}", false),
        (HttpMethod.Get, $"/api/v1/admin/practitioners/{Guid.NewGuid()}/verification-events", false),
        (HttpMethod.Post, $"/api/v1/admin/practitioners/{Guid.NewGuid()}/approve", false),
        (HttpMethod.Post, $"/api/v1/admin/practitioners/{Guid.NewGuid()}/reject", false),
        (HttpMethod.Post, $"/api/v1/admin/practitioners/{Guid.NewGuid()}/suspend", false),
        (HttpMethod.Post, $"/api/v1/admin/practitioners/{Guid.NewGuid()}/reinstate", false),
        (HttpMethod.Put, "/api/v1/admin/moderators/demo-mod", true),
        (HttpMethod.Delete, "/api/v1/admin/moderators/demo-mod", true),
    ];

    static HttpRequestMessage Req((HttpMethod Method, string Path, bool _) e) =>
        new(e.Method, e.Path) { Content = e.Method == HttpMethod.Post ? JsonContent.Create(new { reason = "a sufficiently long reason" }) : null };

    [Fact]
    public async Task Unauthenticated_is_401_on_every_admin_endpoint()
    {
        foreach (var e in Endpoints) Assert.Equal(HttpStatusCode.Unauthorized, (await f.As(null).SendAsync(Req(e))).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("User")]
    public async Task A_signed_in_user_without_the_role_is_403_on_every_admin_endpoint(string? role)
    {
        foreach (var e in Endpoints) Assert.Equal(HttpStatusCode.Forbidden, (await f.As("demo-plain", role).SendAsync(Req(e))).StatusCode);
    }

    [Fact]
    public async Task Admin_passes_the_policy_everywhere_except_the_super_admin_only_endpoints()
    {
        foreach (var e in Endpoints)
        {
            var s = (await f.As("demo-admin", "Admin").SendAsync(Req(e))).StatusCode;
            if (e.SuperAdminOnly) Assert.Equal(HttpStatusCode.Forbidden, s);
            else Assert.NotEqual(HttpStatusCode.Forbidden, s);
        }
    }

    [Fact]
    public async Task SuperAdmin_can_put_and_delete_moderators_idempotently()
    {
        var c = f.As("demo-root", "SuperAdmin");
        var url = "/api/v1/admin/moderators/demo-mod-" + Guid.NewGuid().ToString("n");
        var id = url[(url.LastIndexOf('/') + 1)..];
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync(url, null)).StatusCode);
        Assert.Equal(1, await f.Db(db => db.Moderators.CountAsync(m => m.UserId == id)));
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync(url)).StatusCode);
        Assert.Equal(0, await f.Db(db => db.Moderators.CountAsync(m => m.UserId == id)));
    }

    [Fact]
    public async Task Admin_cannot_add_a_moderator_and_an_overlong_id_is_422()
    {
        var id = "demo-mod-" + Guid.NewGuid().ToString("n");
        Assert.Equal(HttpStatusCode.Forbidden, (await f.As("demo-admin", "Admin").PutAsync("/api/v1/admin/moderators/" + id, null)).StatusCode);
        Assert.Equal(0, await f.Db(db => db.Moderators.CountAsync(m => m.UserId == id)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await f.As("demo-root", "SuperAdmin").PutAsync("/api/v1/admin/moderators/" + new string('m', 65), null)).StatusCode);
    }
}

public class AdminListTests(VerificationFactory f) : IClassFixture<VerificationFactory>
{
    const string Url = "/api/v1/admin/practitioners";
    HttpClient Admin => f.As("demo-admin", "Admin");

    async Task<AdminPractitionerPage> Page(string query) => (await Admin.GetFromJsonAsync<AdminPractitionerPage>(Url + query, J))!;

    /// <summary>Submitted practitioners with a Submitted event each; many share one timestamp so the id decides.</summary>
    async Task<List<Guid>> SeedSubmitted(int n, int daysBack)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < n; i++)
        {
            var at = f.Clock.GetUtcNow().AddDays(-daysBack).AddMinutes(i / 3); // groups of three share a time
            var p = await f.Seed(PractitionerStatus.Submitted);
            await f.Db(async db =>
            {
                db.VerificationEvents.Add(new VerificationEvent { Id = Guid.NewGuid(), PractitionerId = p.Id, Kind = VerificationEventKind.Submitted, ActorUserId = p.UserId, At = at });
                await db.SaveChangesAsync();
                return 0;
            });
            ids.Add(p.Id);
        }
        return ids;
    }

    static DateTimeOffset Key(AdminPractitionerRow r) => r.SubmittedAt ?? r.CreatedAt;

    [Fact]
    public async Task Default_is_Submitted_oldest_first_with_the_contract_row_and_paging_has_no_gaps_or_duplicates()
    {
        var mine = await SeedSubmitted(30, 4000);
        await f.Seed(PractitionerStatus.Approved); // must not appear in the default list
        var all = new List<AdminPractitionerRow>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await Page("?limit=7" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            Assert.True(page.Items.Count <= 7);
            all.AddRange(page.Items); cursor = page.NextCursor; pages++;
        } while (cursor is not null && pages < 100);

        Assert.Equal(all.Count, all.Select(r => r.Id).Distinct().Count());
        Assert.All(all, r => Assert.Equal(PractitionerStatus.Submitted, r.Status));
        Assert.Equal(all.OrderBy(Key).ThenBy(r => r.Id).Select(r => r.Id), all.Select(r => r.Id));
        Assert.Equal(mine.Count, all.Count(r => mine.Contains(r.Id)));
        Assert.True(all.Select(r => r.Id).Intersect(mine).Count() == 30);
        Assert.NotNull(all.First(r => mine.Contains(r.Id)).SubmittedAt);
    }

    [Fact]
    public async Task Limit_defaults_to_12_and_is_clamped_to_50()
    {
        await SeedSubmitted(55, 3000);
        Assert.Equal(12, (await Page("")).Items.Count);
        Assert.Equal(50, (await Page("?limit=500")).Items.Count);
        Assert.Single((await Page("?limit=0")).Items);
        Assert.Single((await Page("?limit=-3")).Items);
    }

    [Fact]
    public async Task Status_filter_and_bad_values()
    {
        var p = await f.Seed(PractitionerStatus.Suspended);
        var suspended = await Page("?status=Suspended&limit=50");
        Assert.Contains(suspended.Items, r => r.Id == p.Id);
        Assert.All(suspended.Items, r => Assert.Equal(PractitionerStatus.Suspended, r.Status));
        foreach (var bad in new[] { "?status=Nonsense", "?status=9", "?cursor=!!!", "?cursor=" + Convert.ToBase64String("x"u8.ToArray()) })
        {
            var r = await Admin.GetAsync(Url + bad);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            Assert.Equal("validation_failed", (await Problem(r)).Code);
        }
    }
}
