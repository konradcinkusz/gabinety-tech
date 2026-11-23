namespace Gabinety.DemoSeeder;

/// <summary>
/// The collaboration proposals (ADR proposal 0013, T-13.02): three negotiations in three states, made through the Booking API as the demo clients and programmers would. Idempotent: a client who already
/// has a proposal to that programmer (in any state, closed ones included) is left alone, so a second run creates and changes nothing. The demo reset removes these rows with the bookings (Booking's own
/// <c>POST /internal/v1/demo/reset</c> covers both), so this step has nothing of its own to reset.
/// </summary>
public sealed class ProposalSeedStep : ISeedStep
{
    public string Name => "proposals";
    public int Order => 250; // after Booking (200), before Reputation (300)

    public Task<int> ResetAsync(SeedContext context, CancellationToken ct) => Task.FromResult(-1); // Booking's reset removes the proposals too

    public async Task RunAsync(SeedContext context, CancellationToken ct)
    {
        if (context.Options.BookingUrl.Length == 0) { context.Log.Info("proposals: skipped (Backend:Booking is not configured)"); return; }
        var api = new BookingApi(context.Http.For(SeedService.Booking), context.Options, context.Time);
        var today = DateOnly.FromDateTime(context.Time.GetUtcNow().UtcDateTime);
        int created = 0, unchanged = 0;

        foreach (var thread in ProposalPlan.All)
        {
            DemoNames.Require(thread.Client); DemoNames.Require(thread.Practitioner);
            if (!context.Practitioners.TryGetValue(thread.Practitioner, out var practitioner) || practitioner.Status != "Approved")
            {
                context.Log.Info($"proposals: {thread.Client} to {thread.Practitioner} skipped (the programmer is not Approved)");
                continue;
            }
            var client = context.Clients.TryGetValue(thread.Client, out var c) ? c : await context.Identity.SignInAsync(thread.Client, ct);
            if ((await api.ProposalTargetsAsync(client, ct)).Contains(practitioner.Id)) { unchanged++; continue; }

            object Terms(long rate, int? hoursPerWeek, int? total, int? months) =>
                new { rate = new { amountMinor = rate, currency = "PLN" }, hoursPerWeek, totalHours = total, durationMonths = months, startDate = today.AddDays(thread.StartInDays).ToString("yyyy-MM-dd") };

            var (id, round) = await api.CreateProposalAsync(client, practitioner.Id, thread.Kind, Terms(thread.RateMinor, thread.HoursPerWeek, thread.TotalHours, thread.DurationMonths),
                thread.Text, thread.Company, Guid.NewGuid(), ct);
            var programmer = await context.Identity.SignInAsync(thread.Practitioner, ct);
            switch (thread.Outcome)
            {
                case ProposalOutcome.Countered:
                    await api.CounterProposalAsync(programmer, id, round, Terms(thread.CounterRateMinor, 10, null, thread.DurationMonths), thread.CounterText, ct);
                    break;
                case ProposalOutcome.Accepted:
                    await api.AcceptProposalAsync(programmer, id, round, ct);
                    break;
            }
            created++;
            context.Log.Info($"proposals: {thread.Client} to {thread.Practitioner} {thread.Outcome}");
        }
        context.Log.Info($"proposals: created={created} unchanged={unchanged}");
    }
}
