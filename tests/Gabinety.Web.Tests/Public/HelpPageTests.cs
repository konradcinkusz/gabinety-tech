using System.Net;

namespace Gabinety.Web.Tests.Public;

/// <summary>T-11.18: the help page (<c>/pomoc</c>): grouped native answers, and the contact sentence only when <c>Support:Email</c> is configured.</summary>
public class HelpPageTests
{
    [Fact]
    public async Task With_a_support_address_the_page_offers_the_mailto_link_and_the_contact_sentence()
    {
        await using var web = PublicSupport.Host().With("Support:Email", "pomoc@example.test");
        var page = await web.NewBrowser().GetAsync("/pomoc");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Single(page.All("h1"));
        Assert.Equal("Pomoc", page.TextOf("h1"));

        var link = page.One("a[href='mailto:pomoc@example.test']")!;
        Assert.Equal("Napisz do nas.", link.TextContent);
        Assert.Equal("Nie znalazłeś odpowiedzi? Napisz do nas.", link.ParentElement!.TextContent.Trim());
    }

    [Fact]
    public async Task Without_a_support_address_the_page_shows_neither_the_sentence_nor_a_mailto_link()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/pomoc");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Single(page.All("h1"));
        Assert.Null(page.One("a[href^='mailto:']"));
        Assert.DoesNotContain("Nie znalazłeś odpowiedzi?", page.Html);
    }

    [Fact]
    public async Task The_answers_are_native_details_grouped_by_topic_and_carry_no_number_of_their_own()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/pomoc");
        Assert.Equal(6, page.All(".prose > section").Count); // booking and payment, cancelling and refunds, disputes, reviews, accounts and data, programmers
        Assert.All(page.All(".prose > section > h2"), h => Assert.False(string.IsNullOrWhiteSpace(h.TextContent)));
        var details = page.All(".faq details");
        Assert.True(details.Count >= 12);
        Assert.All(details, d =>
        {
            Assert.Single(d.GetElementsByTagName("summary"));
            Assert.False(string.IsNullOrWhiteSpace(d.QuerySelector("p")?.TextContent));
        });
        // money and windows are linked to /cennik and /jak-to-dziala, never stated on this page
        Assert.DoesNotMatch(@"\d", page.One(".prose")!.TextContent);
    }

    [Fact]
    public async Task The_help_page_is_linked_from_the_footer()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/pomoc");
        Assert.NotNull(page.One("footer a[href='/pomoc']"));
    }
}
