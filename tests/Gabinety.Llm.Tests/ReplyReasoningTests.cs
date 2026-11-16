namespace Gabinety.Llm.Tests;

public class ReplyReasoningTests
{
    [Fact]
    public void The_final_messages_reasoning_wins() =>
        Assert.Equal("From the message.", ReplyReasoning.Choose("From the message.", ["From an event."]));

    [Fact]
    public void Without_message_reasoning_the_blocks_are_joined_in_order() =>
        Assert.Equal("First.\n\nSecond.", ReplyReasoning.Choose(null, ["First.", "Second."]));

    [Fact]
    public void Blank_input_is_skipped_and_nothing_is_null()
    {
        Assert.Equal("Only.", ReplyReasoning.Choose("   ", ["", " ", "Only."]));
        Assert.Null(ReplyReasoning.Choose(null, []));
    }
}
