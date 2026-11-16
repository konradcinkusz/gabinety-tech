namespace Gabinety.Scheduling;

/// <summary>Pure slot generation, CONTRACTS 9.4. No clock, no I/O: <c>now</c> is an argument.</summary>
public static class SlotGenerator
{
    /// <returns>UTC slot starts, ordered and unique.</returns>
    public static IReadOnlyList<DateTimeOffset> Generate(
        IReadOnlyList<AvailabilityRule> rules,
        IReadOnlyList<AvailabilityException> exceptions,
        TimeSpan duration,
        IReadOnlyList<BusyInterval> busy,
        DateTimeOffset now,
        DateOnly fromDate,
        DateOnly toDate,
        SlotOptions options,
        TimeZoneInfo zone)
    {
        var blocked = exceptions.Select(e => e.DateLocal).ToHashSet();
        var earliest = now.UtcDateTime.AddHours(options.MinLeadHours);
        var latest = now.UtcDateTime.AddDays(options.HorizonDays);
        var buffer = TimeSpan.FromMinutes(options.BufferMinutes);
        var step = TimeSpan.FromMinutes(options.StepMinutes);
        var busyUtc = busy.Select(b => (S: b.Start.UtcDateTime, E: b.End.UtcDateTime + buffer)).ToList();
        var result = new SortedSet<DateTime>();

        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            if (blocked.Contains(date)) continue;
            var iso = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
            foreach (var rule in rules.Where(r => r.IsoWeekday == iso))
            {
                var end = rule.EndLocal.ToTimeSpan();
                for (var t = rule.StartLocal.ToTimeSpan(); t + duration <= end; t += step)
                {
                    var local = date.ToDateTime(TimeOnly.MinValue).Add(t); // Kind: Unspecified
                    if (zone.IsInvalidTime(local)) continue; // spring forward gap
                    var offset = zone.IsAmbiguousTime(local)
                        ? zone.GetAmbiguousTimeOffsets(local).Max() // first occurrence = larger offset
                        : zone.GetUtcOffset(local);
                    var start = DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
                    if (start < earliest || start > latest) continue;
                    var slotEnd = start + duration + buffer;
                    if (busyUtc.Any(b => start < b.E && b.S < slotEnd)) continue;
                    result.Add(start);
                }
            }
        }
        return result.Select(s => new DateTimeOffset(s, TimeSpan.Zero)).ToList();
    }
}
