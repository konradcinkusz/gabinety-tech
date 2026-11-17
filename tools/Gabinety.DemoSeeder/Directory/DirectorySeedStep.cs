namespace Gabinety.DemoSeeder;

/// <summary>
/// The Directory data (T-08.01a): the demo clients, ten practitioners with profile, GitHub link and offerings in all statuses, and the ranking inputs.
/// Everything goes through the HTTP APIs, as the user or the admin would. Idempotent: each call is made only when the Directory does not already hold the wanted state.
/// </summary>
public sealed class DirectorySeedStep : ISeedStep
{
    /// <summary>The pushes of this step are the baseline. Later steps that push real figures (Booking, Reputation) must use a version above this, or their push is ignored (T-03.08 is monotonic).</summary>
    public const long BaselineVersion = 1;

    public string Name => "directory";
    public int Order => 100;

    public async Task<int> ResetAsync(SeedContext context, CancellationToken ct) =>
        await new DirectoryApi(context.Http.For(SeedService.Directory), context.Options, context.Time).ResetAsync(ct);

    public async Task RunAsync(SeedContext context, CancellationToken ct)
    {
        var api = new DirectoryApi(context.Http.For(SeedService.Directory), context.Options, context.Time);
        var taxonomy = await WaitForTaxonomyAsync(context, api, ct);

        foreach (var name in DemoCatalog.ClientNames) context.Clients[name] = await context.Identity.SignInAsync(name, ct);
        context.Log.Info($"directory: {context.Clients.Count} demo clients");

        var admin = await context.Identity.SignInAsync("demo-admin", ct);
        foreach (var demo in DemoCatalog.Practitioners)
            await SeedPractitionerAsync(context, api, admin, taxonomy, demo, ct);
    }

    /// <summary>The Directory fills its taxonomy after it starts listening (migrations run post-start), so an empty answer is waited out, up to 30 seconds.</summary>
    static async Task<TaxonomyView> WaitForTaxonomyAsync(SeedContext context, DirectoryApi api, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var taxonomy = await api.TaxonomyAsync(ct);
            if (taxonomy.Problems.Length > 0 && taxonomy.Techs.Length > 0) return taxonomy;
            if (attempt == 30) throw new SeedException("directory: the taxonomy is still empty after 30 s (has the Directory finished starting?)");
            await Task.Delay(TimeSpan.FromSeconds(1), context.Time, ct);
        }
    }

    static async Task SeedPractitionerAsync(SeedContext context, DirectoryApi api, DemoUser admin, TaxonomyView taxonomy, DemoPractitioner demo, CancellationToken ct)
    {
        DemoNames.Require(demo.UserName); DemoNames.Require(demo.Slug);
        var user = await context.Identity.SignInAsync(demo.UserName, ct);
        var changes = 0;

        var mine = await api.GetMineAsync(user, ct);
        if (mine is null) { mine = await api.CreateAsync(user, demo.DisplayName, demo.Slug, ct); changes++; }
        if (mine.Slug != demo.Slug) throw new SeedException($"directory: {demo.UserName} already has a profile under another slug");

        var complete = demo.Target != DemoStatus.Draft;
        var wanted = new
        {
            displayName = demo.DisplayName, headline = demo.Headline, bio = demo.Bio,
            portfolioUrl = complete ? demo.PortfolioUrl : null, meetingUrlDefault = complete ? demo.MeetingUrl : null, nip = complete ? demo.Nip : null,
        };
        if (mine.DisplayName != wanted.displayName || mine.Headline != wanted.headline || mine.Bio != wanted.bio || mine.PortfolioUrl != wanted.portfolioUrl
            || mine.MeetingUrlDefault != wanted.meetingUrlDefault || mine.Nip != wanted.nip)
        {
            mine = await api.UpdateAsync(user, wanted, ct); changes++;
        }

        if (complete && !mine.GithubLinked) { await api.LinkGithubAsync(user, demo.GithubLogin, ct); changes++; }

        var seeded = new SeededPractitioner(demo.UserName, user.UserId, mine.Id, mine.Slug) { Status = mine.Status };
        changes += await EnsureOfferingsAsync(api, user, taxonomy, demo, seeded, ct);

        foreach (var trigger in StatusPlan.Triggers(Enum.Parse<DemoStatus>(mine.Status), demo.Target))
        {
            if (trigger == Trigger.Submit) await api.SubmitAsync(user, ct);
            else await api.AdminAsync(admin, mine.Id, trigger, trigger is Trigger.Reject or Trigger.Suspend ? demo.Reason : null, ct);
            seeded.Status = trigger switch { Trigger.Submit => "Submitted", Trigger.Reject => "Rejected", Trigger.Suspend => "Suspended", _ => "Approved" };
            changes++;
        }

        if (demo.Stats is { } s) // ignored by the Directory when the version is not above the stored one, so a second run changes nothing
        {
            await api.PushReputationAsync(mine.Id, s.Reviews, s.Average, BaselineVersion, ct);
            await api.PushStatsAsync(mine.Id, s, BaselineVersion, ct);
        }

        context.Practitioners[demo.UserName] = seeded;
        context.Log.Info($"directory: {demo.UserName} {demo.Slug} {seeded.Status} offerings={seeded.Offerings.Count} {(changes == 0 ? "unchanged" : $"changes={changes}")}");
    }

    static async Task<int> EnsureOfferingsAsync(DirectoryApi api, DemoUser user, TaxonomyView taxonomy, DemoPractitioner demo, SeededPractitioner seeded, CancellationToken ct)
    {
        if (demo.Offerings.Length == 0) return 0;
        var existing = (await api.OfferingsAsync(user, ct)).ToDictionary(o => o.Title);
        var changes = 0;
        foreach (var o in demo.Offerings)
        {
            var problem = Pick(taxonomy.Problems, o.ProblemSlot);
            var techs = o.TechSlots.Select(t => Pick(taxonomy.Techs, t)).Distinct().ToArray();
            var body = new { title = o.Title, description = o.Description, durationMinutes = o.Minutes, priceMinor = o.PriceMinor, problemSlug = problem, techSlugs = techs };
            if (!existing.TryGetValue(o.Title, out var have)) { seeded.Offerings[o.Title] = await api.CreateOfferingAsync(user, body, ct); changes++; continue; }
            seeded.Offerings[o.Title] = have.Id;
            if (have.Description != o.Description || have.DurationMinutes != o.Minutes || have.PriceMinor != o.PriceMinor || have.ProblemSlug != problem || !have.TechSlugs.SequenceEqual(techs))
            {
                await api.UpdateOfferingAsync(user, have.Id, body, ct); changes++;
            }
        }
        return changes;
    }

    static string Pick(string[] slugs, int slot) => slugs[slot % slugs.Length];
}
