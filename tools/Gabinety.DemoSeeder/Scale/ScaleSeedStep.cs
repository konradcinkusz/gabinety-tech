namespace Gabinety.DemoSeeder;

/// <summary>
/// The scaled dataset (T-10.04): with <c>--factor N</c> above 1 it adds <see cref="ScalePlan.Extras"/> Approved practitioners to the Directory, each registered in the identity provider,
/// with a complete profile, a linked GitHub account, offerings, and the rating and booking figures pushed at the baseline version. Idempotent like the Directory step it mirrors.
/// Runs last (order 400) and leaves <see cref="SeedContext.Practitioners"/> alone, so Booking and Reputation see only the ordinary demo data.
/// Reset needs nothing here: the Directory's reset removes the whole <c>demo-</c> namespace, the extras included.
/// </summary>
public sealed class ScaleSeedStep : ISeedStep
{
    public string Name => "scale";
    public int Order => 400;

    public Task<int> ResetAsync(SeedContext context, CancellationToken ct) => Task.FromResult(0); // nothing of its own: the Directory step's reset removes the extras with the rest of the namespace

    public async Task RunAsync(SeedContext context, CancellationToken ct)
    {
        var factor = context.Options.Factor;
        if (factor <= 1) { context.Log.Info("scale: factor 1, nothing to add"); return; }
        var api = new DirectoryApi(context.Http.For(SeedService.Directory), context.Options, context.Time);
        var taxonomy = await api.TaxonomyAsync(ct);
        var admin = await context.Identity.SignInAsync("demo-admin", ct);
        var extras = ScalePlan.Extras(factor);
        context.Log.Info($"scale: factor {factor}: {extras.Count} extra practitioners, {ScalePlan.TotalReviews(factor)} reviews in all (rating counts)");
        var changed = 0;
        for (var i = 0; i < extras.Count; i++)
        {
            changed += await SeedOneAsync(context, api, admin, taxonomy, extras[i], ct);
            if ((i + 1) % 100 == 0) context.Log.Info($"scale: {i + 1} of {extras.Count}");
        }
        context.Log.Info($"scale: done, {changed} changes");
    }

    static async Task<int> SeedOneAsync(SeedContext context, DirectoryApi api, DemoUser admin, TaxonomyView taxonomy, ScaledPractitioner p, CancellationToken ct)
    {
        DemoNames.Require(p.UserName); DemoNames.Require(p.Slug);
        var user = await context.Identity.SignInAsync(p.UserName, ct);
        var changes = 0;
        var mine = await api.GetMineAsync(user, ct);
        if (mine is null) { mine = await api.CreateAsync(user, p.DisplayName, p.Slug, ct); changes++; }
        if (mine.Slug != p.Slug) throw new SeedException($"scale: {p.UserName} already has a profile under another slug");

        if (mine.Headline != p.Headline || mine.Bio != p.Bio || mine.PortfolioUrl != p.PortfolioUrl || mine.MeetingUrlDefault != p.MeetingUrl || mine.Nip != p.Nip || mine.DisplayName != p.DisplayName)
        {
            mine = await api.UpdateAsync(user, new { displayName = p.DisplayName, headline = p.Headline, bio = p.Bio, portfolioUrl = p.PortfolioUrl, meetingUrlDefault = p.MeetingUrl, nip = p.Nip }, ct);
            changes++;
        }
        if (!mine.GithubLinked) { await api.LinkGithubAsync(user, p.GithubLogin, ct); changes++; }

        var existing = (await api.OfferingsAsync(user, ct)).Select(o => o.Title).ToHashSet();
        foreach (var o in p.Offerings.Where(o => !existing.Contains(o.Title)))
        {
            await api.CreateOfferingAsync(user, new
            {
                title = o.Title, description = o.Description, durationMinutes = o.Minutes, priceMinor = o.PriceMinor,
                problemSlug = taxonomy.Problems[o.ProblemSlot % taxonomy.Problems.Length], techSlugs = o.TechSlots.Select(t => taxonomy.Techs[t % taxonomy.Techs.Length]).Distinct().ToArray(),
            }, ct);
            changes++;
        }

        foreach (var trigger in StatusPlan.Triggers(Enum.Parse<DemoStatus>(mine.Status), DemoStatus.Approved))
        {
            if (trigger == Trigger.Submit) await api.SubmitAsync(user, ct); else await api.AdminAsync(admin, mine.Id, trigger, null, ct);
            changes++;
        }

        await api.PushReputationAsync(mine.Id, p.Stats.Reviews, p.Stats.Average, DirectorySeedStep.BaselineVersion, ct);
        await api.PushStatsAsync(mine.Id, p.Stats, DirectorySeedStep.BaselineVersion, ct);
        return changes;
    }
}
