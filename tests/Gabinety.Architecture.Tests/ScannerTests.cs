namespace Gabinety.Architecture.Tests;

/// <summary>The guards are only worth trusting if their scanners are precise; these pin the cases the task names.</summary>
public class ScannerTests
{
    static IReadOnlyList<string> Flagged(string code) => CodeScan.FreeTextInLogCalls(CodeScan.Sanitize(code)).Select(h => h.Name).ToList();

    [Fact] public void A_length_is_not_flagged() => Assert.Empty(Flagged("""logger.LogInformation("n={Length}", text.Length);"""));
    [Fact] public void A_Text_length_is_not_flagged() => Assert.Empty(Flagged("""_logger.LogWarning("n={Length}", reply.Text.Length);"""));
    [Fact] public void A_member_named_Text_is_flagged() => Assert.Equal(["Text"], Flagged("""logger.LogInformation("got {Text}", brief.Text);"""));
    [Fact] public void Nested_calls_and_Reply_are_flagged() => Assert.Equal(["Reply"], Flagged("""_log.LogError(ex, "x {A}", Hash(r.Reply));"""));
    [Fact] public void Interpolated_holes_are_flagged() => Assert.Equal(["Bio"], Flagged("""logger.LogDebug($"bio {p.Bio}");"""));
    [Fact] public void Text_in_a_string_or_comment_is_not_flagged() =>
        Assert.Empty(Flagged("logger.LogInformation(\"the Text and Reply\"); // logger.LogError(Brief)\n/* logger.LogError(Bio) */"));
    [Fact] public void Non_logging_calls_are_not_flagged() => Assert.Empty(Flagged("var s = Compose(brief.Text);"));
    [Fact] public void Comments_are_stripped_but_clock_in_code_is_kept() =>
        Assert.Contains("DateTime.UtcNow", CodeScan.Sanitize("// DateTime.UtcNow\nvar t = DateTime.UtcNow;"));
}
