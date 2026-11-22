using System.Globalization;

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
}
