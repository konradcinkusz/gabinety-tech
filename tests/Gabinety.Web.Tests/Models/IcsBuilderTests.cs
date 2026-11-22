using System.Text;
using Gabinety.Web.Hosting;

namespace Gabinety.Web.Tests.Models;

/// <summary>The calendar file of a booking (RFC 5545) as a pure function: the escaping, the folding, the CRLF line ends and the UTC times.</summary>
public class IcsBuilderTests
{
    static readonly Guid Id = Guid.Parse("018f0000-0000-7000-8000-0000000000b1");
    static readonly DateTimeOffset Stamp = new(2026, 11, 22, 9, 30, 15, TimeSpan.Zero);
    // 12:00 in Warsaw in December is 11:00 UTC: the file must say the UTC time, with the Z.
    static readonly DateTimeOffset Start = new(2026, 12, 8, 12, 0, 0, TimeSpan.FromHours(1));

    static IcsEvent Event(string summary = "Audyt kodu demo", string? description = "Programista: Anna Demowska") =>
        new(Id, Stamp, Start, Start.AddHours(1), summary, description);

    static string[] Physical(string folded) => folded.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    // ---- text

    [Fact]
    public void Escape_protects_the_separators_and_newlines_and_drops_carriage_returns()
    {
        Assert.Equal(@"a\,b\;c\\d\ne", IcsBuilder.Escape("a,b;c\\d\r\ne"));
    }

    [Fact]
    public void Other_control_characters_become_a_space_so_a_line_cannot_break_out()
    {
        Assert.Equal("a b", IcsBuilder.Escape("a\tb"));
        Assert.DoesNotMatch(@"[\x00-\x08\x0B-\x1F]", IcsBuilder.Escape("a\u0001b\u001Fc"));
    }

    [Fact]
    public void A_summary_with_separators_is_escaped_in_the_file()
    {
        var s = IcsBuilder.Event(Event(summary: "Audyt, kod; demo"));
        Assert.Contains("SUMMARY:Audyt\\, kod\\; demo\r\n", s);
    }

    [Fact]
    public void Without_a_description_there_is_no_description_line()
    {
        Assert.DoesNotContain("DESCRIPTION", IcsBuilder.Event(Event(description: null)));
        Assert.DoesNotContain("DESCRIPTION", IcsBuilder.Event(Event(description: "   ")));
    }

    // ---- lines

    [Fact]
    public void Every_line_ends_with_crlf_and_no_bare_line_feed_is_written()
    {
        var s = IcsBuilder.Event(Event());
        Assert.DoesNotMatch(@"(?<!\r)\n", s);
        Assert.StartsWith("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n", s);
        Assert.EndsWith("END:VEVENT\r\nEND:VCALENDAR\r\n", s);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(s, "BEGIN:VEVENT"));
    }

    [Fact]
    public void A_short_line_is_not_folded()
    {
        Assert.Equal("UID:x\r\n", IcsBuilder.Fold("UID:x"));
    }

    [Fact]
    public void A_long_line_is_folded_at_75_octets_and_unfolds_to_the_original()
    {
        var value = string.Concat(Enumerable.Repeat("abcdefghij", 20)); // 200 octets
        var line = "DESCRIPTION:" + value;
        var physical = Physical(IcsBuilder.Fold(line));
        Assert.Equal(75, Encoding.UTF8.GetByteCount(physical[0]));
        Assert.All(physical, p => Assert.True(Encoding.UTF8.GetByteCount(p) <= 75, p));
        Assert.All(physical.Skip(1), p => Assert.StartsWith(" ", p)); // a continuation starts with one space
        var unfolded = string.Concat(physical.Select((p, i) => i == 0 ? p : p[1..]));
        Assert.Equal(line, unfolded);
    }

    [Fact]
    public void Folding_never_splits_a_multi_byte_character()
    {
        // Polish letters are two octets, an emoji (a surrogate pair) four: a fold must fall between characters.
        var line = "SUMMARY:" + string.Concat(Enumerable.Repeat("ą", 40)) + string.Concat(Enumerable.Repeat("😀", 20));
        var physical = Physical(IcsBuilder.Fold(line));
        var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
        foreach (var p in physical)
        {
            Assert.True(Encoding.UTF8.GetByteCount(p) <= 75);
            _ = strict.GetString(Encoding.UTF8.GetBytes(p)); // throws if a sequence was cut
        }
        Assert.Equal(line, string.Concat(physical.Select((p, i) => i == 0 ? p : p[1..])));
    }

    // ---- times and identity

    [Fact]
    public void The_times_are_utc_in_the_basic_form_with_the_z()
    {
        var s = IcsBuilder.Event(Event());
        Assert.Contains("DTSTART:20261208T110000Z\r\n", s);
        Assert.Contains("DTEND:20261208T120000Z\r\n", s);
        Assert.Contains("DTSTAMP:20261122T093015Z\r\n", s);
        Assert.Equal("20261208T110000Z", IcsBuilder.Utc(Start));
    }

    [Fact]
    public void The_uid_is_the_booking_id_at_gabinety_tech()
    {
        Assert.Contains($"UID:{Id:D}@gabinety.tech\r\n", IcsBuilder.Event(Event()));
    }
}
