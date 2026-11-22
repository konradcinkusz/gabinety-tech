using System.Globalization;
using System.Text;

namespace Gabinety.Web.Hosting;

/// <summary>What one calendar event says: the booking it is for, the moments in UTC, the title and the (optional) description text.</summary>
public sealed record IcsEvent(Guid BookingId, DateTimeOffset StampUtc, DateTimeOffset StartUtc, DateTimeOffset EndUtc, string Summary, string? Description);

/// <summary>
/// The iCalendar file of a booking (RFC 5545): one VCALENDAR with one VEVENT. A pure function of its input, so the escaping, the folding, the CRLF line ends and the UTC times are unit-tested
/// without a host. Lines end with CRLF, a text value escapes <c>\</c> <c>;</c> <c>,</c> and newlines, and a content line longer than 75 octets is folded (a CRLF and one space, never inside a UTF-8 sequence).
/// </summary>
public static class IcsBuilder
{
    public const string Crlf = "\r\n";
    /// <summary>RFC 5545 3.1: a content line is at most 75 octets before it is folded.</summary>
    public const int MaxOctets = 75;
    const string ProdId = "-//Gabinety Tech//Rezerwacje//PL";

    public static string Event(IcsEvent e)
    {
        var sb = new StringBuilder();
        Line(sb, "BEGIN:VCALENDAR");
        Line(sb, "VERSION:2.0");
        Line(sb, "PRODID:" + ProdId);
        Line(sb, "CALSCALE:GREGORIAN");
        Line(sb, "BEGIN:VEVENT");
        Line(sb, "UID:" + e.BookingId.ToString("D", CultureInfo.InvariantCulture) + "@gabinety.tech");
        Line(sb, "DTSTAMP:" + Utc(e.StampUtc));
        Line(sb, "DTSTART:" + Utc(e.StartUtc));
        Line(sb, "DTEND:" + Utc(e.EndUtc));
        Line(sb, "SUMMARY:" + Escape(e.Summary));
        if (!string.IsNullOrWhiteSpace(e.Description)) Line(sb, "DESCRIPTION:" + Escape(e.Description));
        Line(sb, "END:VEVENT");
        Line(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    /// <summary>A UTC instant as <c>yyyyMMdd'T'HHmmss'Z'</c> (the form with the Z: an absolute time, no zone parameter needed).</summary>
    public static string Utc(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>RFC 5545 3.3.11: backslash, semicolon and comma are escaped, a newline becomes <c>\n</c>, a carriage return is dropped, other control characters become a space.</summary>
    public static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case ';': sb.Append("\\;"); break;
                case ',': sb.Append("\\,"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': break;
                default: sb.Append(c < ' ' ? ' ' : c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Appends one content line, folded to 75 octets, with its CRLF.</summary>
    static void Line(StringBuilder sb, string line) => sb.Append(Fold(line));

    /// <summary>
    /// Folds a content line: the first physical line holds 75 octets, each continuation is a CRLF, a space and 74 more octets. A surrogate pair is never split, so every physical line is valid UTF-8.
    /// </summary>
    public static string Fold(string line)
    {
        var sb = new StringBuilder(line.Length + 16);
        var limit = MaxOctets;
        var used = 0;
        for (var i = 0; i < line.Length;)
        {
            var length = char.IsHighSurrogate(line[i]) && i + 1 < line.Length && char.IsLowSurrogate(line[i + 1]) ? 2 : 1;
            var piece = line.Substring(i, length);
            var size = Encoding.UTF8.GetByteCount(piece);
            if (used + size > limit)
            {
                sb.Append(Crlf).Append(' ');
                used = 0;
                limit = MaxOctets - 1; // the leading space is an octet of its own
            }
            sb.Append(piece);
            used += size;
            i += length;
        }
        return sb.Append(Crlf).ToString();
    }
}
