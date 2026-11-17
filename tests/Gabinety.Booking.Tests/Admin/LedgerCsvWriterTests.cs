using Gabinety.Booking.Features.Admin.Ledger;

namespace Gabinety.Booking.Tests;

/// <summary>The CSV writer of the ledger export: quoting, escaping and formula-injection neutralisation (OWASP "CSV injection").</summary>
public class LedgerCsvWriterTests
{
    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+SUM(A1)", "'+SUM(A1)")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("\tx", "'\tx")]
    public void A_leading_formula_character_is_neutralised_with_an_apostrophe(string input, string expected) =>
        Assert.Equal(expected.Contains('\t') ? "\"" + expected + "\"" : expected, CsvWriter.Escape(input));

    [Theory]
    [InlineData("PLN")]
    [InlineData("ClientCharge")]
    [InlineData("2026-12-01T09:30:00Z")]
    [InlineData("5000")]
    [InlineData("a=b")]
    [InlineData("")]
    public void A_controlled_value_is_written_unchanged(string input) => Assert.Equal(input, CsvWriter.Escape(input));

    [Theory]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("cr\rhere", "\"cr\rhere\"")]
    public void A_separator_quote_or_line_break_is_quoted_and_quotes_are_doubled(string input, string expected) =>
        Assert.Equal(expected, CsvWriter.Escape(input));

    [Fact]
    public void A_neutralised_field_that_also_needs_quoting_is_both()
    {
        Assert.Equal("\"'=HYPERLINK(\"\"x\"\",\"\"y\"\")\"", CsvWriter.Escape("=HYPERLINK(\"x\",\"y\")"));
    }

    [Fact]
    public async Task A_row_is_comma_separated_and_ends_with_CRLF()
    {
        var sw = new StringWriter();
        var w = new CsvWriter(sw);
        await w.WriteRowAsync("a", "=b", "c,d");
        await w.WriteRowAsync("e");
        Assert.Equal("a,'=b,\"c,d\"\r\ne\r\n", sw.ToString());
    }
}
