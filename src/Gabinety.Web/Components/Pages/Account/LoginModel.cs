using System.ComponentModel.DataAnnotations;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Account;

/// <summary>The sign-in form. The password is never echoed back into the page, logged or kept beyond the request.</summary>
public sealed class LoginModel
{
    [Required(ErrorMessage = Pl.Login.Required), StringLength(254)]
    public string? Email { get; set; }

    [Required(ErrorMessage = Pl.Login.Required), StringLength(200)]
    public string? Password { get; set; }
}
