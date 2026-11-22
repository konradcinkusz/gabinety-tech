using System.Net;

namespace Gabinety.Web.Tests.Pipeline;

/// <summary>
/// Found by running the host in a real browser: a browser asks for static assets with Accept-Encoding, and the pre-compressed variants (.gz, .br) must be found. A plain HttpClient and curl send no
/// Accept-Encoding, so every other test of the host passed while every browser got a 500 for the stylesheets and the framework script. These ask the way a browser does.
/// </summary>
public class CompressedAssetsTests
{
    static readonly string[] Assets = ["/_framework/blazor.web.js", "/tokens.css", "/app.css", "/Gabinety.Web.styles.css"];

    [Theory]
    [InlineData("gzip")] [InlineData("br")] [InlineData("gzip, deflate, br, zstd")]
    public async Task Static_assets_answer_a_browsers_Accept_Encoding(string acceptEncoding)
    {
        await using var web = new WebFactory();
        var client = web.CreateClient(new() { AllowAutoRedirect = false });
        foreach (var path in Assets)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "no-cors");
            var response = await client.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} with Accept-Encoding: {acceptEncoding} -> {(int)response.StatusCode}");
            Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType); // not the error page
        }
    }
}
