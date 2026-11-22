using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.PractitionerArea;

/// <summary>How the payments page describes the account state, and which action it offers. Pure, so the wording and the rule "no ready account, no bookings" are tested without a page.</summary>
public static class PaymentsView
{
    public static string Label(PaymentsState s) => s switch { PaymentsState.Ready => PlPractitioner.Payments.Ready, PaymentsState.None => PlPractitioner.Payments.NotStarted, _ => PlPractitioner.Payments.NeedsWork };

    /// <summary>The badge kind (contract 5.7): ready is success, a started account that needs work is a warning, everything else (not started, demo, unknown) is neutral.</summary>
    public static string Kind(PaymentsState s) => s switch { PaymentsState.Ready => "success", PaymentsState.Pending => "warning", _ => "neutral" };

    public static string Body(PaymentsState s) => s switch
    {
        PaymentsState.Ready => PlPractitioner.Payments.ReadyBody, PaymentsState.Pending => PlPractitioner.Payments.PendingBody, PaymentsState.None => PlPractitioner.Payments.NoneBody,
        PaymentsState.NotApplicable => PlPractitioner.Payments.Demo, _ => PlPractitioner.Payments.Unavailable,
    };

    /// <summary>The label of the one button, or null when there is nothing to start (ready, demo, unknown).</summary>
    public static string? Action(PaymentsState s) => s switch { PaymentsState.None => PlPractitioner.Payments.Start, PaymentsState.Pending => PlPractitioner.Payments.Continue, _ => null };

    /// <summary>
    /// The single-use link of the provider's hosted flow is followed only when it is an absolute https URL with no credentials in it: the answer of a backend is not trusted to send a visitor anywhere,
    /// and a <c>javascript:</c>, <c>data:</c> or plain-http link is refused.
    /// </summary>
    public static bool IsFollowable(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(u.UserInfo) && url!.Length <= 2048;
}
