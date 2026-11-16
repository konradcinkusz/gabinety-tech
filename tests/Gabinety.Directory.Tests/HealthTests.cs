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

public class MeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Me_without_a_token_is_401()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_with_a_garbage_token_is_401_not_500()
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new("Bearer", "not.a.jwt");
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/me")).StatusCode);
    }
}
