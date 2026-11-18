using System.Net;

namespace Gabinety.Assistant.Tests.Triage;

public class InternalKeyRotationTests
{
    const string Previous = "demo-internal-key-previous";

    [Fact]
    public async Task Current_and_previous_keys_both_authenticate_and_anything_else_is_401()
    {
        using var rig = new TriageRig(null, ("Internal:PreviousKey", Previous));
        Assert.Equal(HttpStatusCode.OK, (await rig.Post(5, "demo text", TriageRig.Key)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await rig.Post(5, "demo text", Previous)).StatusCode);
        foreach (var k in new[] { "demo-wrong", Previous + "x", Previous[..^1], "", null })
            Assert.Equal(HttpStatusCode.Unauthorized, (await rig.Post(5, "demo text", k)).StatusCode);
    }

    [Fact]
    public async Task With_no_previous_key_a_previous_value_is_401_and_an_empty_header_never_matches()
    {
        using var rig = new TriageRig();
        Assert.Equal(HttpStatusCode.Unauthorized, (await rig.Post(5, "demo text", Previous)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await rig.Post(5, "demo text", "")).StatusCode);
        using var blank = new TriageRig(null, ("Internal:PreviousKey", ""));
        Assert.Equal(HttpStatusCode.Unauthorized, (await blank.Post(5, "demo text", "")).StatusCode);
    }
}
