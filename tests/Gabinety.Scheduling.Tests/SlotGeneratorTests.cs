using Gabinety.Scheduling;

namespace Gabinety.Scheduling.Tests;

public class SlotGeneratorTests
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    private static readonly SlotOptions Defaults = new();
    private static readonly DateTimeOffset LongAgo = new(2027, 5, 30, 0, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyList<AvailabilityException> NoEx = [];
    private static readonly IReadOnlyList<BusyInterval> NoBusy = [];

    private static AvailabilityRule Rule(int day, string from, string to) =>
        new(day, TimeOnly.Parse(from), TimeOnly.Parse(to));

    private static string[] Local(IEnumerable<DateTimeOffset> slots) =>
        slots.Select(s => TimeZoneInfo.ConvertTime(s, Warsaw).ToString("HH:mm zzz")).ToArray();

    private static IReadOnlyList<DateTimeOffset> Run(
        IReadOnlyList<AvailabilityRule> rules, DateOnly from, DateOnly to, int minutes = 30,
        IReadOnlyList<AvailabilityException>? ex = null, IReadOnlyList<BusyInterval>? busy = null,
        DateTimeOffset? now = null, SlotOptions? options = null) =>
        SlotGenerator.Generate(rules, ex ?? NoEx, TimeSpan.FromMinutes(minutes), busy ?? NoBusy,
            now ?? LongAgo, from, to, options ?? Defaults, Warsaw);

    [Fact]
    public void Spring_forward_vector_2027_03_28()
    {
        var d = new DateOnly(2027, 3, 28);
        var slots = Run([Rule(7, "01:00", "04:00")], d, d, now: new(2027, 3, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(["01:00 +01:00", "01:30 +01:00", "03:00 +02:00", "03:30 +02:00"], Local(slots));
    }

    [Fact]
    public void Autumn_back_vector_2027_10_31_d30()
    {
        var d = new DateOnly(2027, 10, 31);
        var slots = Run([Rule(7, "02:00", "04:00")], d, d, now: new(2027, 10, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(["02:00 +02:00", "02:30 +02:00", "03:00 +01:00", "03:30 +01:00"], Local(slots));
    }

    [Fact]
    public void Autumn_back_vector_2027_10_31_d60()
    {
        var d = new DateOnly(2027, 10, 31);
        var slots = Run([Rule(7, "02:00", "04:00")], d, d, 60, now: new(2027, 10, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(["02:00 +02:00", "02:30 +02:00", "03:00 +01:00"], Local(slots));
    }

    [Fact]
    public void Lead_time_boundary_keeps_exactly_12h_drops_one_minute_less()
    {
        var d = new DateOnly(2027, 6, 1); // Tuesday, CEST
        var rules = new[] { Rule(2, "10:00", "11:00") };
        var start = new DateTimeOffset(2027, 6, 1, 8, 0, 0, TimeSpan.Zero); // 10:00 local
        Assert.Contains(start, Run(rules, d, d, now: start.AddHours(-12)));
        Assert.DoesNotContain(start, Run(rules, d, d, now: start.AddHours(-12).AddMinutes(1)));
    }

    [Fact]
    public void Horizon_boundary_keeps_exactly_60_days_drops_one_minute_more()
    {
        var d = new DateOnly(2027, 6, 1);
        var rules = new[] { Rule(2, "10:00", "11:00") };
        var start = new DateTimeOffset(2027, 6, 1, 8, 0, 0, TimeSpan.Zero);
        Assert.Contains(start, Run(rules, d, d, now: start.AddDays(-60)));
        Assert.DoesNotContain(start, Run(rules, d, d, now: start.AddDays(-60).AddMinutes(-1)));
    }

    [Fact]
    public void Buffer_blocks_the_next_slot()
    {
        var d = new DateOnly(2027, 6, 1);
        var rules = new[] { Rule(2, "10:00", "12:00") };
        var s10 = new DateTimeOffset(2027, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var busy = new[] { new BusyInterval(s10, s10.AddMinutes(30)) };
        // 10:00 booked; its buffer reaches 10:45, so 10:30 is blocked, 11:00 is free.
        Assert.Equal(["11:00 +02:00", "11:30 +02:00"], Local(Run(rules, d, d, busy: busy)));
    }

    [Fact]
    public void Slot_ending_right_before_busy_start_is_blocked_by_its_own_buffer()
    {
        var d = new DateOnly(2027, 6, 1);
        var rules = new[] { Rule(2, "10:00", "12:00") };
        var b = new DateTimeOffset(2027, 6, 1, 8, 30, 0, TimeSpan.Zero); // 10:30 local
        var busy = new[] { new BusyInterval(b, b.AddMinutes(30)) };
        // slot 10:00 spans [10:00,10:45) and overlaps busy [10:30,11:15)
        Assert.Equal(["11:30 +02:00"], Local(Run(rules, d, d, busy: busy)));
    }

    [Fact]
    public void Exception_date_yields_nothing()
    {
        var d = new DateOnly(2027, 6, 1);
        var slots = Run([Rule(2, "10:00", "12:00")], d, d, ex: [new AvailabilityException(d)]);
        Assert.Empty(slots);
    }

    [Fact]
    public void Duration_60_never_ends_after_rule_end()
    {
        var d = new DateOnly(2027, 6, 1);
        var slots = Run([Rule(2, "10:00", "12:00")], d, d, 60);
        Assert.Equal(["10:00 +02:00", "10:30 +02:00", "11:00 +02:00"], Local(slots));
    }

    [Fact]
    public void Property_slots_inside_rules_and_unique_ordered()
    {
        var rng = new Random(42);
        for (var iter = 0; iter < 200; iter++)
        {
            var rules = new List<AvailabilityRule>();
            for (var day = 1; day <= 7; day++)
            {
                var a = rng.Next(0, 44); // half-hours
                var b = rng.Next(a + 1, 48);
                if (rng.Next(3) > 0) rules.Add(new(day, new TimeOnly(a / 2, a % 2 * 30), new TimeOnly(b / 2, b % 2 * 30)));
            }
            var from = new DateOnly(2027, 3, 20).AddDays(rng.Next(0, 250));
            var to = from.AddDays(rng.Next(0, 14));
            var minutes = rng.Next(2) == 0 ? 30 : 60;
            var slots = Run(rules, from, to, minutes, now: new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), options: new SlotOptions(MinLeadHours: 0, HorizonDays: 3650));

            for (var i = 1; i < slots.Count; i++) Assert.True(slots[i - 1] < slots[i]);
            foreach (var s in slots)
            {
                Assert.Equal(TimeSpan.Zero, s.Offset);
                var local = TimeZoneInfo.ConvertTime(s, Warsaw);
                var day = (int)local.DayOfWeek == 0 ? 7 : (int)local.DayOfWeek;
                var t = TimeOnly.FromDateTime(local.DateTime);
                Assert.Contains(rules, r => r.IsoWeekday == day && t >= r.StartLocal
                    && t.AddMinutes(minutes) <= r.EndLocal && t.AddMinutes(minutes) > t);
            }
            if (minutes == 30)
                for (var i = 1; i < slots.Count; i++) Assert.True(slots[i - 1].AddMinutes(30) <= slots[i]);
        }
    }
}
