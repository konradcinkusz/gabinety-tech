namespace Gabinety.DemoSeeder;

/// <summary>
/// The Booking data (T-08.01b): weekday availability for each Approved demo practitioner and twelve bookings in the states the journeys need, all through the
/// public and user APIs of Booking (create, simulated payment, cancel, dispute, no-show report, admin resolve) and the internal test clock for the states that need time to pass.
/// Idempotent: a booking is found again by the demo key in its brief and only the steps it has not yet passed are taken. The clock is read at the start, moved forward
/// only when a late action is still due, and put back to exactly that value in a <c>finally</c>.
/// </summary>
public sealed class BookingSeedStep : ISeedStep
{
    public string Name => "booking";
    public int Order => 200;

    /// <summary>The offset the clock has before and after the run. Null (the default): whatever <c>GET /internal/v1/test/clock</c> answers at the start, so a clock someone moved is put back where it was.
    /// A value forces that baseline instead (0 puts a clock left moved by an interrupted run back to real time).</summary>
    public long? BaselineOffsetMinutes { get; set; }
    /// <summary>How long to wait, after the clock passed end + 48 h, for the service's auto-complete job to make a booking Completed (the job runs every 60 s on the offset time).</summary>
    public TimeSpan CompletionWait { get; init; } = TimeSpan.FromSeconds(75);
    public TimeSpan CompletionPoll { get; init; } = TimeSpan.FromSeconds(3);

    static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    static readonly TimeSpan CompletionWindow = TimeSpan.FromHours(48), NoShowAfter = TimeSpan.FromMinutes(20), LateCancelBefore = TimeSpan.FromHours(6), DisputeAfterEnd = TimeSpan.FromHours(1);

    /// <summary>A booking that needs the clock to be somewhere before its next action.</summary>
    sealed record Due(Work Work, DateTimeOffset At, Func<Task> Act);
    sealed record Work(DemoBooking Plan, DemoUser Client, DemoUser Practitioner, SeededPractitioner Seeded, Guid OfferingId, BookingInfo Info);

    public async Task<int> ResetAsync(SeedContext context, CancellationToken ct)
    {
        if (context.Options.BookingUrl.Length == 0) return -1;
        if (context.Options.BookingKey.Length == 0) throw new SeedException("booking: Internal:Keys:Booking is not configured");
        return await new BookingApi(context.Http.For(SeedService.Booking), context.Options, context.Time).ResetAsync(ct);
    }

    public async Task RunAsync(SeedContext context, CancellationToken ct)
    {
        if (context.Options.BookingUrl.Length == 0) { context.Log.Info("booking: skipped (Backend:Booking is not configured)"); return; }
        if (context.Options.BookingKey.Length == 0) throw new SeedException("booking: Internal:Keys:Booking is not configured");
        var api = new BookingApi(context.Http.For(SeedService.Booking), context.Options, context.Time);
        var approved = context.Practitioners.Values.Where(p => p.Status == "Approved" && p.Offerings.Count > 0).OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        if (approved.Count == 0) throw new SeedException("booking: no Approved demo practitioner with an offering (run the directory step first)");
        var clients = context.Clients.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Value).ToList();
        if (clients.Count == 0) throw new SeedException("booking: no demo clients (run the directory step first)");
        var admin = await context.Identity.SignInAsync("demo-admin", ct);

        var baseline = BaselineOffsetMinutes ?? (await api.GetClockAsync(ct)).Offset;
        var clock = new Clock(api, baseline);
        var changes = 0;
        var finished = false;
        try
        {
            var now0 = await clock.SetAsync(baseline, ct); // "now" is what the target system answers, never this machine's clock
            changes += await EnsureAvailabilityAsync(context, api, approved, ct);

            var known = await KnownAsync(api, clients, ct);
            var due = new List<Due>();
            foreach (var plan in BookingPlan.All.Where(p => p.Target != BookingTarget.PendingPayment)) // those wait: their hold must be live when the run ends
            {
                var built = await BuildAsync(context, api, plan, approved, clients, known, now0, ct);
                if (built.Created) changes++;
                changes += await AdvanceNowAsync(api, admin, built.Work, due, context, ct);
            }

            // The clock moves forward only here, in time order, and only for what is still due.
            foreach (var d in due.OrderBy(d => d.At))
            {
                await clock.MoveToAsync(d.At, ct);
                await d.Act();
                changes++;
            }
            finished = true;
        }
        finally
        {
            await clock.RestoreAsync(context.Log, throwOnFailure: finished);
        }

