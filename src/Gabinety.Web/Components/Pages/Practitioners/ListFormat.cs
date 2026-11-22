using Gabinety.Web.Components.Shared;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Practitioners;

/// <summary>
/// The count and the price as the practitioner list shows them. Plain C#, no Polish literal here: the words are in <see cref="Pl.ListPage"/>.
/// </summary>
public static class ListFormat
{
    /// <summary><c>1 programista</c>; every other number, zero and the teens included, takes <c>programistów</c> (the masculine personal noun in the genitive plural).</summary>
    public static string PractitionerCount(int count) =>
        string.Format(Pl.ListPage.CountPhrase, count, count == 1 ? Pl.ListPage.PractitionerOne : Pl.ListPage.PractitionerMany);

    /// <summary><c>120 zł</c> for a whole amount, <c>120,50 zł</c> otherwise. Integer grosz in, the grosz only shown when there are some.</summary>
    public static string Price(long grosz) => grosz % 100 == 0 ? Money.Pln(grosz).Replace(",00", "") : Money.Pln(grosz);
}
