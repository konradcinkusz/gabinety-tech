using Gabinety.Web.Components.Shared;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Account;

/// <summary>
/// The new-password form. <see cref="Token"/> and <see cref="Email"/> are read from the link's query ONCE, when the page is first shown, and travel as hidden fields from then on: they are never printed as
/// text, never put into a link and never logged. The password rules are those of registration (<see cref="RegisterModel"/>), every problem at once; the passwords are never echoed back.
/// </summary>
public sealed class ResetPasswordModel
{
    public const string PasswordId = "reset-password", ConfirmationId = "reset-confirmation";

    public string? Token { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? Confirmation { get; set; }

    /// <summary>True when the link did not carry both values: there is nothing to reset.</summary>
    public bool LinkIsIncomplete => string.IsNullOrWhiteSpace(Token) || string.IsNullOrWhiteSpace(Email);

    public FormProblems Validate()
    {
        var problems = new FormProblems();
        var password = Password ?? "";
        if (password.Length == 0) problems.Add(PasswordId, Pl.Register.PasswordRequired);
        else if (password.Length > RegisterModel.MaxPassword) problems.Add(PasswordId, Pl.Register.PasswordTooLong);
        else
        {
            var short_ = password.Length < RegisterModel.MinPassword;
            var weak = !RegisterModel.IsStrong(password);
            if (short_ && weak) problems.Add(PasswordId, Pl.Register.PasswordTooShortAndWeak);
            else if (short_) problems.Add(PasswordId, Pl.Register.PasswordTooShort);
            else if (weak) problems.Add(PasswordId, Pl.Register.PasswordWeak);
        }
        if (string.IsNullOrEmpty(Confirmation)) problems.Add(ConfirmationId, Pl.Reset.ConfirmationRequired);
        else if (!string.Equals(password, Confirmation, StringComparison.Ordinal)) problems.Add(ConfirmationId, Pl.Reset.Mismatch);
        return problems;
    }
}