        var nowBack = await clock.SetAsync(baseline, ct); // the clock is at its baseline again
        var knownNow = await KnownAsync(api, clients, ct);
        foreach (var plan in BookingPlan.All.Where(p => p.Target == BookingTarget.PendingPayment))
            if ((await BuildAsync(context, api, plan, approved, clients, knownNow, nowBack, ct)).Created) changes++;

        context.Log.Info($"booking: {BookingPlan.All.Length} demo bookings {(changes == 0 ? "unchanged" : $"changes={changes}")}");
    }

    static async Task<Dictionary<string, BookingInfo>> KnownAsync(BookingApi api, List<DemoUser> clients, CancellationToken ct)
    {
        var known = new Dictionary<string, BookingInfo>();
        foreach (var c in clients)
            foreach (var b in await api.MyBookingsAsync(c, ct))
                if (KeyOf(b) is { } key) known[key] = b;
        return known;
    }

    /// <summary>The demo key a booking was made under: the <c>[demo-...]</c> at the start of its brief.</summary>
    internal static string? KeyOf(BookingInfo b) =>
        b.BriefText is { Length: > 2 } t && t[0] == '[' && t.IndexOf(']') is > 1 and var end && DemoNames.IsDemo(t[1..end]) ? t[1..end] : null;

    // ---- availability

    static async Task<int> EnsureAvailabilityAsync(SeedContext context, BookingApi api, List<SeededPractitioner> approved, CancellationToken ct)
    {
        var changes = 0;
        for (var i = 0; i < approved.Count; i++)
        {
            var user = await context.Identity.SignInAsync(approved[i].Key, ct);
            var wanted = BookingPlan.Availability(i).OrderBy(r => r).ToList();
            var have = (await api.GetAvailabilityAsync(user, ct)).OrderBy(r => r).ToList();
            if (have.SequenceEqual(wanted)) continue;
            await api.PutAvailabilityAsync(user, wanted, ct);
            changes++;
        }
        context.Log.Info($"booking: availability for {approved.Count} practitioners" + (changes == 0 ? " unchanged" : $" changes={changes}"));
        return changes;
    }

    // ---- find or create

    sealed record Built(Work Work, bool Created);

    async Task<Built> BuildAsync(SeedContext context, BookingApi api, DemoBooking plan, List<SeededPractitioner> approved, List<DemoUser> clients,
        Dictionary<string, BookingInfo> known, DateTimeOffset now0, CancellationToken ct)
    {
        DemoNames.Require(plan.Key);
        var seeded = approved[plan.Practitioner % approved.Count];
        var practitioner = await context.Identity.SignInAsync(seeded.Key, ct);
        var client = clients[plan.Client % clients.Count];
        var offeringId = seeded.Offerings.OrderBy(o => o.Key, StringComparer.Ordinal).First().Value;

        if (known.TryGetValue(plan.Key, out var existing))
        {
            context.Log.Info($"booking: {plan.Key} found as {existing.Status}");
            return new Built(new Work(plan, client, practitioner, seeded, offeringId, existing), false);
        }

        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now0, Warsaw).DateTime).AddDays(plan.Days);
        var wantedLocal = day.ToDateTime(new TimeOnly(plan.Hour, plan.Minute));
        var slots = await api.SlotsAsync(seeded.Id, offeringId, day, day.AddDays(13), ct);
        var candidates = slots.Where(s => TimeZoneInfo.ConvertTime(s, Warsaw).DateTime >= wantedLocal).Take(6).ToList();
        foreach (var start in candidates)
        {
            var outcome = await api.CreateAsync(client, plan.IdempotencyKey, offeringId, start, plan.BriefText, ct);
            if (outcome.Created is null) continue; // taken by another booking: the next offered slot
            var created = await api.GetAsync(client, outcome.Created.Id, ct);
            context.Log.Info($"booking: {plan.Key} created {seeded.Key}");
            return new Built(new Work(plan, client, practitioner, seeded, offeringId, created), true);
        }
        throw new SeedException($"booking: {plan.Key} found no free slot on or after {day:yyyy-MM-dd} for {seeded.Key}");
    }

    // ---- bring each booking forward

    /// <summary>Takes every step that needs no clock move and queues the ones that do. Returns the writes made.</summary>
    async Task<int> AdvanceNowAsync(BookingApi api, DemoUser admin, Work w, List<Due> due, SeedContext context, CancellationToken ct)
    {
        var changes = 0;
        var info = w.Info;
        var target = w.Plan.Target;

        if (info.Status == "PendingPayment")
        {
            await api.SimulatePaymentAsync(w.Client, info.Id, target == BookingTarget.Expired ? "failed" : "succeeded", ct); // a failed payment moves the booking to Expired (CONTRACTS 8.1)
            info = await api.GetAsync(w.Client, info.Id, ct);
            changes++;
        }

        switch (target, info.Status)
        {
            case (BookingTarget.CancelledByClient, "Confirmed"):
                await api.CancelAsync(w.Client, info.Id, "Zmiana planów, rezygnuję z terminu.", ct); // 24 h or more before the start: a full refund
                changes++;
                break;
            case (BookingTarget.LateCancelledByClient, "Confirmed"):
                due.Add(new Due(w, info.Start - LateCancelBefore, () => api.CancelAsync(w.Client, info.Id, "Nagła zmiana planów tuż przed terminem.", ct)));
                break;
            case (BookingTarget.Completed, "Confirmed"):
                due.Add(new Due(w, info.End + CompletionWindow + TimeSpan.FromMinutes(1), () => WaitForCompletedAsync(context, api, w, info.Id, ct)));
                break;
            case (BookingTarget.ClientNoShow, "Confirmed"):
                due.Add(new Due(w, info.Start + NoShowAfter, () => api.ReportClientNoShowAsync(w.Practitioner, info.Id, ct)));
                break;
            case (BookingTarget.Disputed or BookingTarget.Resolved, "Confirmed"):
                due.Add(new Due(w, info.End + DisputeAfterEnd, async () =>
                {
                    await api.DisputeAsync(w.Client, info.Id, "not_as_described", "Zakres konsultacji odbiegał od opisu w ofercie.", ct);
                    if (target == BookingTarget.Resolved) await api.ResolveAsync(admin, info.Id, "release", "Konsultacja odbyła się zgodnie z ofertą, środki dla programisty.", ct);
                }));
                break;
            case (BookingTarget.Resolved, "Disputed"): // an earlier run stopped between the dispute and the decision; the admin needs no clock
                await api.ResolveAsync(admin, info.Id, "release", "Konsultacja odbyła się zgodnie z ofertą, środki dla programisty.", ct);
                changes++;
                break;
        }
        return changes;
    }

    /// <summary>Polls the booking until the service's auto-complete job has made it Completed. Without that job (not on main yet) it says so and goes on: the rest of the data is still valid.</summary>
    async Task WaitForCompletedAsync(SeedContext context, BookingApi api, Work w, Guid id, CancellationToken ct)
    {
        for (var waited = TimeSpan.Zero; ; waited += CompletionPoll)
        {
            if ((await api.GetAsync(w.Client, id, ct)).Status == "Completed") return;
            if (waited >= CompletionWait) break;
            await Task.Delay(CompletionPoll, context.Time, ct);
        }
        context.Log.Info($"booking: {w.Plan.Key} is still Confirmed after its end + 48 h: no auto-complete job answered within {CompletionWait.TotalSeconds:0} s (T-04.08a not deployed?)");
    }

    /// <summary>Moves the Booking clock and puts it back. All values come from the answers of the clock endpoint.</summary>
    sealed class Clock(BookingApi api, long baselineOffset)
    {
        readonly long _baseline = baselineOffset;
        long _offset = baselineOffset; // the last offset the service confirmed

        public async Task<DateTimeOffset> SetAsync(long offset, CancellationToken ct)
        {
            var (o, now) = await api.SetClockAsync(offset, ct);
            _offset = o;
            return now;
        }

        public async Task MoveToAsync(DateTimeOffset at, CancellationToken ct)
        {
            var now = await SetAsync(_offset, ct); // the same offset, to learn "now"
            if (now >= at) return;
            await SetAsync(_offset + (long)Math.Ceiling((at - now).TotalMinutes), ct);
        }

        /// <summary>Always tried, also after a failure. A failed restore is reported; it throws only when nothing else already failed, so it never hides the first error.</summary>
        public async Task RestoreAsync(ISeedLog log, bool throwOnFailure)
        {
            try { await api.SetClockAsync(_baseline, CancellationToken.None); }
            catch (Exception e) when (e is HttpRequestException or SeedException)
            {
                log.Info($"booking: the clock could not be restored to its baseline ({e.GetType().Name}); set it with POST /internal/v1/test/clock");
                if (throwOnFailure) throw;
            }
        }
    }
}
