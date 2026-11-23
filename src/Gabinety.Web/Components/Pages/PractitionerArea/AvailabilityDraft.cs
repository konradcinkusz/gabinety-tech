using System.Globalization;
using Gabinety.Web.Api;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.PractitionerArea;

public enum AvailabilityIssueCode { TooManyRules, TooManyExceptions, NotHalfHour, EndBeforeStart, Overlap, DuplicateException, BadWeekday, BadDate }

/// <summary><c>Row</c> is the 0-based rule the issue is about, <c>Weekday</c> the ISO weekday of an overlap.</summary>
public sealed record AvailabilityIssue(AvailabilityIssueCode Code, int? Row = null, int? Weekday = null)
{
    public string Message
    {
        get
        {
            var text = Code switch
            {
                AvailabilityIssueCode.TooManyRules => PlPractitioner.Availability.TooManyRules,
                AvailabilityIssueCode.TooManyExceptions => PlPractitioner.Availability.TooManyExceptions,
                AvailabilityIssueCode.NotHalfHour => PlPractitioner.Availability.NotHalfHour,
                AvailabilityIssueCode.EndBeforeStart => PlPractitioner.Availability.EndBeforeStart,
                AvailabilityIssueCode.Overlap => string.Format(CultureInfo.InvariantCulture, PlPractitioner.Availability.Overlap, Weekday is >= 1 and <= 7 ? PlPractitioner.Availability.Days[Weekday.Value - 1] : ""),
                AvailabilityIssueCode.DuplicateException => PlPractitioner.Availability.DuplicateException,
                AvailabilityIssueCode.BadWeekday => PlPractitioner.Availability.BadWeekday,
                _ => PlPractitioner.Availability.BadDate,
            };
            return Row is { } r ? string.Format(CultureInfo.InvariantCulture, PlPractitioner.Availability.RowIssue, r + 1, text) : text;
        }
    }
}

/// <summary>
/// The practitioner's weekly windows and blocked days as an editable draft (CONTRACTS 4.2). A static page has no component state between requests, so the whole draft travels in the form
/// (<c>rule-0-day</c>, <c>rule-0-from</c>, <c>rule-0-to</c>, <c>exception-0</c>, ...) and is rebuilt here; the commands (add, remove) change the draft and show it again, and only "save" calls the service.
/// It mirrors the service's validation so the form refuses what the service would; the service stays the authority.
/// </summary>
public sealed class AvailabilityDraft
{
    public const int MaxRules = 28, MaxExceptions = 90;
    public static readonly string[] Times = [.. Enumerable.Range(0, 48).Select(i => $"{i / 2:00}:{(i % 2 == 0 ? "00" : "30")}")];

    public List<AvailabilityRuleDto> Rules { get; } = [];
    public List<string> Exceptions { get; } = [];

    public static AvailabilityDraft From(AvailabilityDto dto)
    {
        var d = new AvailabilityDraft();
        d.Rules.AddRange(dto.Rules.OrderBy(r => r.IsoWeekday).ThenBy(r => r.StartLocal, StringComparer.Ordinal));
        d.Exceptions.AddRange(dto.Exceptions.Select(e => e.DateLocal).OrderBy(x => x, StringComparer.Ordinal));
        return d;
    }

