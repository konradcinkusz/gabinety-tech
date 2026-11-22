using System.Globalization;
using Gabinety.Web.Api;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Admin;

public enum PeriodProblem { None, Format, Order, Range }

public sealed record LedgerRange(string From, string To, PeriodProblem Problem);

public sealed record LedgerPreset(string Id, string From, string To);

/// <summary>
/// The period of the ledger page and its export (CONTRACTS 4.3: <c>from</c> and <c>to</c> as <c>yyyy-MM-dd</c>, UTC days, both ends inclusive, at most 366 days counting both). Time comes from the caller's
/// <see cref="TimeProvider"/> (SPEC 5 rule 4); the same rules guard the page and the download route.
/// </summary>
public static class LedgerPeriod
{
    public const int MaxDays = 366;
    public const string ThisMonth = "thisMonth", LastMonth = "lastMonth", ThisYear = "thisYear";

    public static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The day number of a real <c>yyyy-MM-dd</c> date, else null (also for 2026-02-30).</summary>
    public static DateOnly? Parse(string? day) => DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static PeriodProblem Check(string? from, string? to)
    {
        if (Parse(from) is not { } a || Parse(to) is not { } b) return PeriodProblem.Format;
        if (b < a) return PeriodProblem.Order;
        return b.DayNumber - a.DayNumber + 1 > MaxDays ? PeriodProblem.Range : PeriodProblem.None;
    }

    /// <summary>The query's period when both ends are given; otherwise the current month so far (UTC).</summary>
    public static LedgerRange Of(string? from, string? to, DateTimeOffset now)
    {
        if (from is null && to is null) { var month = Presets(now)[0]; return new LedgerRange(month.From, month.To, PeriodProblem.None); }
        return new LedgerRange(from ?? "", to ?? "", Check(from, to));
    }

    public static IReadOnlyList<LedgerPreset> Presets(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);
        return
        [
            new(ThisMonth, Day(firstOfMonth), Day(today)),
            new(LastMonth, Day(firstOfMonth.AddMonths(-1)), Day(firstOfMonth.AddDays(-1))),
            new(ThisYear, Day(new DateOnly(today.Year, 1, 1)), Day(today)),
        ];
    }

    /// <summary>The name of the file the browser saves. Built here from the validated days, never copied from the backend's header.</summary>
    public static string FileName(string from, string to) => $"ledger-{from}-{to}.csv";

    /// <summary>
    /// The period in Polish, genitive months (CONTRACTS 4.3 days in, words out): <c>1–22 listopada 2026</c>, <c>1 października – 22 listopada 2026</c>,
    /// <c>31 grudnia 2026 – 2 stycznia 2027</c>. Empty when either day is not a real date (the page shows the problem instead).
    /// </summary>
    public static string Polish(string from, string to)
    {
        if (Parse(from) is not { } a || Parse(to) is not { } b) return "";
        var (monthA, monthB) = (Pl.Format.MonthsGenitive[a.Month - 1], Pl.Format.MonthsGenitive[b.Month - 1]);
        if (a == b) return $"{a.Day} {monthA} {a.Year}";
        if (a.Year == b.Year && a.Month == b.Month) return $"{a.Day}–{b.Day} {monthB} {b.Year}";
        if (a.Year == b.Year) return $"{a.Day} {monthA} – {b.Day} {monthB} {b.Year}";
        return $"{a.Day} {monthA} {a.Year} – {b.Day} {monthB} {b.Year}";
    }
}

/// <summary>One line of the ledger's summary: the amount of every row of one kind in the preview, and how many rows it sums.</summary>
public sealed record KindTotal(string Kind, long AmountMinor, int Count);

/// <summary>
/// Per-kind totals of ledger rows. Amounts are always positive and the kind gives the direction (SPEC: <c>LedgerEntry</c>), so the sums are per kind and there is no
/// grand total: a net figure would need a sign convention the ledger does not state. Integer grosz arithmetic (SPEC 5 rule 3).
/// </summary>
public static class LedgerTotals
{
    /// <summary>The kinds in ledger order. A row of any other kind is listed in the table but not summed: its direction is unknown.</summary>
    public static readonly IReadOnlyList<string> KnownKinds = ["ClientCharge", "Refund", "PlatformFee", "PractitionerPayout", "PayoutReversal"];

    public static IReadOnlyList<KindTotal> Of(IEnumerable<LedgerRowDto> rows)
    {
        var sums = new Dictionary<string, (long Amount, int Count)>();
        foreach (var row in rows)
        {
            if (!KnownKinds.Contains(row.Kind)) continue;
            var sum = sums.GetValueOrDefault(row.Kind);
            sums[row.Kind] = (sum.Amount + row.AmountMinor, sum.Count + 1);
        }
        return [.. KnownKinds.Where(sums.ContainsKey).Select(kind => new KindTotal(kind, sums[kind].Amount, sums[kind].Count))];
    }
}
