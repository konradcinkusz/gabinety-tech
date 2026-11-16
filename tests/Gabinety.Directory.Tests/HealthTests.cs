using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gabinety.Directory.Tests;

public class HealthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task Probe_answers_200(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_path_is_404()
    {
        var response = await factory.CreateClient().GetAsync("/no-such-path");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