    /// <summary>Rebuilds the draft from the posted form. Unknown or missing values become what validation then reports; at most <c>MaxRules + 1</c> rows are read, so a hostile post cannot make the loop long.</summary>
    public static AvailabilityDraft FromForm(Func<string, string?> value)
    {
        var d = new AvailabilityDraft();
        for (var i = 0; i <= MaxRules && value($"rule-{i}-day") is { } day; i++)
            d.Rules.Add(new AvailabilityRuleDto(int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out var w) ? w : 0, value($"rule-{i}-from") ?? "", value($"rule-{i}-to") ?? ""));
        for (var i = 0; i <= MaxExceptions && value($"exception-{i}") is { } date; i++) d.Exceptions.Add(date);
        return d;
    }

    public void AddRule(int isoWeekday = 1) { if (Rules.Count <= MaxRules) Rules.Add(new AvailabilityRuleDto(isoWeekday is >= 1 and <= 7 ? isoWeekday : 1, "09:00", "10:00")); }

    /// <summary>
    /// Makes the other working days (Monday to Friday) have exactly the intervals of <paramref name="isoWeekday"/>: their own intervals are replaced, weekends are not touched. Only Monday to Friday is a source;
    /// a source without intervals does nothing (it would silently clear the week). Copies stop at the same row limit as <see cref="AddRule"/>, so validation still reports too many.
    /// </summary>
    public void CopyToWeekdays(int isoWeekday)
    {
        if (isoWeekday is < 1 or > 5) return;
        var source = Rules.Where(r => r.IsoWeekday == isoWeekday).ToList();
        if (source.Count == 0) return;
        Rules.RemoveAll(r => r.IsoWeekday is >= 1 and <= 5 && r.IsoWeekday != isoWeekday);
        for (var day = 1; day <= 5; day++)
        {
            if (day == isoWeekday) continue;
            foreach (var r in source) { if (Rules.Count <= MaxRules) Rules.Add(r with { IsoWeekday = day }); }
        }
    }

    /// <summary><c>środa, 24 grudnia 2026</c> for a stored <c>yyyy-MM-dd</c>; anything that is not a real date is returned as it is (validation reports it).</summary>
    public static string LongDate(string iso) => RealDate(iso) ? $"{Gabinety.Web.Components.Pages.Shared.PlFormat.DayHeading(DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture))} {iso[..4]}" : iso;

    public void RemoveRule(int index) { if (index >= 0 && index < Rules.Count) Rules.RemoveAt(index); }

    /// <summary>Adds a blocked day; an empty value is ignored, an invalid or repeated one is kept so that validation says so.</summary>
    public void AddException(string? date) { if (!string.IsNullOrWhiteSpace(date)) Exceptions.Add(date.Trim()); }

    public void RemoveException(int index) { if (index >= 0 && index < Exceptions.Count) Exceptions.RemoveAt(index); }

    public AvailabilityDto ToDto() => new([.. Rules], [.. Exceptions.Select(e => new AvailabilityExceptionDto(e))]);

    static bool TryTime(string s, out int minutes)
    {
        minutes = 0;
        if (s.Length != 5 || s[2] != ':' || !int.TryParse(s.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var h) || !int.TryParse(s.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m)) return false;
        if (h > 23 || m is not (0 or 30)) return false;
        minutes = h * 60 + m;
        return true;
    }

    static bool RealDate(string s) => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public IReadOnlyList<AvailabilityIssue> Validate()
    {
        var issues = new List<AvailabilityIssue>();
        if (Rules.Count > MaxRules) issues.Add(new(AvailabilityIssueCode.TooManyRules));
        if (Exceptions.Count > MaxExceptions) issues.Add(new(AvailabilityIssueCode.TooManyExceptions));
        var usable = new List<(int Day, int Start, int End)>();
        for (var i = 0; i < Rules.Count; i++)
        {
            var r = Rules[i];
            var ok = true;
            if (r.IsoWeekday is < 1 or > 7) { issues.Add(new(AvailabilityIssueCode.BadWeekday, i)); ok = false; }
            if (!TryTime(r.StartLocal, out var start) || !TryTime(r.EndLocal, out var end)) { issues.Add(new(AvailabilityIssueCode.NotHalfHour, i)); ok = false; }
            else if (end <= start) { issues.Add(new(AvailabilityIssueCode.EndBeforeStart, i)); ok = false; }
            else if (ok) usable.Add((r.IsoWeekday, start, end));
        }
        foreach (var day in usable.GroupBy(u => u.Day).OrderBy(g => g.Key))
        {
            var sorted = day.OrderBy(u => u.Start).ToList();
            if (sorted.Where((u, i) => i > 0 && u.Start < sorted[i - 1].End).Any()) issues.Add(new(AvailabilityIssueCode.Overlap, null, day.Key));
        }
        if (Exceptions.Any(e => !RealDate(e))) issues.Add(new(AvailabilityIssueCode.BadDate));
        else if (Exceptions.Distinct(StringComparer.Ordinal).Count() != Exceptions.Count) issues.Add(new(AvailabilityIssueCode.DuplicateException));
        return issues;
    }

    /// <summary>
    /// What the service complained about, as issues: the NAMES of the fields only (<c>rules[2].endLocal</c>, <c>rules</c>, <c>exceptions</c>), because a server message is never shown. A row field becomes one row issue
    /// ("check the day and the hours"); <c>rules</c> alone is an overlap or too many rules, <c>exceptions</c> too many or a repeat.
    /// </summary>
    public static IReadOnlyList<string> ServerRows(IEnumerable<string> fields) =>
        [.. fields.Select(f => f.StartsWith("rules[", StringComparison.Ordinal) && int.TryParse(f.AsSpan(6, Math.Max(0, f.IndexOf(']') - 6)), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? string.Format(CultureInfo.InvariantCulture, PlPractitioner.Availability.RowIssue, n + 1, PlPractitioner.Availability.ServerRow) : null).OfType<string>().Distinct()];
}
