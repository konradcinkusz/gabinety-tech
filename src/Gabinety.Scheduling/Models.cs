namespace Gabinety.Scheduling;

/// <summary>Weekly availability window (CONTRACTS 4.2). IsoWeekday: 1 Monday to 7 Sunday.</summary>
public sealed record AvailabilityRule(int IsoWeekday, TimeOnly StartLocal, TimeOnly EndLocal);

/// <summary>A blocked local day.</summary>
public sealed record AvailabilityException(DateOnly DateLocal);

/// <summary>An active booking [Start, End) in UTC; the generator adds the buffer after End.</summary>
public sealed record BusyInterval(DateTimeOffset Start, DateTimeOffset End);

/// <summary>CONTRACTS 9.4 / section 9 constants; the defaults are the owner's.</summary>
public sealed record SlotOptions(
    int MinLeadHours = 12,
    int HorizonDays = 60,
    int BufferMinutes = 15,
    int StepMinutes = 30);
