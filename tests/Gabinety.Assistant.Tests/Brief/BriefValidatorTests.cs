using System.Text.Json;
using Gabinety.Assistant.Features.Brief;
using Gabinety.Contracts.Assistant;

namespace Gabinety.Assistant.Tests.Brief;

public class BriefValidatorTests
{
    static string Json(string? problem = "audyt-kodu", string[]? tech = null, string summary = "Opis.", string[]? q = null, int minutes = 30, string conf = "low") =>
        JsonSerializer.Serialize(new { problemSlug = problem, techSlugs = tech ?? [], summary, clarifyingQuestions = q ?? [], suggestedDurationMinutes = minutes, confidence = conf });

    [Fact]
    public void A_valid_reply_gives_a_draft_and_a_fenced_one_too()
    {
        var (d, e) = BriefValidator.Validate(Json(tech: ["dotnet", "llm"], q: ["Kiedy?"], minutes: 60, conf: "high"), StubTaxonomy.Full);
        Assert.Null(e);
        Assert.Equal(("audyt-kodu", 2, 60, Confidence.High), (d!.ProblemSlug, d.TechSlugs.Count, d.SuggestedDurationMinutes, d.Confidence));
        Assert.NotNull(BriefValidator.Validate("```json\n" + Json() + "\n```", StubTaxonomy.Full).Draft);
    }

    public static TheoryData<string> Bad => new()
    {
        "not json", "[]", "null", "", Json(problem: null), Json(problem: "x"), Json(tech: ["dotnet", "dotnet"]),
        Json(tech: ["dotnet", "csharp", "azure", "python", "react"]), Json(tech: ["audyt-kodu"]), Json(summary: ""), Json(summary: new string('a', 301)),
        Json(q: ["a", "b", "c", "d"]), Json(q: [new string('a', 141)]), Json(q: [""]), Json(minutes: 45), Json(conf: "certain"), Json(conf: "High"),
        """{"problemSlug":"audyt-kodu"}""", """{"problemSlug":"audyt-kodu","techSlugs":"dotnet","summary":"a","clarifyingQuestions":[],"suggestedDurationMinutes":30,"confidence":"low"}""",
    };

    [Theory, MemberData(nameof(Bad))]
    public void An_invalid_reply_gives_an_error_text_that_never_echoes_the_reply(string reply)
    {
        var (d, e) = BriefValidator.Validate(reply, StubTaxonomy.Full);
        Assert.Null(d);
        Assert.False(string.IsNullOrWhiteSpace(e));
        Assert.True(e.Length < 200);
        if (reply.Length > 3) Assert.DoesNotContain(reply, e);
    }

    [Fact]
    public void An_empty_taxonomy_accepts_nothing() =>
        Assert.Null(BriefValidator.Validate(Json(), new Gabinety.Contracts.Directory.TaxonomyDto("", [], [])).Draft);
}
