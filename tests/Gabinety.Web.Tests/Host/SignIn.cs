namespace Gabinety.Web.Tests;

public static class SignIn
{
    /// <summary>Signs a browser in through the real form, against the identity fake. Returns the cookie value (the session id).</summary>
    public static async Task<string> As(WebFactory web, Browser browser, string email = "demo-client@demo.example", string password = FakeIdentity.Password, string returnTo = "/")
    {
        var response = await browser.PostFormAsync($"/logowanie?redirect={Uri.EscapeDataString(returnTo)}", new Dictionary<string, string> { ["Model.Email"] = email, ["Model.Password"] = password });
        Assert.True((int)response.Status is 302 or 303, $"sign-in did not redirect: {(int)response.Status} {response.Html[..Math.Min(400, response.Html.Length)]}");
        return browser.Cookies["gt_session"];
    }
}
