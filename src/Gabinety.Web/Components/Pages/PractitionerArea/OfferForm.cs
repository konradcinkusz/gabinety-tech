using Gabinety.Web.Api;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.PractitionerArea;

/// <summary>
/// The offer form as posted. It validates what is cheap to say at once (so every problem is shown together, G-9) and leaves the rest to the service, which stays the authority
/// (<see cref="FieldErrors"/>). Money: the price is typed in zloty and leaves here as integer grosz (<see cref="PriceInput"/>).
/// </summary>
public sealed record OfferForm(string Title, string Description, string Duration, string Price, string Problem, IReadOnlyList<string> Techs, string Meeting)
{
    public const int MaxActive = 5, MaxTechs = 4;
    public static readonly OfferForm Empty = new("", "", "30", "", "", [], "");

    /// <summary>The service's field names in the order of the form.</summary>
    public static readonly string[] FieldOrder = ["title", "description", "durationMinutes", "priceMinor", "problemSlug", "techSlugs", "meetingUrl"];

    /// <summary>The id of the control of a service field.</summary>
    public static string IdOf(string field) => field switch { "durationMinutes" => "duration", "priceMinor" => "price", _ => field };

    public static OfferForm From(OfferingPrivateDto o) => new(o.Title, o.Description, o.DurationMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture), PriceInput.ToInput(o.Price.AmountMinor), o.ProblemSlug, o.TechSlugs, o.MeetingUrl ?? "");

    public static OfferForm FromPosted(FormValues f) => new(f.Text("title"), f.Text("description"), f.Text("duration"), f.Text("price"), f.Text("problemSlug"), f.All("techs", MaxTechs + 8), f.Text("meetingUrl"));

    public IReadOnlyDictionary<string, string> Validate()
    {
        var errors = new Dictionary<string, string>();
        if (Title.Length is < 5 or > 80) errors["title"] = PlPractitioner.Errors.ByField["title"];
        if (Description.Length > 600) errors["description"] = PlPractitioner.Errors.ByField["description"];
        if (Duration is not ("30" or "60")) errors["durationMinutes"] = PlPractitioner.Errors.ByField["durationMinutes"];
        switch (PriceInput.Parse(Price).Problem)
        {
            case PriceProblem.Format: errors["priceMinor"] = PlPractitioner.Offers.PriceFormat; break;
            case PriceProblem.Range: errors["priceMinor"] = PlPractitioner.Offers.PriceRange; break;
        }
        if (Problem.Length == 0) errors["problemSlug"] = PlPractitioner.Errors.ByField["problemSlug"];
        if (Techs.Count > MaxTechs) errors["techSlugs"] = PlPractitioner.Errors.ByField["techSlugs"];
        return errors;
    }

    /// <summary>The body for the service; call only when <see cref="Validate"/> found nothing.</summary>
    public OfferingBody ToBody() => new(Title, Description, Duration == "60" ? 60 : 30, PriceInput.Parse(Price).Grosz, Problem, Techs, Meeting.Length == 0 ? null : Meeting);
}
