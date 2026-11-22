using Gabinety.Web.Components.Shared;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Account;

/// <summary>The code of the second sign-in step: either the six digits of the authenticator or a recovery code. Spaces and dashes a person types are not part of the code.</summary>
public sealed class TwoFactorModel
{
    public const string CodeId = "two-factor-code";
    public string? Code { get; set; }

    /// <summary>
    /// The code as the identity service wants it (ASP.NET Identity's own normalisation): an authenticator code loses spaces and hyphens ("123 456", "123-456"), a recovery code loses only spaces
    /// (its hyphen is part of the code).
    /// </summary>
    public string Normalised(bool recovery) => new((Code ?? "").Where(c => !char.IsWhiteSpace(c) && (recovery || c != '-')).ToArray());

    public FormProblems Validate(bool recovery = false)
    {
        var problems = new FormProblems();
        if (Normalised(recovery).Length == 0) problems.Add(CodeId, Pl.TwoFactor.CodeRequired);
        else if (Normalised(recovery).Length > 64) problems.Add(CodeId, Pl.TwoFactor.Invalid);
        return problems;
    }
}
