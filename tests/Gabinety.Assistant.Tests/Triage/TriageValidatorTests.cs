using Gabinety.Assistant.Features.Triage;
using Gabinety.Contracts.Assistant;

namespace Gabinety.Assistant.Tests.Triage;

public class TriageValidatorTests
{
    [Fact]
    public void A_hold_with_reasons_from_the_closed_set_is_a_draft()
    {
        var (d, e) = TriageValidator.Validate("""{"flag":"hold","reasons":["spam","personal_data","off_topic"]}""");
        Assert.Null(e);
        Assert.Equal(TriageVerdict.Hold, d!.Flag);
        Assert.Equal([TriageReason.Spam, TriageReason.PersonalData, TriageReason.OffTopic], d.Reasons);
    }

    [Fact]
    public void None_without_reasons_is_a_draft_and_a_markdown_fence_is_tolerated()
    {
        var (d, _) = TriageValidator.Validate("```json\n{\"flag\":\"none\",\"reasons\":[]}\n```");
        Assert.Equal(TriageVerdict.None, d!.Flag);
        Assert.Empty(d.Reasons);
    }

    [Theory]
    [InlineData("""{"flag":"hold","reasons":["fraud"]}""")]                       // a reason outside the closed set
    [InlineData("""{"flag":"hold","reasons":["Spam"]}""")]                        // wire values are exact
    [InlineData("""{"flag":"hold","reasons":["PersonalData"]}""")]
    [InlineData("""{"flag":"hold","reasons":[]}""")]                              // a hold names a reason for the moderator
    [InlineData("""{"flag":"none","reasons":["spam"]}""")]                        // none carries no reasons
    [InlineData("""{"flag":"hold","reasons":["spam","spam"]}""")]
    [InlineData("""{"flag":"hold","reasons":["spam","abuse","threat","off_topic","personal_data","spam"]}""")]
    [InlineData("""{"flag":"reject","reasons":["spam"]}""")]                      // the model may not decide; only none|hold exist
    [InlineData("""{"flag":"publish","reasons":[]}""")]
    [InlineData("""{"flag":"HOLD","reasons":["spam"]}""")]
    [InlineData("""{"reasons":["spam"]}""")]
    [InlineData("""{"flag":"hold"}""")]
    [InlineData("""{"flag":"hold","reasons":"spam"}""")]
    [InlineData("""{"flag":"hold","reasons":[1]}""")]
    [InlineData("""["hold"]""")]
    [InlineData("not json")]
    [InlineData("")]
    public void Anything_else_is_invalid_with_a_fixed_error_that_does_not_echo_the_reply(string reply)
    {
        var (d, e) = TriageValidator.Validate(reply);
        Assert.Null(d);
        Assert.False(string.IsNullOrWhiteSpace(e));
        if (reply.Length > 3) Assert.DoesNotContain(reply, e!);
    }

    [Fact]
    public void The_error_never_names_the_reason_the_model_produced()
    {
        var (_, e) = TriageValidator.Validate("""{"flag":"hold","reasons":["demo-marker-xyz"]}""");
        Assert.DoesNotContain("demo-marker-xyz", e!);
    }
}
