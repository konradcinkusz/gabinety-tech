using Gabinety.Booking.Clients;
using Gabinety.Booking.Features.Bookings.View;
using Gabinety.Booking.Features.Internal.Erasure;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Notifications;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;

namespace Gabinety.Booking.Features.Proposals;

/// <summary>
/// Builds what a party sees of a proposal (ADR proposal 0013 section 3). Terms and rounds are for both parties. The practitioner sees the client as the "Klient D." snapshot plus the company name when
/// one was given. The counterpart's e-mail appears ONLY in the Accepted state. After an erasure the counterpart reads <see cref="PartyNamesLookup.ErasedName"/> and the text "[usunięto]".
/// A party who is neither gets nothing (the callers answer 404). The practitioner's name and slug come from Directory; when it cannot answer the view still renders with a fallback.
/// </summary>
public sealed class ProposalViewBuilder(IDirectoryClient directory, IContactResolver contacts)
{
    public static ProposalActor? RoleOf(Proposal p, string callerId) =>
        p.ClientUserId == callerId ? ProposalActor.Client : p.PractitionerUserId == callerId ? ProposalActor.Practitioner : null;

    static bool Erased(string userId) => userId.StartsWith(ErasureFeature.TokenPrefix, StringComparison.Ordinal);

    public async Task<Dictionary<Guid, (PractitionerRef Practitioner, ClientRef Client)>> NamesAsync(IReadOnlyCollection<Proposal> rows, CancellationToken ct)
    {
        var live = rows.Where(p => !Erased(p.PractitionerUserId)).Select(p => p.PractitionerId).Distinct().ToList();
        var found = new Dictionary<Guid, DirectoryPractitionerName>();
        var lookups = await Task.WhenAll(live.Select(async id =>
        {
            try { return await directory.GetPractitionerNameAsync(id, ct); }
            catch (DirectoryUnavailableException) { return null; }
        }));
        foreach (var n in lookups) if (n is not null) found[n.PractitionerId] = n;

        return rows.ToDictionary(p => p.Id, p =>
        {
            var erasedPractitioner = Erased(p.PractitionerUserId);
            var name = erasedPractitioner ? PartyNamesLookup.ErasedName : found.TryGetValue(p.PractitionerId, out var n) ? n.DisplayName : PartyNamesLookup.PractitionerFallback;
            var slug = erasedPractitioner ? "" : found.TryGetValue(p.PractitionerId, out var m) ? m.Slug : "";
            var client = Erased(p.ClientUserId) ? PartyNamesLookup.ErasedName : p.ClientDisplayName ?? PartyNamesLookup.ClientFallback;
            return (new PractitionerRef(p.PractitionerId, name, slug), new ClientRef(client));
        });
    }

    public static ProposalSummary Summary(Proposal p, ProposalActor viewer, PractitionerRef practitioner, ClientRef client) => new(
        p.Id, p.Kind, p.Status, ProposalTermsRules.Of(p), practitioner, client, p.CompanyName, p.RoundCount,
        ProposalStateMachine.Turn(p.Status) == viewer, p.LastActionAt, ProposalStateMachine.IsOpen(p.Status) ? p.ExpiresAt : null);

    /// <summary>The full view. <paramref name="p"/> must have its rounds loaded.</summary>
    public async Task<ProposalView> ViewAsync(Proposal p, ProposalActor viewer, CancellationToken ct)
    {
        var (practitioner, client) = (await NamesAsync([p], ct))[p.Id];
        var flags = ProposalPermissions.For(p.Status, viewer, p.RoundCount);
        var rounds = p.Rounds.OrderBy(r => r.Number).Select(r => new ProposalRoundDto(r.Number, r.By, r.Action,
            r.RateMinor is { } rate ? new ProposalTerms(new MoneyDto(rate), r.HoursPerWeek, r.TotalHours, r.DurationMonths, r.StartDate ?? default) : null,
            r.Text, r.CreatedAt)).ToList();
        return new ProposalView(p.Id, p.Kind, p.Status, ProposalTermsRules.Of(p), practitioner, client, p.CompanyName, rounds, p.RoundCount, viewer,
            ProposalStateMachine.IsOpen(p.Status) ? p.ExpiresAt : null, flags.CanAccept, flags.CanCounter, flags.CanReject, flags.CanWithdraw,
            p.Status == ProposalStatus.Accepted ? new ProposalContact(await CounterpartEmailAsync(p, viewer, ct)) : null);
    }

    /// <summary>The other party's address: the client's snapshot for the practitioner, Directory's for the client. Null when there is none (erased, or Directory is down: the view says so).</summary>
    async Task<string?> CounterpartEmailAsync(Proposal p, ProposalActor viewer, CancellationToken ct)
    {
        if (viewer == ProposalActor.Practitioner) return Erased(p.ClientUserId) ? null : UserEmail.Normalize(p.ClientEmail);
        return Erased(p.PractitionerUserId) ? null : await contacts.ResolveAddressAsync(new ContactLookup(p.PractitionerUserId, true, null), ct);
    }
}

/// <summary>Registers what the proposal features share.</summary>
public sealed class ProposalsCoreFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.AddScoped<ProposalViewBuilder>();
        builder.Services.AddSingleton<ProposalRateLimiter>();
    }

    public void Map(IEndpointRouteBuilder endpoints) { }
}
