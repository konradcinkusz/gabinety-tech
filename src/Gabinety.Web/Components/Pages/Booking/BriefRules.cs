using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Booking;

/// <summary>CONTRACTS 4.2: the brief is 20 to 2000 characters after trimming. The same rule the Booking service enforces, applied first so the visitor sees every problem at once, in Polish.</summary>
public static class BriefRules
{
    public const int Min = 20, Max = 2000;

    /// <summary>The message under the field, or null when the brief is acceptable.</summary>
    public static string? Problem(string? text)
    {
        var n = (text ?? "").Trim().Length;
        return n < Min ? BookFlowText.Brief.TooShort : n > Max ? BookFlowText.Brief.TooLong : null;
    }

    public static bool CanAskAssistant(string? text) => (text ?? "").Trim().Length >= Min;
}
