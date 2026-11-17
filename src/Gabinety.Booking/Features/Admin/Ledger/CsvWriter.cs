namespace Gabinety.Booking.Features.Admin.Ledger;

/// <summary>
/// A minimal RFC 4180 writer (CRLF rows, comma separator). Every field goes through <see cref="Escape"/>: a field that starts with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>,
/// TAB or CR is prefixed with an apostrophe so a spreadsheet shows it as text instead of evaluating it (formula injection), and a field with a comma, a quote, a line
/// break or a TAB is quoted with inner quotes doubled. The ledger export writes only controlled values today; this keeps a future field from becoming a vector.
/// </summary>
public sealed class CsvWriter(TextWriter output)
{
    public static string Escape(string value)
    {
        if (value.Length == 0) return value;
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.AsSpan().IndexOfAny(",\"\r\n\t") < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public async Task WriteRowAsync(params string[] fields)
    {
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0) await output.WriteAsync(',');
            await output.WriteAsync(Escape(fields[i]));
        }
        await output.WriteAsync("\r\n");
    }
}
