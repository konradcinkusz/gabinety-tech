using Gabinety.Web.Components.Shared;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Account;

/// <summary>The sign-in form. The password is never echoed back into the page, logged or kept beyond the request. Both missing fields are reported at once.</summary>
public sealed class LoginModel
{
    public const string EmailId = "login-email", PasswordId = "login-password";
    public const int MaxEmail = 254, MaxPassword = 200;

    public string? Email { get; set; }
    public string? Password { get; set; }

    public FormProblems Validate()
    {
        var problems = new FormProblems();
        if (string.IsNullOrWhiteSpace(Email)) problems.Add(EmailId, Pl.Login.EmailRequired);
        else if (Email.Trim().Length > MaxEmail) problems.Add(EmailId, Pl.Login.EmailInvalid);
        if (string.IsNullOrEmpty(Password)) problems.Add(PasswordId, Pl.Login.PasswordRequired);
        else if (Password.Length > MaxPassword) problems.Add(PasswordId, Pl.Login.InvalidCredentials);
        return problems;
    }
}
