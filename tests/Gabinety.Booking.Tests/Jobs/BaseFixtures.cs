using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Gabinety.Booking.Jobs.Base;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Gabinety.Booking.Tests;

/// <summary>A clock whose timers fire only when the test advances it. A late advance fires a timer once (like a slow wake-up), so the lag is observable.</summary>
sealed class TickClock : TimeProvider
{
    readonly object _gate = new();
    DateTimeOffset _now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    readonly List<FakeTimer> _timers = [];

    public DateTimeOffset Start { get; } = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }
    public int TimerCount { get { lock (_gate) return _timers.Count; } }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var t = new FakeTimer(this, callback, state);
        lock (_gate) _timers.Add(t);
        t.Change(dueTime, period);
        return t;
    }

    public void Advance(TimeSpan by)
    {
        List<FakeTimer> due;
        lock (_gate)
        {
            _now += by;
            due = _timers.Where(t => t.Next is { } n && n <= _now).ToList();
        }
        foreach (var t in due) t.Fire(_now);
    }

    sealed class FakeTimer(TickClock owner, TimerCallback callback, object? state) : ITimer
    {
        TimeSpan _period;
        public DateTimeOffset? Next { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                _period = period;
                Next = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            }
            return true;
        }

        public void Fire(DateTimeOffset now)
        {
            lock (owner._gate)
            {
                if (Next is not { } n) return;
                if (_period > TimeSpan.Zero && _period != Timeout.InfiniteTimeSpan) { while (n <= now) n += _period; Next = n; }
                else Next = null;
            }
            callback(state);
        }

        public void Dispose() { lock (owner._gate) owner._timers.Remove(this); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

/// <summary>A meter factory of its own: a listener scoped to it sees nothing from other tests running in the same process.</summary>
sealed class TestMeterFactory : IMeterFactory
{
    readonly List<Meter> _meters = [];
    public Meter Create(MeterOptions options)
    {
        var m = new Meter(new MeterOptions(options.Name) { Version = options.Version, Tags = options.Tags, Scope = this });
        lock (_meters) _meters.Add(m);
        return m;
    }
    public void Dispose() { lock (_meters) foreach (var m in _meters) m.Dispose(); }
}

/// <summary>Records every measurement of the job instruments published through <paramref name="scope"/>.</summary>
sealed class MetricCapture : IDisposable
{
    public sealed record Point(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags);

    readonly MeterListener _listener = new();
    readonly ConcurrentQueue<Point> _points = new();
    public IReadOnlyList<Point> Points => _points.ToArray();

    public MetricCapture(IMeterFactory scope)
    {
        _listener.InstrumentPublished = (i, l) => { if (i.Meter.Name == JobMetrics.MeterName && ReferenceEquals(i.Meter.Scope, scope)) l.EnableMeasurementEvents(i); };
        _listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Add(i, v, tags));
        _listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Add(i, v, tags));
        _listener.Start();
    }

    void Add(Instrument i, double v, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        _points.Enqueue(new(i.Name, v, tags.ToArray().ToDictionary(t => t.Key, t => t.Value)));

    public List<Point> Of(string instrument) => Points.Where(p => p.Instrument == instrument).ToList();
    public int Runs(string job, string outcome) =>
        Of("gabinety_job_runs_total").Count(p => (string?)p.Tags["job"] == job && (string?)p.Tags["outcome"] == outcome);

    public void Dispose() => _listener.Dispose();
}

/// <summary>A job whose work is a delegate; records when each run started.</summary>
sealed class TestJob(TickClock time, IConfiguration config, JobMetrics metrics, ILogger log, Func<int, CancellationToken, Task>? body = null)
    : PeriodicJob(time, config, metrics, log)
{
    readonly List<DateTimeOffset> _runs = [];
    public override string Name => "test_job";
    public List<DateTimeOffset> Runs { get { lock (_runs) return [.. _runs]; } }

    public override async Task RunOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        int n;
        lock (_runs) { _runs.Add(now); n = _runs.Count; }
        if (body != null) await body(n, ct);
    }
}

static class BaseFx
{
    public static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    public static async Task Eventually(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10); // bounds a wait for the loop's own scheduling; the job's clock is never slept on
        while (!condition()) { if (DateTime.UtcNow > deadline) Assert.Fail("timed out waiting for: " + what); await Task.Delay(5); }
    }
}
