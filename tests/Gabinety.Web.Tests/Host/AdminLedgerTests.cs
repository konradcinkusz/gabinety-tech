using System.Net;
using System.Net.Http.Headers;
using Gabinety.Web.Api;
using Gabinety.Web.Text;

namespace Gabinety.Web.Tests.AdminPages;

/// <summary>A response body the test controls chunk by chunk: what the backend sends, when, and how it dies.</summary>
sealed class ScriptedBody(Func<CancellationToken, Task<byte[]?>>[] chunks) : Stream
{
    public ScriptedBody(Func<CancellationToken, Task<byte[]?>> a, Func<CancellationToken, Task<byte[]?>> b) : this([a, b]) { }
    public ScriptedBody(Func<CancellationToken, Task<byte[]?>> a) : this([a]) { }
    int _next;
    public bool Disposed { get; private set; }
    public int Delivered => _next;
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_next >= chunks.Length) return 0;
        var chunk = await chunks[_next++](ct);
        if (chunk is null) return 0;
        chunk.CopyTo(buffer.Span);
        return chunk.Length;
    }
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public static Func<CancellationToken, Task<byte[]?>> Bytes(string text) => _ => Task.FromResult<byte[]?>(System.Text.Encoding.UTF8.GetBytes(text));
    public static Func<CancellationToken, Task<byte[]?>> Fail() => _ => throw new IOException("backend connection reset: secret-backend-address.internal:5432");
    public static Func<CancellationToken, Task<byte[]?>> Wait(TaskCompletionSource gate) => async ct => { await gate.Task.WaitAsync(ct); return null; };
}

/// <summary>/admin/ksiega and its download route: the period rules, the preview table, and a response that is STREAMED, not buffered, and never a half-valid file.</summary>
public class AdminLedgerTests
{
    const string Header = "booking_id,kind,amount_minor,currency,created_at\n";
    static string Row(string kind, long amount, int n = 1) => $"0b9e1d2c-0000-0000-0000-{n:D12},{kind},{amount},PLN,2026-12-0{n}T10:00:00.1234567Z\n";
    const string Period = "from=2026-12-01&to=2026-12-15";

    static HttpResponseMessage Csv(Stream body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new StreamContent(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/csv") { CharSet = "utf-8" };
        response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "backend-chosen-name.csv" };
        return response;
    }

