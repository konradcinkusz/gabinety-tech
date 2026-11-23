using System.Text.RegularExpressions;
using Gabinety.Web.Components.Shared;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Account;

/// <summary>The "send me a link" form: one e-mail address. A mistake catcher only; the identity service decides, and answers alike for every well-formed address.</summary>
public sealed class ForgotPasswordModel
{
    public const string EmailId = "forgot-email";
    public const int MaxEmail = 254;

    public string? Email { get; set; }

    static readonly Regex EmailShape = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public FormProblems Validate()
    {
        var problems = new FormProblems();
        var email = Email?.Trim() ?? "";
        if (email.Length == 0) problems.Add(EmailId, Pl.Register.EmailRequired);
        else if (email.Length > MaxEmail) problems.Add(EmailId, Pl.Register.EmailTooLong);
        else if (!EmailShape.IsMatch(email)) problems.Add(EmailId, Pl.Register.EmailInvalid);
        return problems;
    }
}
