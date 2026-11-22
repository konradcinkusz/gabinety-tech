using Gabinety.Web.Components.Shared;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Account;

/// <summary>The code of the second sign-in step: either the six digits of the authenticator or a recovery code. Spaces and dashes a person types are not part of the code.</summary>
public sealed class TwoFactorModel
{
    public const string CodeId = "two-factor-code";
    public string? Code { get; set; }

    /// <summary>The code as the identity service wants it: no spaces, no dashes.</summary>
    public string Normalised => new((Code ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

    public FormProblems Validate()
    {
        var problems = new FormProblems();
        if (Normalised.Length == 0) problems.Add(CodeId, Pl.TwoFactor.CodeRequired);
        else if (Normalised.Length > 64) problems.Add(CodeId, Pl.TwoFactor.Invalid);
        return problems;
    }
}
