using Bunit;
using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Practitioners;

namespace Gabinety.Web.Tests.Rendering;

/// <summary>T-11.05: the list card shows up to three technologies as neutral badges and the next free time in Warsaw time, only when the Directory sent one.</summary>
public class PractitionerCardSortTests : BunitContext
{
    static readonly DateTimeOffset Now = new(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);

    static readonly IReadOnlyDictionary<string, string> Techs = new Dictionary<string, string>
    {
        ["dotnet"] = ".NET", ["react"] = "React", ["sql"] = "SQL", ["docker"] = "Docker",
    };

    static PractitionerCardDto Card(IReadOnlyList<string>? techs = null, DateTimeOffset? nextFree = null) =>
        Sample.Card() with { TechSlugs = techs, NextFreeSlotUtc = nextFree };

    [Fact]
    public void Up_to_three_known_technologies_are_neutral_badges_and_an_unknown_slug_is_left_out()
    {
        var cut = Render<PractitionerCard>(p => p.Add(c => c.Practitioner, Card(["nieznany", "dotnet", "react", "sql", "docker"]))
            .Add(c => c.TechNames, Techs).Add(c => c.Now, Now));
        var badges = cut.FindAll(".practitioner-card__techs .badge");
        Assert.Equal([".NET", "React", "SQL"], badges.Select(b => b.TextContent));
        Assert.All(badges, b => Assert.Contains("badge--neutral", b.ClassName));
    }

    [Fact]
    public void A_card_without_technologies_or_without_names_for_them_shows_no_technology_row()
    {
        var none = Render<PractitionerCard>(p => p.Add(c => c.Practitioner, Card()).Add(c => c.TechNames, Techs).Add(c => c.Now, Now));
        Assert.Empty(none.FindAll(".practitioner-card__techs"));
        var unnamed = Render<PractitionerCard>(p => p.Add(c => c.Practitioner, Card(["dotnet"])).Add(c => c.Now, Now));
        Assert.Empty(unnamed.FindAll(".practitioner-card__techs"));
    }

    [Fact]
    public void A_next_free_slot_reads_as_a_warsaw_day_and_time_with_the_instant_in_the_time_element()
    {
        // 2026-11-24 08:30 UTC is 09:30 in Warsaw (CET), tomorrow relative to the page's clock
        var cut = Render<PractitionerCard>(p => p.Add(c => c.Practitioner, Card(nextFree: new DateTimeOffset(2026, 11, 24, 8, 30, 0, TimeSpan.Zero)))
            .Add(c => c.Now, Now));
        var line = cut.Find(".practitioner-card__next");
        Assert.Equal("Najbliższy termin: jutro, 09:30", line.TextContent.Trim());
        var time = cut.Find(".practitioner-card__next time");
        Assert.Equal("2026-11-24T08:30:00Z", time.GetAttribute("datetime"));
        Assert.Equal("2026-11-24T08:30:00Z", time.GetAttribute("data-utc"));
    }

    [Fact]
    public void A_card_without_a_next_free_slot_shows_no_line()
    {
        var cut = Render<PractitionerCard>(p => p.Add(c => c.Practitioner, Card()).Add(c => c.Now, Now));
        Assert.Empty(cut.FindAll(".practitioner-card__next"));
    }
}