    static HttpResponseMessage CsvOf(string text) => Csv(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)));

    static async Task<HttpResponseMessage> DownloadAsync(AdminHost host, string query = Period, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/admin/ksiega/csv?{query}");
        request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", host.Browser.Cookies.Select(c => $"{c.Key}={c.Value}")));
        return await host.Browser.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    // ---- the page
    [Fact]
    public async Task With_no_period_the_page_shows_this_month_so_far_the_download_and_the_first_entries_as_a_table()
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Booking.Respond = _ => CsvOf(Header + Row("ClientCharge", 18000, 1) + Row("Refund", 10000, 2) + Row("PractitionerPayout", 6000, 3));
        var page = await host.Browser.GetAsync("/admin/ksiega");

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Księga", Assert.Single(page.All("h1")).TextContent);
        var today = DateTime.UtcNow;
        Assert.Equal($"{today:yyyy-MM}-01", page.One("input[name=from]")!.GetAttribute("value"));
        Assert.Equal($"{today:yyyy-MM-dd}", page.One("input[name=to]")!.GetAttribute("value"));
        Assert.Equal("Ten miesiąc", page.TextOf(".segmented a.chip[aria-current=true]"));
        Assert.Contains(page.All("main button"), b => b.TextContent.Trim() == "Pokaż okres" && b.ClassName!.Contains("btn--primary"));
        var download = page.All("main a").Single(a => a.TextContent == "Pobierz CSV");
        Assert.Equal($"/admin/ksiega/csv?from={today:yyyy-MM}-01&to={today:yyyy-MM-dd}", download.GetAttribute("href"));
        Assert.True(download.HasAttribute("download"));
        Assert.Equal(["Data", "Rodzaj", "Rezerwacja", "Kwota"], page.All("table thead th[scope=col]").Select(t => t.TextContent));
        Assert.Equal(["Płatność klienta", "Zwrot dla klienta", "Wypłata dla programisty"], page.All("tbody tr").Select(r => r.QuerySelectorAll("td")[1].TextContent));
        Assert.Equal(["180,00 zł", "100,00 zł", "60,00 zł"], page.All("tbody tr").Select(r => r.QuerySelectorAll("td")[3].TextContent.Replace(' ', ' ')));
        Assert.Equal("Wpisów w okresie: 3.", page.One(".muted")!.TextContent);
        Assert.Equal("region", page.One(".table-wrap")!.GetAttribute("role"));
        Assert.Contains("ledger.csv?from=", host.Web.Booking.Requests[0].PathAndQuery);
    }

    [Fact]
    public async Task The_preview_reads_only_the_first_rows_and_says_the_rest_is_in_the_file()
    {
        await using var host = await AdminHost.StartAsync();
        var body = new ScriptedBody([ScriptedBody.Bytes(Header), .. Enumerable.Range(1, 500).Select(i => ScriptedBody.Bytes(Row("ClientCharge", 100 + i, 1)))]);
        host.Web.Booking.Respond = _ => Csv(body);
        var page = await host.Browser.GetAsync($"/admin/ksiega?{Period}");
        Assert.Equal(50, page.All("tbody tr").Count);
        Assert.Contains("Pierwsze 50 wpisów okresu", page.One(".muted")!.TextContent);
        Assert.InRange(body.Delivered, 51, 60); // header + 50 rows + the one that proves there are more: a year of rows is never read for a preview
        Assert.True(body.Disposed);
    }

    [Theory]
    [InlineData("from=2026-12-15&to=2026-12-01", "Data końcowa nie może być wcześniejsza")]
    [InlineData("from=2026-02-30&to=2026-03-01", "rrrr-mm-dd")]
    [InlineData("from=2026-12-01", "rrrr-mm-dd")]
    [InlineData("from=2026-01-01&to=2027-12-31", "najwyżej 366 dni")]
    public async Task A_period_that_is_not_valid_is_an_alert_and_the_backend_is_not_called(string query, string expected)
    {
        await using var host = await AdminHost.StartAsync();
        var page = await host.Browser.GetAsync($"/admin/ksiega?{query}");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains(expected, page.TextOf("[data-testid=ledger-period-problem]"));
        Assert.Equal("alert", page.One("[data-testid=ledger-period-problem]")!.GetAttribute("role"));
        Assert.DoesNotContain(page.All("main a"), a => a.TextContent == "Pobierz CSV"); // no download link for a period that would be refused
        Assert.Equal(0, host.Web.Booking.Calls);
    }

    [Fact]
    public async Task The_presets_follow_the_clock_and_mark_the_chosen_one()
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Booking.Respond = _ => CsvOf(Header);
        var page = await host.Browser.GetAsync("/admin/ksiega");
        Assert.Equal(["Ten miesiąc", "Poprzedni miesiąc", "Ten rok"], page.All(".segmented a.chip").Select(a => a.TextContent));
        var last = page.All(".segmented a.chip")[1].GetAttribute("href")!;
        var chosen = await host.Browser.GetAsync(last);
        Assert.Equal("Poprzedni miesiąc", chosen.TextOf(".segmented a.chip[aria-current=true]"));
        Assert.Equal("Brak wpisów w tym okresie.", chosen.TextOf(".empty-state"));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Księga jest chwilowo niedostępna")]
    [InlineData(HttpStatusCode.BadRequest, "Serwer odrzucił okres")]
    public async Task A_refused_preview_is_an_alert_with_its_own_words_and_the_page_still_renders(HttpStatusCode status, string expected)
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Booking.Respond = _ => FakeBackend.Problem(status, "x_code");
        var page = await host.Browser.GetAsync($"/admin/ksiega?{Period}");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains(expected, page.TextOf("[data-testid=unavailable]"));
        Assert.Equal("Księga", page.TextOf("h1"));
        Assert.DoesNotContain("SERVER-TITLE", page.Html);
    }

    [Theory]
    [InlineData("/admin/ksiega?from=2026-12-01&to=2026-12-15")]
    [InlineData("/admin/spory")]
    [InlineData("/admin/opinie")]
    [InlineData("/admin/podejrzane")]
    [InlineData("/admin/weryfikacja")]
    public async Task A_service_that_refuses_the_role_makes_the_list_a_403_with_the_forbidden_hook_and_no_server_text(string route)
    {
        await using var host = await AdminHost.StartAsync(); // the cookie says Admin; the service says no (a role withdrawn since sign-in): the service is the boundary
        host.Web.Booking.Respond = host.Web.Reputation.Respond = host.Web.Directory.Respond = _ => FakeBackend.Problem(HttpStatusCode.Forbidden, "forbidden");
        var page = await host.Browser.GetAsync(route);
        Assert.Equal(HttpStatusCode.Forbidden, page.Status);
        Assert.Equal(PlAdmin.Forbidden.Body, page.TextOf("[data-testid=forbidden]"));
        Assert.DoesNotContain("SERVER-TITLE", page.Html);
    }

    // ---- the download route: STREAMED
    [Fact]
    public async Task The_download_passes_the_backends_bytes_on_with_the_headers_of_J6_and_the_administrators_own_bearer()
    {
        await using var host = await AdminHost.StartAsync();
        var text = Header + Row("ClientCharge", 18000);
        host.Web.Booking.Respond = _ => CsvOf(text);
        var response = await DownloadAsync(host);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        Assert.Equal("attachment; filename=\"ledger-2026-12-01-2026-12-15.csv\"", response.Content.Headers.ContentDisposition!.ToString()); // built here, not the backend's "backend-chosen-name.csv"
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(text, await response.Content.ReadAsStringAsync());
        Assert.Equal("booking_id,kind,amount_minor,currency,created_at", (await response.Content.ReadAsStringAsync()).Split('\n')[0]);
        var asked = Assert.Single(host.Web.Booking.Requests);
        Assert.Equal("/api/v1/admin/ledger.csv?from=2026-12-01&to=2026-12-15", asked.PathAndQuery);
        Assert.StartsWith("Bearer ", asked.Authorization);
        Assert.DoesNotContain(host.Web.Errors, e => e.Contains("Unhandled"));
    }

    [Fact]
    public async Task The_download_is_streamed_the_first_rows_reach_the_browser_while_the_backend_is_still_sending()
    {
        await using var host = await AdminHost.StartAsync();
        var gate = new TaskCompletionSource();
        // the backend sends two chunks and then waits: the first is read before the response starts, the SECOND goes through the copy loop, so a route that buffers the body hangs on the gate and never delivers it
        var body = new ScriptedBody([ScriptedBody.Bytes(Header + Row("ClientCharge", 18000, 1)), ScriptedBody.Bytes(Row("Refund", 10000, 2)), ScriptedBody.Wait(gate)]);
        host.Web.Booking.Respond = _ => Csv(body);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        using var response = await DownloadAsync(host, ct: timeout.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        var received = new System.Text.StringBuilder();
        var buffer = new byte[4096];
        while (!received.ToString().Contains(",Refund,")) // returns although the backend has not finished
        {
            var read = await stream.ReadAsync(buffer, timeout.Token);
            Assert.NotEqual(0, read);
            received.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, read));
        }
        Assert.StartsWith("booking_id,kind", received.ToString());
        Assert.False(gate.Task.IsCompleted);

        gate.SetResult(); // now the backend ends
        Assert.Equal(0, await stream.ReadAsync(buffer, timeout.Token));
    }

    [Fact]
    public async Task A_backend_that_fails_before_the_first_byte_is_a_503_problem_the_browser_can_show_and_leaks_nothing()
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Booking.Respond = _ => Csv(new ScriptedBody(ScriptedBody.Fail()));
        var response = await DownloadAsync(host);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("dependency_unavailable", body);
        Assert.DoesNotContain("secret-backend-address", body);
        Assert.Null(response.Content.Headers.ContentDisposition); // not an attachment
        Assert.DoesNotContain(host.Web.Errors, e => e.Contains("secret-backend-address")); // nor in the log
    }

    [Fact]
    public async Task A_backend_that_dies_in_the_middle_makes_the_download_FAIL_it_never_ends_as_a_complete_looking_file()
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Booking.Respond = _ => Csv(new ScriptedBody(ScriptedBody.Bytes(Header + Row("ClientCharge", 18000)), ScriptedBody.Fail()));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response = await DownloadAsync(host, ct: timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // the status line was already sent: all the route can do is break the transfer

        var received = new System.Text.StringBuilder();
        var failed = false;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[4096];
            int n;
            while ((n = await stream.ReadAsync(buffer, timeout.Token)) > 0) received.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, n));
        }
        catch (Exception e) when (e is IOException or HttpRequestException or OperationCanceledException { InnerException: not null } or InvalidOperationException) { failed = true; }
        Assert.True(failed, $"the transfer ended as if it were complete; received: {received}");
        Assert.DoesNotContain(host.Web.Errors, e => e.Contains("secret-backend-address")); // the exception text carries the backend address: it is not logged
        Assert.Contains(host.Web.Errors, e => e.Contains("stream broke")); // but the failure is
    }

    [Fact]
    public async Task A_browser_that_leaves_ends_the_backend_call()
    {
        await using var host = await AdminHost.StartAsync();
        var gate = new TaskCompletionSource();
        var body = new ScriptedBody(ScriptedBody.Bytes(Header + Row("ClientCharge", 18000)), ScriptedBody.Wait(gate));
        host.Web.Booking.Respond = _ => Csv(body);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var response = await DownloadAsync(host, ct: cancel.Token);
        var stream = await response.Content.ReadAsStreamAsync(cancel.Token);
        await stream.ReadAtLeastAsync(new byte[4096], 1, cancellationToken: cancel.Token);
        response.Dispose(); // the browser closes the tab
        stream.Dispose();
        await WaitAsync(() => body.Disposed);
        Assert.True(body.Disposed);
    }

    static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(50);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, "forbidden")]
    [InlineData(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, "unauthorized")]
    [InlineData(HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, "validation_failed")]
    [InlineData(HttpStatusCode.NotFound, HttpStatusCode.NotFound, "not_found")]
    [InlineData(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, "rate_limited")]
    [InlineData(HttpStatusCode.InternalServerError, HttpStatusCode.ServiceUnavailable, "dependency_unavailable")]
    [InlineData(HttpStatusCode.BadGateway, HttpStatusCode.ServiceUnavailable, "dependency_unavailable")]
    public async Task The_backends_refusal_is_mapped_by_status_and_its_own_text_is_never_echoed(HttpStatusCode upstream, HttpStatusCode ours, string code)
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Booking.Respond = _ => FakeBackend.Problem(upstream, "upstream_code");
        var response = await DownloadAsync(host);
        Assert.Equal(ours, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains($"\"code\":\"{code}\"", body);
        Assert.DoesNotContain("SERVER-TITLE", body);
        Assert.DoesNotContain("upstream_code", body);
    }

    [Theory]
    [InlineData("from=2026-12-15&to=2026-12-01")]
    [InlineData("from=2026-02-30&to=2026-03-01")]
    [InlineData("from=2026-01-01&to=2027-12-31")]
    [InlineData("from=x&to=y")]
    [InlineData("")]
    [InlineData("from=2026-12-01&to=2026-12-01%0d%0aSet-Cookie:%20a=b")]
    public async Task A_period_the_service_would_refuse_is_refused_here_and_the_service_is_not_called(string query)
    {
        await using var host = await AdminHost.StartAsync();
        var response = await DownloadAsync(host, query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("validation_failed", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, host.Web.Booking.Calls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task A_circuit_open_or_unreachable_backend_is_the_same_503_without_the_cause()
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Booking.Respond = FakeBackend.Throw(new HttpRequestException("connect ECONNREFUSED secret-backend-address.internal:5432"));
        var response = await DownloadAsync(host);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("secret-backend-address", await response.Content.ReadAsStringAsync());
        Assert.Contains(host.Web.Errors, e => e.StartsWith("ledger.csv upstream refused: status 0")); // the route logs the status only (the kernel's resilience handler logs its own attempts: not this route's text)
        Assert.DoesNotContain(host.Web.Errors.Where(e => e.StartsWith("ledger.csv")), e => e.Contains("secret-backend-address"));
    }
}
