namespace Gabinety.Web.Components.Pages.Gabinet;

/// <summary>What the page knows of one offer's free slots: the days (when the read worked) or that the read failed. Three different states, three different sentences (F-16).</summary>
public sealed record OfferSlots(bool Available, IReadOnlyList<DayGroup> Days)
{
    public static OfferSlots Failed { get; } = new(false, []);
}
