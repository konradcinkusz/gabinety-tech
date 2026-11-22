using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components.Forms;

namespace Gabinety.Web.Hosting;

/// <summary>
/// SPEC 5 rule 13: the only cookies are the session cookies of signed-in users. Found by running the host: in .NET 10 every prerendered page PERSISTS the antiforgery token of
/// <see cref="AntiforgeryStateProvider"/> (so that an interactive form could use it), and creating that token sets an antiforgery cookie and <c>Cache-Control: no-store</c>, on <c>/</c> and on a 404
/// page that have no form at all. This provider creates the token only when something ASKS for it: the <c>&lt;AntiforgeryToken /&gt;</c> inside an <c>EditForm</c>, which is a page with a form
/// (sign-in, sign-out). Validation is unchanged: <c>UseAntiforgery()</c> checks every post with <see cref="IAntiforgery"/>. A circuit has no <c>HttpContext</c> and no token, which is fine:
/// an interactive component's events go over the circuit, not through a form post.
/// </summary>
public sealed class FormOnlyAntiforgeryStateProvider(IAntiforgery antiforgery, IHttpContextAccessor accessor) : AntiforgeryStateProvider
{
    public override AntiforgeryRequestToken? GetAntiforgeryToken()
    {
        if (accessor.HttpContext is not { } context) return null;
        var tokens = antiforgery.GetAndStoreTokens(context);
        return tokens.RequestToken is { } value ? new AntiforgeryRequestToken(value, tokens.FormFieldName) : null;
    }
}
