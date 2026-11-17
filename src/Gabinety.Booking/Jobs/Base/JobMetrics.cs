using System.Diagnostics.Metrics;

namespace Gabinety.Booking.Jobs.Base;

/// <summary>Values of the <c>outcome</c> label of <c>gabinety_job_runs_total</c>. SPEC 8 fixes the names and labels, not the values; these two are the whole set.</summary>
public static class JobOutcome
{
    public const string Ok = "ok", Error = "error";
}

/// <summary>
/// The job metrics of SPEC 8, a closed list: <c>gabinety_job_runs_total{job,outcome}</c> and <c>gabinety_job_lag_seconds{job}</c>.
/// <c>job</c> is the constant <see cref="PeriodicJob.Name"/> of a job class, never an id or user data. The meter comes from <see cref="IMeterFactory"/>,
/// so a test can listen to its own host's meter only.
/// </summary>
public sealed class JobMetrics
{
    public const string MeterName = "Gabinety.Booking.Jobs";
    readonly Counter<long> _runs;
    readonly Histogram<double> _lag;

    public JobMetrics(IMeterFactory factory)
    {
        var meter = factory.Create(MeterName);
        _runs = meter.CreateCounter<long>("gabinety_job_runs_total", description: "Finished job runs by job and outcome (ok, error).");
        _lag = meter.CreateHistogram<double>("gabinety_job_lag_seconds", description: "Seconds between when a job run was due and when it started.");
    }

    public void RunFinished(string job, string outcome) =>
        _runs.Add(1, new KeyValuePair<string, object?>("job", job), new KeyValuePair<string, object?>("outcome", outcome));

    public void Lag(string job, double seconds) => _lag.Record(seconds, new KeyValuePair<string, object?>("job", job));
}
