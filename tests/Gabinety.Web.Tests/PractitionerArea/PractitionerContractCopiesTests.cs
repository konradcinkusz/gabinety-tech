using System.Text.Json;
using System.Text.Json.Serialization;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using W = Gabinety.Web.Api;

namespace Gabinety.Web.Tests.PractitionerAreaTests;

/// <summary>
/// The practitioner area's DTO copies against the real contracts, in both directions: what the services answer must read into the copies, and what the web sends must read into the services' own
/// request types with the same values (a renamed field fails here and not as a blank column or a silently dropped value).
/// </summary>
public class PractitionerContractCopiesTests
{
    static readonly JsonSerializerOptions Services = new(JsonSerializerDefaults.Web); // the services write PractitionerStatus as its PascalCase name (ADR-0012 section 1)
    static readonly Guid Id = Guid.Parse("018f0000-0000-7000-8000-000000000002");

    static TCopy Read<TCopy>(object real) => JsonSerializer.Deserialize<TCopy>(JsonSerializer.Serialize(real, real.GetType(), Services), W.ApiClient.Json)!;

    static TReal Send<TReal>(object copy) => JsonSerializer.Deserialize<TReal>(JsonSerializer.Serialize(copy, copy.GetType(), W.ApiClient.Json), Services)!;

    [Fact]
    public void Me_and_the_private_profile_read_into_the_copies_with_the_status_as_its_wire_name()
    {
        var me = Read<W.MeDto>(new MeDto("u1", Id, PractitionerStatus.Submitted, false, true));
        Assert.Equal(("u1", Id, "Submitted", false, true), (me.UserId, me.PractitionerId, me.PractitionerStatus, me.IsAdmin, me.IsModerator));
        var p = Read<W.PractitionerPrivateDto>(new PractitionerPrivate(Id, "demo-x", PractitionerStatus.Rejected, "Powód", "Demo X", "Nagłówek", "Opis", "https://x.example.com/", "https://meet.google.com/a", "5260250274", true, true, true, "demo-gh"));
        Assert.Equal(("Rejected", "Powód", "demo-x", "demo-gh", "5260250274"), (p.Status, p.StatusReason, p.Slug, p.GithubLogin, p.Nip));
        Assert.True(p.EmailVerified && p.GithubLinked && p.NipValid);
        Assert.Equal("https://meet.google.com/a", p.MeetingUrlDefault);
        Assert.Equal("https://x.example.com/", p.PortfolioUrl);
        Assert.Equal("Opis", p.Bio);
    }

    [Fact]
    public void An_offer_reads_into_the_copy_and_the_list_is_wrapped_in_items()
    {
        var offer = new OfferingPrivate(Id, "Tytuł", "Opis", 60, new MoneyDto(18050), "code-review", ["dotnet"], false, "https://meet.google.com/a");
        var o = Read<W.OfferingPrivateDto>(offer);
        Assert.Equal(("Tytuł", 60, 18050L, "PLN", "code-review", false, "https://meet.google.com/a"), (o.Title, o.DurationMinutes, o.Price.AmountMinor, o.Price.Currency, o.ProblemSlug, o.IsActive, o.MeetingUrl));
        Assert.Equal(["dotnet"], o.TechSlugs);
        var list = Read<W.OfferingsDto>(new { items = new[] { offer } });
        Assert.Equal(Id, list.Items.Single().Id);
    }

    [Fact]
    public void What_the_web_sends_reads_into_the_services_own_request_types_with_the_same_values()
    {
        var create = Send<CreatePractitionerRequest>(new W.CreatePractitionerBody("Demo Jedyny"));
        Assert.Equal("Demo Jedyny", create.DisplayName);
        var update = Send<UpdatePractitionerRequest>(new W.UpdatePractitionerBody("Demo", "Nagłówek", "Opis", "https://x.example.com/", null, "5260250274"));
        Assert.Equal(("Demo", "Nagłówek", "Opis", "https://x.example.com/", null, "5260250274"), (update.DisplayName, update.Headline, update.Bio, update.PortfolioUrl, update.MeetingUrlDefault, update.Nip));
        var offer = Send<OfferingRequest>(new W.OfferingBody("Tytuł oferty", "Opis", 60, 18050, "code-review", ["dotnet", "react"], null));
        Assert.Equal(("Tytuł oferty", "Opis", 60, 18050L, "code-review", null), (offer.Title, offer.Description, offer.DurationMinutes, offer.PriceMinor, offer.ProblemSlug, offer.MeetingUrl));
        Assert.Equal(["dotnet", "react"], offer.TechSlugs);
    }

    [Fact]
    public void An_empty_optional_field_is_omitted_from_the_json_not_sent_as_null()
    {
        var json = JsonSerializer.Serialize(new W.UpdatePractitionerBody("Demo", "H", "B", null, null, null), W.ApiClient.Json);
        Assert.DoesNotContain("portfolioUrl", json);
        Assert.DoesNotContain("null", json);
    }

    [Fact]
    public void Availability_both_ways_with_the_blocked_day_as_a_date_string()
    {
        var real = new AvailabilityDto([new AvailabilityRuleDto(3, "09:00", "10:30")], [new AvailabilityExceptionDto(new DateOnly(2027, 1, 6))]);
        var copy = Read<W.AvailabilityDto>(real);
        Assert.Equal((3, "09:00", "10:30", "2027-01-06"), (copy.Rules[0].IsoWeekday, copy.Rules[0].StartLocal, copy.Rules[0].EndLocal, copy.Exceptions[0].DateLocal));
        var sent = Send<AvailabilityDto>(copy);
        Assert.Equal(real.Rules, sent.Rules);
        Assert.Equal(real.Exceptions, sent.Exceptions);
    }
}
