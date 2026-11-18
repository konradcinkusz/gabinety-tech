using Gabinety.ServiceDefaults;
using Microsoft.Extensions.Configuration;

namespace Gabinety.Directory.Tests.Internal;

/// <summary>The shared key check of ServiceDefaults (Security.cs), without a host. Used by Directory, Booking, Reputation and Assistant.</summary>
public class InternalKeyMatchTests
{
    static IConfiguration Cfg(string? key, string? previous) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Internal:Key"] = key, ["Internal:PreviousKey"] = previous }).Build();

    [Theory]
    [InlineData("new", "old", "new", true)]
    [InlineData("new", "old", "old", true)]
    [InlineData("new", "old", "other", false)]
    [InlineData("new", "old", "ne", false)]
    [InlineData("new", "old", "newold", false)]
    [InlineData("new", null, "old", false)]
    [InlineData("new", "", "", false)]      // an empty previous never matches an empty header
    [InlineData("new", null, "", false)]
    [InlineData("new", "old", "", false)]
    [InlineData("new", "old", null, false)]
    [InlineData("", "old", "old", false)]   // no current key configured: the check is off, even for the previous value
    [InlineData(null, "old", "old", false)]
    [InlineData("", "", "", false)]
    [InlineData(null, null, null, false)]
    public void Matches_current_or_previous_only(string? key, string? previous, string? given, bool expected) =>
        Assert.Equal(expected, InternalKey.Matches(Cfg(key, previous), given));
}
