using Gabinety.Web.Api;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.PractitionerArea;

/// <summary>What the payments provider said about the practitioner's account: not asked, no connected accounts (the mock of the fake profile), or the provider's own state.</summary>
public enum PaymentsState { Unknown, NotApplicable, None, Pending, Ready }

/// <summary><c>Known</c> is false when the fact could not be read (it is then never offered as the next step).</summary>
public sealed record NextStep(string Title, string Text, bool Done, string Href, string Key, bool Known = true);

/// <summary>
/// "What next": verification, offers, availability, payments, in that order, each with the one place it is done. The first step that is not done is the next step. Pure: the dashboard
/// loads the facts and this class decides. Payments counts as done when the provider needs nothing (the mock) and is left out of the "next" choice when it could not be read.
/// </summary>
public static class NextSteps
{
    public static PaymentsState PaymentsOf(ApiResult<PaymentAccountDto> r) => r switch
    {
        { IsOk: true, Value.State: "ready" } => PaymentsState.Ready,
        { IsOk: true, Value.State: "pending" } => PaymentsState.Pending,
        { IsOk: true } => PaymentsState.None,
        { Failure.Status: 404 } => PaymentsState.NotApplicable,
        _ => PaymentsState.Unknown,
    };

    /// <summary>The banner is for a real provider whose account is not ready; the mock (fake profile) and an unreadable answer show none.</summary>
    public static bool ShowPaymentsBanner(PaymentsState s) => s is PaymentsState.None or PaymentsState.Pending;

    public static IReadOnlyList<NextStep> Build(PractitionerPrivateDto profile, IReadOnlyList<OfferingPrivateDto> offers, int? availabilityRules, PaymentsState payments)
    {
        var approved = profile.Status == "Approved";
        return
        [
            new(PlPractitioner.Dashboard.StepVerification, approved ? PlPractitioner.Dashboard.StepVerificationDone : profile.Status == "Submitted" ? PlPractitioner.Dashboard.StepVerificationWaiting : PlPractitioner.Dashboard.StepVerificationTodo,
                approved, PractitionerPaths.Home, "verification"),
            new(PlPractitioner.Dashboard.StepOffers, offers.Any(o => o.IsActive) ? PlPractitioner.Dashboard.StepOffersDone : PlPractitioner.Dashboard.StepOffersTodo, offers.Any(o => o.IsActive), PractitionerPaths.Offers, "offers"),
            new(PlPractitioner.Dashboard.StepAvailability, availabilityRules > 0 ? PlPractitioner.Dashboard.StepAvailabilityDone : PlPractitioner.Dashboard.StepAvailabilityTodo, availabilityRules > 0, PractitionerPaths.Availability, "availability"),
            new(PlPractitioner.Dashboard.StepPayments, payments switch
            {
                PaymentsState.Ready => PlPractitioner.Dashboard.StepPaymentsDone, PaymentsState.NotApplicable => PlPractitioner.Dashboard.StepPaymentsDemo,
                PaymentsState.Unknown => PlPractitioner.Dashboard.StepPaymentsUnknown, _ => PlPractitioner.Dashboard.StepPaymentsTodo,
            }, payments is PaymentsState.Ready or PaymentsState.NotApplicable, PractitionerPaths.Payments, "payments", payments != PaymentsState.Unknown),
        ];
    }

    /// <summary>The first step that is not done (and could be read), or null when everything is.</summary>
    public static NextStep? Next(IReadOnlyList<NextStep> steps) => steps.FirstOrDefault(s => !s.Done && s.Known);
}
