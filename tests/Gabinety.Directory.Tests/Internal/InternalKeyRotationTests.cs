using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Gabinety.Directory.Tests.Internal;

/// <summary>Directory host mid-rotation: <c>Internal:Key</c> is the new value, <c>Internal:PreviousKey</c> the old one (RUNBOOK 5.3).</summary>
public sealed class RotationFactory : InternalFactory
{
    public const string Previous = "demo-internal-key-previous-0001";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Internal:PreviousKey", Previous);
    }
}

public class InternalKeyRotationTests(RotationFactory f) : IClassFixture<RotationFactory>
{
    static string Path() => $"/internal/v1/practitioners/by-user/demo-{Guid.NewGuid():n}";

    [Fact]
    public async Task Current_and_previous_keys_both_reach_the_endpoint() // 404 = authenticated, no such user
    {
        Assert.Equal(HttpStatusCode.NotFound, (await f.Internal(RotationFactory.Key).GetAsync(Path())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Internal(RotationFactory.Previous).GetAsync(Path())).StatusCode);
    }

    [Fact]
    public async Task A_key_that_is_neither_current_nor_previous_is_401_also_a_prefix_of_either()
    {
        foreach (var k in new[] { "demo-wrong-key", RotationFactory.Previous + "x", RotationFactory.Previous[..^1], RotationFactory.Key[..^1] })
            Assert.Equal(HttpStatusCode.Unauthorized, (await f.Internal(k).GetAsync(Path())).StatusCode);
    }

    [Fact]
    public async Task An_empty_or_missing_header_is_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Internal(null).GetAsync(Path())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Internal("").GetAsync(Path())).StatusCode);
    }
}

public class InternalKeyNoPreviousTests(InternalFactory f) : IClassFixture<InternalFactory>
{
    [Fact]
    public async Task With_no_previous_key_configured_an_empty_or_missing_header_is_401_and_the_current_key_works()
    {
        string P() => $"/internal/v1/practitioners/by-user/demo-{Guid.NewGuid():n}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Internal(null).GetAsync(P())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Internal("").GetAsync(P())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Internal().GetAsync(P())).StatusCode);
    }
}
