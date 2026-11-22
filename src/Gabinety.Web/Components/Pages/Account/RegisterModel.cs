using System.Text.RegularExpressions;
using Gabinety.Web.Components.Shared;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Account;

/// <summary>
/// The registration form and its rules, in plain C# (unit tested without a renderer). The rules of authservice's password policy (a digit, a lower-case and an upper-case letter, a character that
/// is not a letter or digit, 8 or more) are checked HERE so that every problem is shown at once; the server remains the judge. The password is never echoed back, logged or kept.
/// </summary>
public sealed class RegisterModel
{
    public const string EmailId = "register-email", PasswordId = "register-password", ConsentId = "register-consent";
    public const int MinPassword = 8, MaxPassword = 200, MaxEmail = 254;

    public string? Email { get; set; }
    public string? Password { get; set; }
    public bool Consent { get; set; }

    /// <summary>The versions of the terms and the privacy text the page showed, sent back verbatim (hidden fields; never printed as text).</summary>
    public string? TermsVersion { get; set; }
    public string? PrivacyVersion { get; set; }

    // One "@", something on both sides, a dot in the domain: a mistake catcher, not an RFC parser (authservice decides).
    static readonly Regex EmailShape = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public FormProblems Validate()
    {
        var problems = new FormProblems();
        var email = Email?.Trim() ?? "";
        if (email.Length == 0) problems.Add(EmailId, Pl.Register.EmailRequired);
        else if (email.Length > MaxEmail) problems.Add(EmailId, Pl.Register.EmailTooLong);
        else if (!EmailShape.IsMatch(email)) problems.Add(EmailId, Pl.Register.EmailInvalid);

        var password = Password ?? "";
        if (password.Length == 0) problems.Add(PasswordId, Pl.Register.PasswordRequired);
        else if (password.Length > MaxPassword) problems.Add(PasswordId, Pl.Register.PasswordTooLong);
        else
        {
            var short_ = password.Length < MinPassword;
            var weak = !IsStrong(password);
            if (short_ && weak) problems.Add(PasswordId, Pl.Register.PasswordTooShortAndWeak);
            else if (short_) problems.Add(PasswordId, Pl.Register.PasswordTooShort);
            else if (weak) problems.Add(PasswordId, Pl.Register.PasswordWeak);
        }

        if (!Consent) problems.Add(ConsentId, Pl.Register.ConsentRequired);
        return problems;
    }

    public static bool IsStrong(string password) =>
        // ASP.NET Identity's own definitions (PasswordValidator): ASCII digits and ASCII letters, and "not a letter or digit" for the special character. A Polish letter does not count as a lower- or upper-case letter there.
        password.Any(c => c is >= '0' and <= '9') && password.Any(c => c is >= 'a' and <= 'z') && password.Any(c => c is >= 'A' and <= 'Z') && password.Any(c => !char.IsLetterOrDigit(c));
}
