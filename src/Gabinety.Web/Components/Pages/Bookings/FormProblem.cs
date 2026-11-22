using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Bookings;

/// <summary>One problem of a form: the id of the field it belongs to and the message (Polish, what to do, never a code).</summary>
public sealed record FormProblem(string FieldId, string Message);

public static class FormProblemText
{
    /// <summary>"Popraw 1 pole", "Popraw 2 pola" (2-4, but not 12-14), "Popraw 5 pól".</summary>
    public static string Heading(int count) => count == 1 ? BookFlowText.Errors.SummaryOne
        : string.Format(count % 10 is >= 2 and <= 4 && count % 100 is < 12 or > 14 ? BookFlowText.Errors.SummaryFew : BookFlowText.Errors.SummaryMany, count);
}
