using Gabinety.Web.Api;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Account;

/// <summary>The deletion form: a password (when the account has one) and the confirmation checkbox. The password is never echoed back, logged or kept beyond the request.</summary>
public sealed class DeleteAccountForm
{
    public string? Password { get; set; }
    public bool Confirm { get; set; }

    /// <summary>The message to show before anything is sent, or null when the form may go to the identity service. The checkbox is checked first, as in the Next.js form.</summary>
    public string? Problem(bool hasPassword) =>
        !Confirm ? Pl.Account.DeleteConfirmRequired
        : hasPassword && string.IsNullOrWhiteSpace(Password) ? Pl.Account.DeletePasswordRequired
        : null;

    /// <summary>The text for a refusal of the identity service: a wrong password and an outage say different things.</summary>
    public static string? MessageFor(DeleteOutcome outcome) => outcome switch
    {
        DeleteOutcome.Rejected => Pl.Account.DeleteFailed,
        DeleteOutcome.Unavailable => Pl.Account.DeleteUnavailable,
        _ => null,
    };
}

/// <summary>The consents form has no field: the post itself is the act (accept the current versions).</summary>
public sealed class AcceptConsentsForm;
