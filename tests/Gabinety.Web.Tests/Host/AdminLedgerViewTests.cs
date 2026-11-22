using System.Net;
using System.Net.Http.Headers;

namespace Gabinety.Web.Tests.AdminPages;

/// <summary>/admin/ksiega as the administrator sees it (UX-051): the booking reference, the per-kind totals, the Polish period, the table's structure.</summary>
public class AdminLedgerViewTests
{
    const string Header = "booking_id,kind,amount_minor,currency,created_at\n";
    const string Period = "from=2026-12-01&to=2026-12-15";

    // n gives the random tail: 0b9e1d2c-0000-0000-0000-00000000000n, so the last eight characters are 0000000n
    static string Id(int n) => $"0b9e1d2c-0000-0000-0000-{n:D12}";
    static string Row(string kind, long amount, int n) => $"{Id(n)},{kind},{amount},PLN,2026-12-0{n}T10:00:00.1234567Z\n";

    static HttpResponseMessage CsvOf(string text)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, System.Text.Encoding.UTF8, "text/csv") };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/csv") { CharSet = "utf-8" };
        return response;
    }

    static string Money(string text) => text.Replace(' ', ' ');

    [Fact]
    public async Task The_booking_column_shows_the_random_tail_with_the_whole_id_in_a_title_and_no_link()
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Booking.Respond = _ => CsvOf(Header + Row("ClientCharge", 18000, 1) + Row("Refund", 10000, 2));
        var page = await host.Browser.GetAsync($"/admin/ksiega?{Period}");

        var refs = page.All("tbody td[data-label=Rezerwacja] span.ledger-ref");
        Assert.Equal(["00000001", "00000002"], refs.Select(r => r.TextContent));
        Assert.Equal([Id(1), Id(2)], refs.Select(r => r.GetAttribute("title")));
        Assert.Empty(page.All("table a")); // no admin-readable booking page exists: the reference is not a link
    }

    [Fact]
    public async Task Two_bookings_made_in_the_same_moment_still_read_differently()
    {
        await using var host = await AdminHost.StartAsync();
        // the same timestamp prefix (0b9e1d2c) for every row: the old column read the same eight characters on each row
        host.Web.Booking.Respond = _ => CsvOf(Header + Row("ClientCharge", 18000, 1) + Row("ClientCharge", 18000, 9));
        var page = await host.Browser.GetAsync($"/admin/ksiega?{Period}");
        var texts = page.All("tbody td[data-label=Rezerwacja] span.ledger-ref").Select(r => r.TextContent).ToList();
        Assert.Equal(2, texts.Distinct().Count());
        Assert.DoesNotContain("0b9e1d2c", string.Concat(texts));
    }

    [Fact]
    public async Task The_totals_per_kind_are_summed_in_grosz_and_shown_above_the_table()
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Booking.Respond = _ => CsvOf(Header + Row("ClientCharge", 18000, 1) + Row("ClientCharge", 9000, 2) + Row("Refund", 10000, 3));
        var page = await host.Browser.GetAsync($"/admin/ksiega?{Period}");

        var totals = page.One(".ledger-totals")!;
        Assert.Equal("Sumy po rodzajach", totals.QuerySelector("h2")!.TextContent);
        Assert.Equal(["Płatność klienta", "Zwrot dla klienta"], totals.QuerySelectorAll("dt").Select(t => t.TextContent));
        Assert.Equal(["270,00 zł", "100,00 zł"], totals.QuerySelectorAll("dd").Select(d => Money(d.TextContent)));
        Assert.Equal("Wpisów w okresie: 3.", page.One(".muted")!.TextContent); // the count line stays the first muted line
    }

    [Fact]
    public async Task A_preview_that_is_cut_short_shows_no_totals_because_they_would_be_for_part_of_the_period()
    {
        await using var host = await AdminHost.StartAsync();
        var rows = string.Concat(Enumerable.Range(1, 60).Select(i => Row("ClientCharge", 100, (i % 9) + 1)));
        host.Web.Booking.Respond = _ => CsvOf(Header + rows);
        var page = await host.Browser.GetAsync($"/admin/ksiega?{Period}");

        Assert.Equal(50, page.All("tbody tr").Count);
        Assert.Null(page.One(".ledger-totals"));
        Assert.Contains(page.All(".muted"), p => p.TextContent.Contains("nie więcej niż 50 wpisów"));
    }

    [Fact]
    public async Task The_period_line_is_in_Polish_and_the_table_is_a_labelled_scroller_with_the_table_classes()
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Booking.Respond = _ => CsvOf(Header);
        var page = await host.Browser.GetAsync($"/admin/ksiega?{Period}");

        Assert.Equal("Wybrany okres: 1–15 grudnia 2026.", page.All("main p").Single(p => p.TextContent.StartsWith("Wybrany okres")).TextContent);
        Assert.Equal("Brak wpisów w tym okresie.", page.TextOf(".empty-state"));
    }

    [Fact]
    public async Task The_table_names_its_columns_and_scrolls_inside_its_region()
    {
        await using var host = await AdminHost.StartAsync();
        host.Web.Booking.Respond = _ => CsvOf(Header + Row("ClientCharge", 18000, 1));
        var page = await host.Browser.GetAsync($"/admin/ksiega?{Period}");

        var wrap = page.One(".table-wrap.ledger-table")!;
        Assert.Equal("region", wrap.GetAttribute("role"));
        Assert.Equal("0", wrap.GetAttribute("tabindex"));
        Assert.Equal(["table", "table--cards"], page.One("table")!.ClassName!.Split(' ').Where(c => c.StartsWith("table")).Order());
        Assert.All(page.All("tbody td"), td => Assert.True(td.HasAttribute("data-label")));
    }
}
