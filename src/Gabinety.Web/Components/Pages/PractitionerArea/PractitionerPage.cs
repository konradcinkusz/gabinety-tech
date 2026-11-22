using Gabinety.Web.Api;
using Gabinety.Web.Components.Layout;
using Gabinety.Web.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace Gabinety.Web.Components.Pages.PractitionerArea;

/// <summary>
/// What the pages of the practitioner's area share: the signed-in user's Directory client, the posted form, the redirect after a save, and the guard. The pages are static server rendering with
/// plain forms (antiforgery, works without JavaScript): a handler reads the posted fields, calls the service, and either redirects with a flash (post, redirect, get: a refresh does not post again) or
/// shows the errors where they belong.
/// </summary>
public abstract class PractitionerPage : ComponentBase
{
    [Inject] protected PractitionerClient Practitioner { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;
    [CascadingParameter] protected HttpContext HttpContext { get; set; } = default!;

    /// <summary>The fields of the request's form; empty on a GET.</summary>
    protected FormValues Posted => HttpContext.Request.HasFormContentType ? new FormValues(HttpContext.Request.Form) : FormValues.Empty;

    /// <summary>The profile, set by <see cref="LoadProfileAsync"/>.</summary>
    protected PractitionerPrivateDto? Profile { get; private set; }

    /// <summary>The failure that stopped the page from loading (a service that is down), shown as an alert in place of the content.</summary>
    protected ApiFailure? LoadFailure { get; set; }

    /// <summary>Goes to <paramref name="path"/>, with a flash key when there is one. Call it as the last thing a handler does.</summary>
    protected void Done(string path, string? flash = null) => Navigation.NavigateTo(flash is null ? path : $"{path}?{FlashKey.Parameter}={flash}");

    /// <summary>True when the account has no practitioner profile (only set when <c>LoadProfileAsync(missingIsState: true)</c> asked for it).</summary>
    protected bool NoProfile { get; private set; }

    /// <summary>
    /// The practitioner guard. Returns true when the page may render with <see cref="Profile"/>. An account with no profile is sent to the dashboard, which onboards it (a redirect, not a silent empty page),
    /// unless the page IS the dashboard (<paramref name="missingIsState"/>): then it is <see cref="NoProfile"/>. A session the service no longer accepts is sent to sign-in; a service that is down is <see cref="LoadFailure"/>.
    /// </summary>
    protected async Task<bool> LoadProfileAsync(bool missingIsState = false)
    {
        var r = await Practitioner.ProfileAsync();
        if (r.IsOk) { Profile = r.Value; NoProfile = false; return true; }
        switch (r.Failure!)
        {
            case { Status: 404 } when missingIsState: NoProfile = true; break;
            case { Status: 404 }: Navigation.NavigateTo(PractitionerPaths.Home); break;
            case { Status: 401 }: Navigation.NavigateTo(Auth.ReturnTarget.LoginUrl(Auth.ReturnTarget.Safe("/" + Navigation.ToBaseRelativePath(Navigation.Uri)))); break;
            default:
                LoadFailure = r.Failure;
                if (HttpContext is { Response.HasStarted: false }) HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; // not a 200 error page (as the public lists)
                break;
        }
        return false;
    }

    protected static string FailureText(ApiFailure f) => f switch { { Status: 0 } => PlPractitioner.Common.Network, { Status: 429 } or { Status: 503 } => PlPractitioner.Common.Unavailable, _ => PlPractitioner.Common.Unavailable };
}
