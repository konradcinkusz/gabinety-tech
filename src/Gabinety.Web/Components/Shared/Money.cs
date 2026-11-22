using System.Globalization;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Shared;

/// <summary>Money is an integer grosz everywhere (SPEC 5 rule 3): formatted with integer arithmetic only, as in <c>web/src/components/ui/format.ts</c>.</summary>
public static class Money
{
    const char NoBreakSpace = ' ';

    /// <summary><c>250,00 zł</c>; thousands and the currency are separated by U+00A0. A negative amount is a programming error, not a price.</summary>
    public static string Pln(long grosz)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(grosz);
        var whole = (grosz / 100).ToString(CultureInfo.InvariantCulture);
        var grouped = new System.Text.StringBuilder();
        for (var i = 0; i < whole.Length; i++)
        {
            if (i > 0 && (whole.Length - i) % 3 == 0) grouped.Append(NoBreakSpace);
            grouped.Append(whole[i]);
        }
        return $"{grouped},{grosz % 100:00}{NoBreakSpace}{Pl.Format.Currency}";
    }
}
