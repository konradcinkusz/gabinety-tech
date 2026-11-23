using Gabinety.Web.Components.Shared;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Account;

/// <summary>
/// The change-password section of <c>/konto</c> (T-11.08): the current password, the new one and its repetition. The rules of the new password are those of registration
/// (<see cref="RegisterModel"/>) and every problem is reported at once. The passwords are never logged and never travel in a link; they go to the identity service in the body only,
/// and they are never rendered back: every password input comes back empty after a refusal.
/// </summary>
public sealed class ChangePasswordForm
{
    public const string CurrentId = "change-current", NewId = "change-new", ConfirmationId = "change-confirmation";

    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
    public string? Confirmation { get; set; }

    public FormProblems Validate()
    {
        var problems = new FormProblems();
        if (string.IsNullOrEmpty(CurrentPassword)) problems.Add(CurrentId, Pl.Account.CurrentRequired);

        var password = NewPassword ?? "";
        if (password.Length == 0) problems.Add(NewId, Pl.Register.PasswordRequired);
        else if (password.Length > RegisterModel.MaxPassword) problems.Add(NewId, Pl.Register.PasswordTooLong);
        else
        {
            var tooShort = password.Length < RegisterModel.MinPassword;
            var weak = !RegisterModel.IsStrong(password);
            if (tooShort && weak) problems.Add(NewId, Pl.Register.PasswordTooShortAndWeak);
            else if (tooShort) problems.Add(NewId, Pl.Register.PasswordTooShort);
            else if (weak) problems.Add(NewId, Pl.Register.PasswordWeak);
        }

        if (string.IsNullOrEmpty(Confirmation)) problems.Add(ConfirmationId, Pl.Reset.ConfirmationRequired);
        else if (!string.Equals(password, Confirmation, StringComparison.Ordinal)) problems.Add(ConfirmationId, Pl.Reset.Mismatch);
        return problems;
    }
}
